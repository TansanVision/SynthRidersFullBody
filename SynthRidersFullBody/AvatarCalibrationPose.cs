using Il2CppRootMotion.FinalIK;
using UnityEngine;

namespace SynthRidersFullBody;

// One immutable reference per loaded avatar. Never learn a new reference from
// the output of a previous full-body solve.
/// <summary>
/// 読み込み時の骨の位置と基準回転を保持し、校正プレビューの固定・サイズ調整・復元を行います。
/// </summary>
internal sealed class AvatarCalibrationPose
{
    /// <summary>
    /// 1本のボーンについて保存したローカル位置と回転です。
    /// </summary>
    /// <param name="Bone">保存する姿勢の対象ボーン。</param>
    /// <param name="Position">ボーンのローカル位置。</param>
    /// <param name="Rotation">ボーンのローカル回転。</param>
    private readonly record struct BonePose(Transform Bone, Vector3 Position, Quaternion Rotation)
    {
        /// <summary>
        /// 保存したローカル位置・回転を、存在するボーンへ適用します。
        /// </summary>
        public void Apply()
        {
            if (Bone == null) return;
            Bone.localPosition = Position;
            Bone.localRotation = Rotation;
        }
    }

    private readonly Animator _animator;
    /// <summary>左右の掌面を骨格情報から標準方向へ合わせられたかを示します。</summary>
    public string PalmStatus { get; private set; } = "not captured";
    private readonly BonePose[] _reference;
    private readonly float _rootLocalY;
    private readonly Quaternion _rootLocalRotation;
    private BonePose[]? _restore;
    private Vector3 _restoreRootPosition;
    private Quaternion _restoreRootRotation;
    private Vector3 _restoreRootScale;
    private Vector3 _fixedScale;
    private Vector3 _fixedPosition;
    private Quaternion _fixedRotation;
    private bool _animatorEnabled;
    private VRIK? _ik;
    private bool _ikEnabled;
    private VRIKRootController? _rootController;
    private bool _rootControllerEnabled;
    /// <summary>
    /// 復元用の姿勢を保持して校正プレビューを表示しているかを示します。
    /// </summary>
    public bool IsActive => _restore != null;
    /// <summary>
    /// 基準回転の取得元と取得できたボーン数を示す診断文です。
    /// </summary>
    public string Source { get; }
    /// <summary>
    /// ボーンの相対パスと基準位置から作った、校正復帰時の骨格識別文字列です。
    /// </summary>
    public string RigSignature { get; }
    /// <summary>
    /// 現在のプレビューでユーザーの高さに合わせたサイズ調整が完了したかを示します。
    /// </summary>
    public bool SizeFitted { get; private set; }
    /// <summary>
    /// サイズ調整の結果、または調整を待つ理由を示します。
    /// </summary>
    public string SizeStatus { get; private set; } = "Stand upright with both feet on the floor";

    /// <summary>
    /// Animatorのルートから指定ボーンまでの相対パスを作成します。
    /// </summary>
    /// <param name="bone">対象ボーンのTransform。</param>
    /// <returns>スラッシュ区切りのTransform相対パス。</returns>
    private string PathOf(Transform bone)
    {
        var names = new Stack<string>();
        while (bone != null && bone != _animator.transform) { names.Push(bone.name); bone = bone.parent; }
        return string.Join("/", names);
    }

    /// <summary>
    /// 基準姿勢の管理対象ボーンについて、現在のローカル姿勢を値として保存します。
    /// </summary>
    /// <returns>Unityオブジェクトを参照しない復帰用ボーン姿勢の配列。</returns>
    public CalibrationBone[] CaptureBones() => _reference.Select(p =>
        new CalibrationBone(PathOf(p.Bone), p.Bone.localPosition, p.Bone.localRotation)).ToArray();

    /// <summary>
    /// 保存済みの校正姿勢と実寸を一時適用して校正処理を実行し、表示中の姿勢を復元します。
    /// </summary>
    /// <param name="saved">最初に成功した校正から保持している復帰用データ。</param>
    /// <param name="rootPose">一時的に適用するモデルルートのワールド姿勢。</param>
    /// <param name="calibrate">基準姿勢を適用した状態で同期的に実行する校正処理。</param>
    /// <returns>全ボーンを解決でき、校正処理が成功した場合はtrue。</returns>
    /// <remarks>全パスを解決してから姿勢を変更し、同じ呼び出し内のfinallyで表示姿勢を復元します。成功時は保存した実寸を保持し、失敗時はスケールも元へ戻します。</remarks>
    public bool WithReference(CalibrationSession saved, Pose rootPose, Func<bool> calibrate)
    {
        // Resolve all paths before touching the rig. The enclosing call stays
        // synchronous so the temporary reference pose is never rendered.
        var bones = saved.Bones.Select(p => _animator.transform.Find(p.Path)).ToArray();
        if (bones.Any(b => b == null)) return false;
        var restore = bones.Select(b => new BonePose(b, b.localPosition, b.localRotation)).ToArray();
        var root = _animator.transform;
        var position = root.position;
        var rotation = root.rotation;
        var scale = root.localScale;
        var success = false;
        try
        {
            // Reinstated/reparented rigs retain the physical size accepted at
            // calibration, even when the game has applied a new uniform scale.
            root.localScale *= saved.Scale.y / root.lossyScale.y;
            root.SetPositionAndRotation(rootPose.Position, rootPose.Rotation);
            for (var i = 0; i < bones.Length; i++)
            {
                bones[i].localPosition = saved.Bones[i].Position;
                bones[i].localRotation = saved.Bones[i].Rotation;
            }
            success = calibrate();
            return success;
        }
        finally
        {
            if (root != null)
            {
                if (!success) root.localScale = scale;
                root.SetPositionAndRotation(position, rotation);
            }
            foreach (var pose in restore) pose.Apply();
        }
    }

    /// <summary>
    /// 最初の骨格状態を採取し、単位変換済みの骨の位置を保った基準姿勢を構築します。
    /// </summary>
    /// <param name="animator">対象HumanoidアバターのAnimator。</param>
    public AvatarCalibrationPose(Animator animator)
    {
        _animator = animator;
        _rootLocalY = animator.transform.localPosition.y;
        _rootLocalRotation = animator.transform.localRotation;
        var skeleton = new Dictionary<string, SkeletonBone>(StringComparer.Ordinal);
        var duplicateNames = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var bone in animator.avatar.humanDescription.skeleton)
                if (!string.IsNullOrEmpty(bone.name) && !skeleton.TryAdd(bone.name, bone)) duplicateNames.Add(bone.name);
        }
        catch { /* Older avatars may have no HumanDescription; retain the first pose once. */ }
        foreach (var name in duplicateNames) skeleton.Remove(name);
        var transforms = new Dictionary<int, Transform>();
        for (var i = 0; i < (int)HumanBodyBones.LastBone; i++)
        {
            var bone = animator.GetBoneTransform((HumanBodyBones)i);
            while (bone != null && bone != animator.transform)
            {
                transforms.TryAdd(bone.GetInstanceID(), bone);
                bone = bone.parent;
            }
        }
        var count = 0;
        _reference = transforms.Values.Select(bone =>
        {
            if (skeleton.TryGetValue(bone.name, out var rest) && ValidRotation(rest.rotation))
            {
                count++;
                // HumanDescription may retain importer units (e.g. cm) while
                // runtime transforms are already converted. Keep the loaded
                // bone lengths and hierarchy offsets; use only rest rotation.
                return new BonePose(bone, bone.localPosition, rest.rotation);
            }
            return new BonePose(bone, bone.localPosition, bone.localRotation);
        }).ToArray();
        Source = $"runtime bone lengths; skeleton rest rotations {count}/{_reference.Length}";
        RigSignature = string.Join("|", _reference.Select(p =>
        {
            // Asset coordinates remain useful as an identity, never as a
            // replacement for runtime bone translations.
            var position = skeleton.TryGetValue(p.Bone.name, out var rest) ? rest.position : p.Position;
            return FormattableString.Invariant($"{PathOf(p.Bone)}:{position.x:R},{position.y:R},{position.z:R}");
        })
            .OrderBy(p => p, StringComparer.Ordinal));
    }

    /// <summary>
    /// アニメーションとIKを一時停止し、選択した校正姿勢を固定表示します。
    /// </summary>
    /// <param name="camera">ゲーム内のHMD視点として使用するカメラ。</param>
    /// <param name="forward">入力位置と同じ座標系におけるユーザーまたはアバターの正面方向。</param>
    /// <param name="ik">対象アバターのVRIK。見つからない場合はnull。</param>
    /// <param name="tPose">腕を水平に広げるTポーズを使用するか。falseはStandingです。</param>
    /// <param name="canFit">直立・追跡条件を満たし、現在の入力でサイズを合わせられるか。</param>
    /// <param name="leftFoot">左足の主入力のワールド姿勢。</param>
    /// <param name="rightFoot">右足の主入力のワールド姿勢。</param>
    /// <remarks>初回に復元値を採取し、その後は固定したワールド位置・回転とローカルスケールを再適用します。頭の動きで基準姿勢を更新しません。</remarks>
    public void Show(Camera camera, Vector3 forward, VRIK? ik, bool tPose,
        bool canFit, Pose leftFoot, Pose rightFoot)
    {
        if (_animator == null) return;
        if (!IsActive)
        {
            _restore = _reference.Select(p => new BonePose(p.Bone,
                p.Bone.localPosition, p.Bone.localRotation)).ToArray();
            var root = _animator.transform;
            _restoreRootPosition = root.position;
            _restoreRootRotation = root.rotation;
            _restoreRootScale = _fixedScale = root.localScale;
            SizeFitted = false;
            SizeStatus = "Stand upright with both feet on the floor";
            _animatorEnabled = _animator.enabled;
            _ik = ik;
            _ikEnabled = ik != null && ik.enabled;
            _rootController = ik?.references?.root?.GetComponent<VRIKRootController>();
            _rootControllerEnabled = _rootController != null && _rootController.enabled;
            DisableDrivers();
            foreach (var pose in _reference) pose.Apply();
            root.localRotation = _rootLocalRotation;
            var flatForward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            if (flatForward.sqrMagnitude < 0.001f) flatForward = Vector3.forward;
            var currentForward = Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized;
            _fixedRotation = Quaternion.FromToRotation(currentForward, flatForward) * root.rotation;
            root.rotation = _fixedRotation;
            var local = root.localPosition;
            local.y = _rootLocalY;
            root.localPosition = local;
            var head = _animator.GetBoneTransform(HumanBodyBones.Head);
            var horizontalOffset = head == null ? Vector3.zero :
                Vector3.ProjectOnPlane(camera.transform.position - head.position, Vector3.up);
            _fixedPosition = root.position + horizontalOffset;
        }
        DisableDrivers();
        _animator.transform.localScale = _fixedScale;
        _animator.transform.SetPositionAndRotation(_fixedPosition, _fixedRotation);
        foreach (var pose in _reference) pose.Apply();
        if (canFit && !SizeFitted) FitSize(camera, leftFoot, rightFoot);
        var right = Vector3.ProjectOnPlane(_animator.transform.right, Vector3.up).normalized;
        // Both modes start from the same immutable skeleton. Standing lowers
        // the arms slightly outward so hands do not intersect the thighs.
        var leftDirection = tPose ? -right : (Vector3.down - right * 0.12f).normalized;
        var rightDirection = tPose ? right : (Vector3.down + right * 0.12f).normalized;
        AlignArm(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, leftDirection);
        AlignArm(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, rightDirection);
        var leftPalm = AlignPalm(true, leftDirection, tPose ? Vector3.down : right);
        var rightPalm = AlignPalm(false, rightDirection, tPose ? Vector3.down : -right);
        PalmStatus = $"left={(leftPalm ? "standard" : "reference fallback")}, right={(rightPalm ? "standard" : "reference fallback")}";
    }

    /// <summary>指の配置またはVRIKの手軸から掌面を求め、Tポーズでは下、直立では体側へ向けます。</summary>
    private bool AlignPalm(bool left, Vector3 fingersDirection, Vector3 palmDirection)
    {
        var hand = _animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        if (hand == null) return false;
        var index = _animator.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
        var little = _animator.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
        var middle = _animator.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
        var inverse = Quaternion.Inverse(hand.rotation);
        if (index != null && little != null)
        {
            var first = index.position - hand.position;
            var last = little.position - hand.position;
            if (PalmPoseMath.TryFingerReference(left, N(inverse * first), N(inverse * last),
                middle != null ? N(inverse * (middle.position - hand.position)) : null,
                N(fingersDirection), N(palmDirection), out var rotation))
            {
                hand.rotation = new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);
                return true;
            }
        }
        var arm = left ? _ik?.solver?.leftArm : _ik?.solver?.rightArm;
        return arm != null && TrySetPalm(hand, arm.wristToPalmAxis,
            Vector3.Cross(arm.wristToPalmAxis, arm.palmToThumbAxis) * (left ? -1f : 1f), fingersDirection, palmDirection);
    }

    /// <summary>有効な掌面の基底がある場合だけ手ボーンを回し、位置や指の形は変えません。</summary>
    private static bool TrySetPalm(Transform hand, Vector3 localFingers, Vector3 localPalm, Vector3 fingers, Vector3 palm)
    {
        if (!PalmPoseMath.TryRotation(N(localFingers), N(localPalm), N(fingers), N(palm), out var rotation)) return false;
        hand.rotation = new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);
        return true;
    }

    /// <summary>
    /// 頭と左右足の高さから全体スケールと位置を調整し、プレビューの固定値を更新します。
    /// </summary>
    /// <param name="camera">ゲーム内のHMD視点として使用するカメラ。</param>
    /// <param name="left">左足のワールド姿勢。</param>
    /// <param name="right">右足のワールド姿勢。</param>
    private void FitSize(Camera camera, Pose left, Pose right)
    {
        var head = _animator.GetBoneTransform(HumanBodyBones.Head);
        var leftFoot = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        var rightFoot = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
        if (head == null || leftFoot == null || rightFoot == null) return;
        // Prefer eye bones for headset alignment. Rigs without eyes use the
        // native head target's joint position when it is near the headset.
        var leftEye = _animator.GetBoneTransform(HumanBodyBones.LeftEye);
        var rightEye = _animator.GetBoneTransform(HumanBodyBones.RightEye);
        var modelHead = leftEye != null && rightEye != null
            ? (leftEye.position + rightEye.position) * 0.5f : head.position;
        var userHead = camera.transform.position;
        var headTarget = _ik?.solver?.spine?.headTarget;
        if ((leftEye == null || rightEye == null) && headTarget != null &&
            Vector3.Distance(headTarget.position, userHead) < 0.3f) userHead = headTarget.position;
        var root = _animator.transform;
        if (!AvatarScaleMath.TryFit(N(root.position), N(root.localScale), N(modelHead),
            N(leftFoot.position), N(rightFoot.position), N(userHead), N(left.Position), N(right.Position),
            out var position, out var scale, out var factor))
        {
            SizeStatus = "Cannot fit avatar size; stand upright and check foot roles";
            return;
        }
        _fixedPosition = new Vector3(position.X, position.Y, position.Z);
        _fixedScale = new Vector3(scale.X, scale.Y, scale.Z);
        root.localScale = _fixedScale;
        root.position = _fixedPosition;
        SizeFitted = true;
        SizeStatus = $"Avatar size fitted x{factor:0.###}; world scale={root.lossyScale}";
    }

    /// <summary>
    /// Unityの数学型を、成分の値を保ってSystem.Numericsの型へ変換します。
    /// </summary>
    /// <param name="v">変換または検証するベクトル。</param>
    /// <returns>同じ成分を持つSystem.Numericsの値。</returns>
    private static System.Numerics.Vector3 N(Vector3 v) => new(v.x, v.y, v.z);

    /// <summary>
    /// 基準姿勢に使用できる有限値と大きさを持つQuaternionかを確認します。
    /// </summary>
    /// <param name="q">変換または検証するQuaternion。</param>
    /// <returns>全成分が有限でノルムの二乗が0.5より大きければtrue。</returns>
    private static bool ValidRotation(Quaternion q) => float.IsFinite(q.x) && float.IsFinite(q.y) &&
        float.IsFinite(q.z) && float.IsFinite(q.w) && Quaternion.Dot(q, q) > 0.5f;

    /// <summary>
    /// 次の有効な直立姿勢でサイズを取り直せるよう、調整済みフラグを解除します。
    /// </summary>
    public void RefitSize() => SizeFitted = false;

    /// <summary>
    /// 元のドライバーを再開して目標設定を試み、失敗時にはプレビュー前のサイズと姿勢へ戻します。
    /// </summary>
    /// <param name="setupTargets">ドライバー再開後に同期的に行うIK目標の設定処理。</param>
    /// <returns>目標設定が成功した場合はtrue。</returns>
    /// <remarks>設定処理がfalseを返した場合や例外で終了した場合は、校正前のルートと骨の姿勢を復元します。例外自体は呼び出し元へ伝わります。</remarks>
    public bool Commit(Func<bool> setupTargets)
    {
        var restore = _restore;
        End(restorePose: false); // Target setup requires the original drivers enabled.
        var success = false;
        try { success = setupTargets(); return success; }
        finally
        {
            if (!success && restore != null && _animator != null)
            {
                var root = _animator.transform;
                root.localScale = _restoreRootScale;
                root.SetPositionAndRotation(_restoreRootPosition, _restoreRootRotation);
                foreach (var pose in restore) pose.Apply();
            }
        }
    }

    /// <summary>
    /// プレビューを上書きするAnimator・VRIK・ルートコントローラーを一時停止します。
    /// </summary>
    private void DisableDrivers()
    {
        _animator.enabled = false;
        if (_ik != null) _ik.enabled = false;
        if (_rootController != null) _rootController.enabled = false;
    }

    /// <summary>
    /// 上腕と前腕を指定したワールド方向へ回し、骨の長さを維持したまま腕を伸ばします。
    /// </summary>
    /// <param name="upperId">上腕のHumanoidボーン識別子。</param>
    /// <param name="lowerId">前腕のHumanoidボーン識別子。</param>
    /// <param name="handId">手のHumanoidボーン識別子。</param>
    /// <param name="outward">腕を伸ばすワールド空間の方向。</param>
    private void AlignArm(HumanBodyBones upperId, HumanBodyBones lowerId, HumanBodyBones handId,
        Vector3 outward)
    {
        var upper = _animator.GetBoneTransform(upperId);
        var lower = _animator.GetBoneTransform(lowerId);
        var hand = _animator.GetBoneTransform(handId);
        if (upper == null || lower == null || hand == null) return;
        if ((lower.position - upper.position).sqrMagnitude > 0.0001f)
            upper.rotation = Quaternion.FromToRotation(lower.position - upper.position, outward) * upper.rotation;
        if ((hand.position - lower.position).sqrMagnitude > 0.0001f)
            lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, outward) * lower.rotation;
    }

    /// <summary>
    /// 校正プレビューを終了し、必要に応じて元の姿勢とサイズを戻してドライバー状態を復元します。
    /// </summary>
    /// <param name="restorePose">プレビュー前の姿勢とサイズへ戻すか。falseは校正済みの固定姿勢を保持します。</param>
    public void End(bool restorePose = true)
    {
        var restore = _restore;
        _restore = null;
        if (restore == null) return;
        try
        {
            if (restorePose && _animator != null)
            {
                _animator.transform.localScale = _restoreRootScale;
                _animator.transform.SetPositionAndRotation(_restoreRootPosition, _restoreRootRotation);
                foreach (var pose in restore) pose.Apply();
            }
        }
        finally
        {
            if (_animator != null) _animator.enabled = _animatorEnabled;
            if (_ik != null) _ik.enabled = _ikEnabled;
            if (_rootController != null) _rootController.enabled = _rootControllerEnabled;
            _ik = null;
            _rootController = null;
        }
    }
}
