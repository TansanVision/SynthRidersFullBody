using Il2CppRootMotion.FinalIK;
using UnityEngine;

namespace SynthRidersFullBody;

// Optional measured head, arm poles, wrists, knee poles and toes. Native
// targets are restored for each lost input and when full-body driving stops.
/// <summary>
/// 任意に割り当てた頭・腕・膝・足先の校正オフセットとVRIK目標を管理します。
/// </summary>
internal sealed class AdditionalTrackerTargets
{
    private readonly Animator _avatar;
    private readonly ModConfig _config;
    private readonly Dictionary<TrackerRole, RigidPoseOffset> _offsets = new();
    private readonly Dictionary<TrackerRole, Pose> _poses = new();
    private readonly Dictionary<TrackerRole, TrackerPoseFilter.AdaptivePoseFilter> _filters = new();
    private readonly List<Slot> _slots = new();
    private bool _active;
    /// <summary>
    /// 左足首と左足先の両方に校正済み入力があるかを示します。
    /// </summary>
    public bool DualLeft { get; private set; }
    /// <summary>
    /// 右足首と右足先の両方に校正済み入力があるかを示します。
    /// </summary>
    public bool DualRight { get; private set; }
    /// <summary>
    /// 指定部位の校正オフセットが登録されているかを調べます。
    /// </summary>
    /// <param name="role">対象の身体部位。</param>
    /// <returns>登録されていればtrue。</returns>
    public bool Has(TrackerRole role) => _offsets.ContainsKey(role);
    /// <summary>
    /// 頭が未割り当て、または割り当てた頭の姿勢を取得できているかを示します。
    /// </summary>
    public bool HeadTrackingAvailable => !Has(TrackerRole.Head) || _poses.ContainsKey(TrackerRole.Head);
    /// <summary>
    /// 指定部位の補正・平滑化済みワールド姿勢を取得します。
    /// </summary>
    /// <param name="role">対象の身体部位。</param>
    /// <param name="pose">成功時に、オフセット適用・平滑化済みのワールド姿勢を受け取ります。</param>
    /// <returns>現在の入力から姿勢を計算できていればtrue。</returns>
    public bool TryGet(TrackerRole role, out Pose pose) => _poses.TryGetValue(role, out pose);

    /// <summary>
    /// 追加部位の対象アバターと補正設定を保持します。
    /// </summary>
    /// <param name="avatar">処理対象のアバター。</param>
    /// <param name="config">使用する共有設定。保存処理がないメソッドではファイルへの書き込みは行いません。</param>
    public AdditionalTrackerTargets(Animator avatar, ModConfig config) { _avatar = avatar; _config = config; }
    /// <summary>
    /// 部位に対応するHumanoidボーンを取得します。
    /// </summary>
    /// <param name="role">対象の身体部位。</param>
    /// <returns>対応するボーン。対象外またはボーンがない場合はnull。</returns>
    private Transform? Bone(TrackerRole role) => role switch
    {
        TrackerRole.Head => _avatar.GetBoneTransform(HumanBodyBones.Head),
        TrackerRole.LeftWrist => _avatar.GetBoneTransform(HumanBodyBones.LeftHand),
        TrackerRole.RightWrist => _avatar.GetBoneTransform(HumanBodyBones.RightHand),
        TrackerRole.LeftElbow => _avatar.GetBoneTransform(HumanBodyBones.LeftLowerArm),
        TrackerRole.RightElbow => _avatar.GetBoneTransform(HumanBodyBones.RightLowerArm),
        TrackerRole.LeftKnee => _avatar.GetBoneTransform(HumanBodyBones.LeftLowerLeg),
        TrackerRole.RightKnee => _avatar.GetBoneTransform(HumanBodyBones.RightLowerLeg),
        TrackerRole.LeftToes => _avatar.GetBoneTransform(HumanBodyBones.LeftToes),
        TrackerRole.RightToes => _avatar.GetBoneTransform(HumanBodyBones.RightToes),
        _ => null
    };

    /// <summary>
    /// 追加部位を駆動するためのVRIKとボーンの条件を確認します。
    /// </summary>
    /// <param name="poses">部位ごとの有効なワールド姿勢。欠測部位は含めません。</param>
    /// <param name="ik">対象アバターのVRIK。見つからない場合はnull。</param>
    /// <returns>不足条件の説明。条件を満たす場合はnull。</returns>
    public string? Validate(IReadOnlyDictionary<TrackerRole, Pose> poses, VRIK? ik)
    {
        foreach (var role in poses.Keys)
        {
            if (role is TrackerRole.Waist or TrackerRole.LeftFoot or TrackerRole.RightFoot) continue;
            if (ik == null || ik.solver == null || !ik.solver.initiated)
                return "Additional body roles need an initialized VRIK avatar";
            // Toe-only tracking can drive the foot bone of a toeless avatar.
            if (role == TrackerRole.LeftToes && !poses.ContainsKey(TrackerRole.LeftFoot) ||
                role == TrackerRole.RightToes && !poses.ContainsKey(TrackerRole.RightFoot)) continue;
            if (Bone(role) == null) return $"VRM has no bone for {TrackerRoles.Label(role)}";
            if (role == TrackerRole.LeftToes && ik.references?.leftToes != Bone(role) ||
                role == TrackerRole.RightToes && ik.references?.rightToes != Bone(role))
                return $"VRIK needs the VRM {TrackerRoles.Label(role)} bone for dual foot tracking";
        }
        return null;
    }

    /// <summary>
    /// 現在の基準姿勢と各入力の間の剛体オフセットを取り直し、履歴を初期化します。
    /// </summary>
    /// <param name="poses">部位ごとの有効なワールド姿勢。欠測部位は含めません。</param>
    public void Calibrate(IReadOnlyDictionary<TrackerRole, Pose> poses)
    {
        Detach();
        _offsets.Clear(); _poses.Clear(); _filters.Clear();
        DualLeft = poses.ContainsKey(TrackerRole.LeftFoot) && poses.ContainsKey(TrackerRole.LeftToes);
        DualRight = poses.ContainsKey(TrackerRole.RightFoot) && poses.ContainsKey(TrackerRole.RightToes);
        foreach (var entry in poses)
        {
            var bone = Bone(entry.Key);
            if (bone == null) continue;
            _offsets[entry.Key] = RigidPoseOffset.Between(N(entry.Value.Position), N(entry.Value.Rotation),
                N(bone.position), N(bone.rotation));
            _filters[entry.Key] = new TrackerPoseFilter.AdaptivePoseFilter();
        }
        Update(poses);
    }

    /// <summary>
    /// 追加部位の目標を作成し、元の目標と重みを保存してVRIKへ接続します。
    /// </summary>
    /// <param name="solver">接続先のVRIKソルバー。</param>
    public void Attach(IKSolverVR solver)
    {
        Detach();
        Add(TrackerRole.Head, () => solver.spine.headTarget, t => solver.spine.headTarget = t,
            () => (solver.spine.positionWeight, solver.spine.rotationWeight),
            (p, r) => { solver.spine.positionWeight = p; solver.spine.rotationWeight = r; });
        Add(TrackerRole.LeftWrist, () => solver.leftArm.target, t => solver.leftArm.target = t,
            () => (solver.leftArm.positionWeight, solver.leftArm.rotationWeight),
            (p, r) => { solver.leftArm.positionWeight = p; solver.leftArm.rotationWeight = r; });
        Add(TrackerRole.RightWrist, () => solver.rightArm.target, t => solver.rightArm.target = t,
            () => (solver.rightArm.positionWeight, solver.rightArm.rotationWeight),
            (p, r) => { solver.rightArm.positionWeight = p; solver.rightArm.rotationWeight = r; });
        Add(TrackerRole.LeftElbow, () => solver.leftArm.bendGoal, t => solver.leftArm.bendGoal = t,
            () => (solver.leftArm.bendGoalWeight, 0f), (p, _) => solver.leftArm.bendGoalWeight = p);
        Add(TrackerRole.RightElbow, () => solver.rightArm.bendGoal, t => solver.rightArm.bendGoal = t,
            () => (solver.rightArm.bendGoalWeight, 0f), (p, _) => solver.rightArm.bendGoalWeight = p);
        _active = true;
        ApplyBindings();
    }

    /// <summary>
    /// 校正済みの1部位について、復元可能なVRIK目標スロットを作成します。
    /// </summary>
    /// <param name="role">対象の身体部位。</param>
    /// <param name="read">現在のVRIK目標を取得するアクセサー。</param>
    /// <param name="write">VRIK目標を差し替えるアクセサー。</param>
    /// <param name="readWeights">現在の位置・回転重みを取得するアクセサー。</param>
    /// <param name="writeWeights">位置・回転の重みを設定するアクセサー。</param>
    private void Add(TrackerRole role, Func<Transform?> read, Action<Transform?> write,
        Func<(float, float)> readWeights, Action<float, float> writeWeights)
    {
        if (!Has(role)) return;
        var go = new GameObject($"SynthRidersFullBody.{role}Target");
        UnityEngine.Object.DontDestroyOnLoad(go);
        var slot = new Slot(role, go.transform, read(), read, write, readWeights(), writeWeights);
        _slots.Add(slot);
        if (_poses.TryGetValue(role, out var pose)) slot.Target.SetPositionAndRotation(pose.Position, pose.Rotation);
    }

    /// <summary>
    /// 取得できた入力へ校正オフセットとフィルターを適用し、追加目標の姿勢を更新します。
    /// </summary>
    /// <param name="poses">部位ごとの有効なワールド姿勢。欠測部位は含めません。</param>
    public void Update(IReadOnlyDictionary<TrackerRole, Pose> poses)
    {
        _poses.Clear();
        foreach (var entry in _offsets)
        {
            if (!poses.TryGetValue(entry.Key, out var raw)) { _filters[entry.Key].Reset(); continue; }
            if (_config.SmoothingEnabled) raw = _filters[entry.Key].Filter(raw, Time.unscaledTime, _config);
            var value = entry.Value.Apply(N(raw.Position), N(raw.Rotation));
            _poses[entry.Key] = new Pose(U(value.Position), U(value.Rotation));
        }
        foreach (var slot in _slots)
        {
            var valid = _poses.TryGetValue(slot.Role, out var pose);
            slot.Blend = valid ? Mathf.MoveTowards(slot.Blend, 1f, Time.unscaledDeltaTime / 0.3f) : 0f;
            if (valid) slot.Target.SetPositionAndRotation(pose.Position, pose.Rotation);
        }
        ApplyBindings();
    }
    /// <summary>
    /// 追加目標の使用状態を切り替え、各スロットの接続へ反映します。
    /// </summary>
    /// <param name="active">MODによる追跡目標の駆動を有効にするか。</param>
    public void SetActive(bool active) { _active = active; ApplyBindings(); }
    /// <summary>
    /// 各VRIK接続がMODの目標または保存した元の目標を参照しているかを示します。
    /// </summary>
    public bool OwnsBindings => _slots.All(s => s.Read() == s.Target || s.Read() == s.Previous);
    /// <summary>
    /// 追跡中の追加目標を接続し、停止・喪失した部位をゲーム側の目標へ戻します。
    /// </summary>
    /// <remarks>入力が復帰した部位だけをMOD目標へ再接続します。ゲーム側の元のTransformを破棄しません。</remarks>
    public void ApplyBindings()
    {
        foreach (var slot in _slots)
        {
            if (slot.Read() != slot.Target && slot.Read() != slot.Previous) continue;
            if (_active && _poses.ContainsKey(slot.Role))
            {
                slot.Write(slot.Target);
                slot.Weights(slot.Blend, slot.Blend);
            }
            else slot.Restore();
        }
    }
    /// <summary>
    /// 対応する足の追跡が有効な場合、IK計算後の足先ボーンへ計測回転を適用します。
    /// </summary>
    /// <param name="left">左足の追跡が有効か。</param>
    /// <param name="right">右足の追跡が有効か。</param>
    public void ApplyToeRotations(bool left, bool right)
    {
        if (!_active) return;
        if (left && DualLeft && TryGet(TrackerRole.LeftToes, out var l) && Bone(TrackerRole.LeftToes) is { } lb)
            lb.rotation = l.Rotation;
        if (right && DualRight && TryGet(TrackerRole.RightToes, out var r) && Bone(TrackerRole.RightToes) is { } rb)
            rb.rotation = r.Rotation;
    }
    /// <summary>
    /// 元のVRIK目標と重みを復元し、MODが作成した追加目標を破棄します。
    /// </summary>
    public void Detach()
    {
        foreach (var slot in _slots)
        {
            try { slot.Restore(); } catch { /* Rig teardown. */ }
            if (slot.Target != null) UnityEngine.Object.Destroy(slot.Target.gameObject);
        }
        _slots.Clear(); _active = false;
    }
    /// <summary>
    /// 追加部位の目標と、ゲーム側の接続を復元するためのアクセサーを保持します。
    /// </summary>
    /// <param name="Role">このスロットが担当する身体部位。</param>
    /// <param name="Target">MODが作成した追加部位のIK目標。</param>
    /// <param name="Previous">接続前のゲーム側目標。</param>
    /// <param name="Read">現在のVRIK目標を取得するアクセサー。</param>
    /// <param name="Write">VRIK目標を差し替えるアクセサー。</param>
    /// <param name="PreviousWeights">接続前の位置・回転の重み。</param>
    /// <param name="Weights">位置・回転の重みを設定するアクセサー。</param>
    private sealed record Slot(TrackerRole Role, Transform Target, Transform? Previous,
        Func<Transform?> Read, Action<Transform?> Write, (float Position, float Rotation) PreviousWeights,
        Action<float, float> Weights)
    {
        /// <summary>
        /// 追加トラッカーの目標を反映する重み。追跡状態の変化を滑らかにつなぎます。
        /// </summary>
        public float Blend = 1f;
        /// <summary>
        /// このスロットの目標がまだ接続されている場合に、元の目標と保存した位置・回転の重みを復元します。
        /// </summary>
        public void Restore()
        {
            if (Read() != Target) return;
            Write(Previous);
            Weights(PreviousWeights.Position, PreviousWeights.Rotation);
        }
    }
    /// <summary>
    /// Unityの数学型を、成分の値を保ってSystem.Numericsの型へ変換します。
    /// </summary>
    /// <param name="v">変換または検証するベクトル。</param>
    /// <returns>同じ成分を持つSystem.Numericsの値。</returns>
    private static System.Numerics.Vector3 N(Vector3 v) => new(v.x, v.y, v.z);
    /// <summary>
    /// Unityの数学型を、成分の値を保ってSystem.Numericsの型へ変換します。
    /// </summary>
    /// <param name="q">変換または検証するQuaternion。</param>
    /// <returns>同じ成分を持つSystem.Numericsの値。</returns>
    private static System.Numerics.Quaternion N(Quaternion q) => new(q.x, q.y, q.z, q.w);
    /// <summary>
    /// System.Numericsの数学型を、成分の値を保ってUnityの型へ変換します。
    /// </summary>
    /// <param name="v">変換または検証するベクトル。</param>
    /// <returns>同じ成分を持つUnityの値。</returns>
    private static Vector3 U(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
    /// <summary>
    /// System.Numericsの数学型を、成分の値を保ってUnityの型へ変換します。
    /// </summary>
    /// <param name="q">変換または検証するQuaternion。</param>
    /// <returns>同じ成分を持つUnityの値。</returns>
    private static Quaternion U(System.Numerics.Quaternion q) => new(q.X, q.Y, q.Z, q.W);
}
