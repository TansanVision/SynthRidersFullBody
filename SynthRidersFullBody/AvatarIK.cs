using UnityEngine;
using Il2CppSynth.Avatars;
using Il2CppRootMotion.FinalIK;

namespace SynthRidersFullBody;

/// <summary>
/// アバターの校正と全身IKを管理し、腰・足および追加部位の追跡を骨格へ反映します。
/// </summary>
internal sealed class AvatarIK
{
    /// <summary>
    /// このインスタンスが駆動するHumanoidアバターのAnimatorです。
    /// </summary>
    public Animator Animator { get; }
    /// <summary>
    /// 対象アバターに対応するVRIK。見つからない場合はnullです。
    /// </summary>
    public VRIK? IkComponent => _ik;
    /// <summary>
    /// ゲーム標準IK・VRIK目標・ボーン直接駆動など、現在の駆動状態を示す診断文です。
    /// </summary>
    public string DriveMode => _usingGameTargets ? "game IK (waist unavailable / paused)" : _ik != null && _ik.enabled
        ? _pelvisTarget == null ? "VRIK waiting" :
            _chestGoal != null ? "VRIK targets + virtual chest" : "VRIK targets"
        : "bone fallback";
    /// <summary>
    /// 骨格寸法、目標接続、重み、ルート制御をまとめたログ用の状態です。
    /// </summary>
    public string RigSummary
    {
        get
        {
            var root = _ik?.references?.root;
            var spine = _ik?.solver?.spine;
            var head = Animator.GetBoneTransform(HumanBodyBones.Head);
            var rootController = root != null ? root.GetComponent<VRIKRootController>() : null;
            return $"name={Animator.gameObject.name}, root={root?.name ?? "missing"}, " +
                $"scale={Animator.transform.lossyScale.y:0.###}, " +
                $"head-to-hips={(head == null ? 0f : Vector3.Distance(head.position, _hips.position)):0.###}m, " +
                $"legs=({_leftUpperLength + _leftLowerLength:0.###}," +
                $"{_rightUpperLength + _rightLowerLength:0.###})m, " +
                $"VRIK={(_ik != null && _ik.enabled ? "on" : "off")}, " +
                $"initiated={_ik?.solver?.initiated}, " +
                $"IKWeight={_ik?.solver?.IKPositionWeight:0.###}, LOD={_ik?.solver?.LOD}, " +
                $"headTarget={(spine?.headTarget != null ? "yes" : "no")}, " +
                $"neckStiffness={_previousNeckStiffness:0.###}->{spine?.neckStiffness:0.###}, " +
                $"headClamp={_previousHeadClampWeight:0.###}->{spine?.headClampWeight:0.###}, " +
                $"chestGoalWeight={spine?.chestGoalWeight:0.###}, " +
                $"rootAngle={spine?.maxRootAngle:0.#}, " +
                $"rootController={(rootController != null ? "yes" : "no")}, " +
                $"rootControlActive={rootController?.enabled}, " +
                $"kneeGuides=({(_leftBendGoal != null ? "on" : "game/off")}," +
                $"{(_rightBendGoal != null ? "on" : "game/off")}), " +
                $"legEndpoints=({LeftLegEnd.name},{RightLegEnd.name}), " +
                $"rootPosition={root?.position}, pelvisPosition={_hips.position}";
        }
    }
    /// <summary>
    /// 直近のIK初期化・目標設定エラー。エラーがなければnullです。
    /// </summary>
    public string? IkError { get; private set; }
    /// <summary>
    /// 同じアバターへの校正復帰を判定する識別キーです。
    /// </summary>
    public string AvatarKey { get; private set; } = "";
    /// <summary>
    /// ロードが完了し、VRIKと頭の入力が校正復帰を受け付けられる状態かを示します。
    /// </summary>
    public bool ReadyForRecovery
    {
        get
        {
            var loader = AvatarLoader.s_instance;
            if (loader != null && (loader.avatarIsLoading || !loader.hasAvatarLoaded)) return false;
            return Animator != null && Animator.gameObject.activeInHierarchy && _ik != null && _ik.enabled &&
                _ik.gameObject.activeInHierarchy && _ik.solver != null && _ik.solver.initiated &&
                (_ik.solver.spine?.headTarget != null || _config.RoleBindings.ContainsKey(nameof(TrackerRole.Head)));
        }
    }

    /// <summary>
    /// 現在の校正姿勢と入力を、シーン破棄後も保持できる値の集合として保存します。
    /// </summary>
    /// <param name="waist">腰のワールド姿勢。</param>
    /// <param name="left">左足のワールド姿勢。</param>
    /// <param name="right">右足のワールド姿勢。</param>
    /// <param name="additional">部位ごとの追跡姿勢。校正・駆動処理ではゲームのワールド空間を使用します。</param>
    /// <param name="source">取得元APIの識別名。</param>
    /// <param name="space">追跡空間からゲームのワールド空間への固定変換。</param>
    /// <returns>同じゲーム起動中の校正復帰に使用するスナップショット。</returns>
    public CalibrationSession CaptureSession(Pose waist, Pose left, Pose right,
        IReadOnlyDictionary<TrackerRole, Pose> additional, string source, TrackingSpaceAnchor space) =>
        new(AvatarKey, _calibrationPose.RigSignature, _calibrationPose.CaptureBones(),
            new Pose(Animator.transform.position, Animator.transform.rotation), Animator.transform.lossyScale,
            waist, left, right, additional.ToDictionary(x => x.Key, x => x.Value), source, space);

    /// <summary>
    /// アバター識別子・骨格・拡縮比が保存済み校正と互換かを確認します。
    /// </summary>
    /// <param name="saved">最初に成功した校正から保持している復帰用データ。</param>
    /// <param name="reason">復帰できない理由。復帰できる場合は空文字列です。</param>
    /// <returns>保存した校正を復帰できる組み合わせならtrue。</returns>
    public bool CanRestore(CalibrationSession saved, out string reason)
    {
        reason = saved.AvatarKey != AvatarKey ? "different VRM" :
            saved.RigSignature != _calibrationPose.RigSignature ? "avatar skeleton changed" :
            !saved.MatchesScale(Animator.transform.lossyScale) ? "non-uniform or invalid avatar scale" : "";
        return reason.Length == 0;
    }

    /// <summary>
    /// 追跡空間の変更を反映し、保存した基準姿勢から校正目標を再構築します。
    /// </summary>
    /// <param name="saved">最初に成功した校正から保持している復帰用データ。</param>
    /// <param name="space">追跡空間からゲームのワールド空間への固定変換。</param>
    /// <returns>基準姿勢を再適用して校正に成功した場合はtrue。</returns>
    public bool RestoreSession(CalibrationSession saved, TrackingSpaceAnchor space)
    {
        if (!ReadyForRecovery || !CanRestore(saved, out _)) return false;
        var additional = saved.Additional.ToDictionary(x => x.Key, x => saved.Remap(x.Value, space));
        if (ValidateAdditionalRoles(additional) is { } error) { IkError = error; return false; }
        ResetCalibration(true);
        var success = _calibrationPose.WithReference(saved, saved.Remap(saved.Root, space), () =>
            Calibrate(saved.Remap(saved.Waist, space), saved.Remap(saved.Left, space),
                saved.Remap(saved.Right, space), null, additional));
        // The next current tracker frame fills the targets before a solver update.
        if (success)
        {
            _trackingBlend = 0f;
            if (_rootController != null) _rootController.enabled = false;
        }
        return success;
    }
    private readonly Transform _hips;
    private readonly Transform _leftUpper;
    private readonly Transform _leftLower;
    private readonly Transform _leftFoot;
    private readonly Transform _rightUpper;
    private readonly Transform _rightLower;
    private readonly Transform _rightFoot;
    private readonly CalibratedToeRotation _leftToeRotation;
    private readonly CalibratedToeRotation _rightToeRotation;
    /// <summary>
    /// VRIKに左足先が登録されていれば足先、なければ左足首のボーンを取得します。
    /// </summary>
    private Transform LeftLegEnd => _ik?.references?.leftToes ?? _leftFoot;
    /// <summary>
    /// VRIKに右足先が登録されていれば足先、なければ右足首のボーンを取得します。
    /// </summary>
    private Transform RightLegEnd => _ik?.references?.rightToes ?? _rightFoot;
    private RigidPoseOffset _leftEndOffset;
    private RigidPoseOffset _rightEndOffset;
    private readonly Transform? _chest;
    private readonly VRIK? _ik;
    private IKSolverVR? _boundSolver;
    private bool _hasIkSnapshot;
    private bool _usingGameTargets;
    private bool _hasLeftTracking = true;
    private bool _hasRightTracking = true;
    private IKSolver.UpdateDelegate? _beforeSolve;
    private IKSolver.UpdateDelegate? _afterSolve;
    private string? _solveError;
    private int _solveCount;
    private float _lastSolveTime = -1f;
    private float _pelvisErrorAfterSolve;
    private float _pelvisAngleAfterSolve;
    private float _leftFootErrorAfterSolve;
    private float _rightFootErrorAfterSolve;
    private Pose _rawWaist;
    private Vector3 _waistCalibrationPosition;
    private Vector3 _pelvisCalibrationPosition;
    private readonly ModConfig _config;
    private readonly AvatarCalibrationPose _calibrationPose;
    private readonly AdditionalTrackerTargets _additional;
    private bool _leftKneeTracked, _rightKneeTracked;
    /// <summary>
    /// 校正用の基準姿勢をどのように取得したかを示します。
    /// </summary>
    public string CalibrationPoseSource => _calibrationPose.Source;
    /// <summary>
    /// 校正プレビューのサイズ調整状態を示します。
    /// </summary>
    public string CalibrationSizeStatus => _calibrationPose.SizeStatus;
    /// <summary>
    /// 校正プレビューが有効で、サイズ調整が完了しているかを示します。
    /// </summary>
    public bool CalibrationSizeReady => _calibrationPose.IsActive && _calibrationPose.SizeFitted;
    private float _leftUpperLength;
    private float _leftLowerLength;
    private float _rightUpperLength;
    private float _rightLowerLength;
    private Transform? _pelvisTarget;
    private Transform? _leftTarget;
    private Transform? _rightTarget;
    private Transform? _chestGoal;
    private Transform? _leftBendGoal;
    private Transform? _rightBendGoal;
    private Transform? _previousPelvisTarget;
    private Transform? _previousLeftTarget;
    private Transform? _previousRightTarget;
    private Transform? _previousChestGoal;
    private Transform? _previousLeftBendGoal;
    private Transform? _previousRightBendGoal;
    private VRIKRootController? _rootController;
    private bool _createdRootController;
    private bool _previousRootControllerEnabled;
    private float _previousPelvisPositionWeight;
    private float _previousPelvisRotationWeight;
    private float _previousLeftPositionWeight;
    private float _previousLeftRotationWeight;
    private float _previousRightPositionWeight;
    private float _previousRightRotationWeight;
    private float _previousLocomotionWeight;
    private float _previousChestGoalWeight;
    private float _previousLeftBendGoalWeight;
    private float _previousRightBendGoalWeight;
    private bool _previousPlantFeet;
    private float _previousNeckStiffness;
    private float _previousHeadClampWeight;
    private float _previousMaintainPelvisPosition;
    private float _previousMinHeadHeight;
    private float _previousMaxRootAngle;
    private float _previousBodyPosStiffness;
    private float _previousBodyRotStiffness;
    private float _previousRotateChestByHands;
    private float _previousMoveBodyBack;
    private Vector3 _waistWorldOffset;
    private Vector3 _leftOffset;
    private Vector3 _rightOffset;
    private Quaternion _waistReferenceRotation;
    private Quaternion _hipsReferenceRotation;
    private Vector3 _leftHipLocalOffset;
    private Vector3 _rightHipLocalOffset;
    private Vector3 _leftTrackerReferencePosition;
    private Vector3 _rightTrackerReferencePosition;
    private Vector3 _leftTargetReferencePosition;
    private Vector3 _rightTargetReferencePosition;
    private float _leftUserLegReferenceDistance;
    private float _rightUserLegReferenceDistance;
    private float _leftAvatarLegReferenceDistance;
    private float _rightAvatarLegReferenceDistance;
    private Vector3 _chestLocalOffset;
    private Vector3 _bodyForwardReference;
    private Vector3 _leftKneeDirectionReference;
    private Vector3 _rightKneeDirectionReference;
    private Vector3 _leftLegAxisReference;
    private Vector3 _rightLegAxisReference;
    private Quaternion _lastPelvisYaw = Quaternion.identity;
    private Vector3 _smoothedChestForward;
    private float _smoothedChestWeight = -1f;
    private Pose _lastLeftPose;
    private Pose _lastRightPose;
    private Quaternion _leftRotationOffset;
    private Quaternion _rightRotationOffset;
    private Quaternion _headReferenceRotation;
    private Quaternion _headTargetReferenceRotation;
    private Quaternion _hmdReferenceRotation;
    private bool _hasHeadReference;
    private bool _hasAppliedTargets;
    private bool _calibrated;
    private bool _requireExplicitCalibration = true;
    private bool _ikUnavailable;
    private float _trackingBlend = 1f;
    private Vector3 _scaleAtCalibration;
    /// <summary>
    /// 対象ボーンとIKを保持し、基準姿勢と脚の寸法を初期化します。
    /// </summary>
    /// <param name="animator">対象HumanoidアバターのAnimator。</param>
    /// <param name="ik">対象アバターのVRIK。見つからない場合はnull。</param>
    /// <param name="config">使用する共有設定。保存処理がないメソッドではファイルへの書き込みは行いません。</param>
    /// <param name="calibrationPose">再利用する基準姿勢。nullの場合は読み込み時の姿勢を新たに取得します。</param>
    private AvatarIK(Animator animator, VRIK? ik, ModConfig config,
        AvatarCalibrationPose? calibrationPose = null)
    {
        Animator = animator;
        _ik = ik;
        _config = config;
        _calibrationPose = calibrationPose ?? new AvatarCalibrationPose(animator);
        _additional = new AdditionalTrackerTargets(animator, config);
        _hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        _leftUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        _leftLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        _leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        _rightUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        _rightLower = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        _rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        _leftToeRotation = new CalibratedToeRotation(_leftFoot, animator.GetBoneTransform(HumanBodyBones.LeftToes));
        _rightToeRotation = new CalibratedToeRotation(_rightFoot, animator.GetBoneTransform(HumanBodyBones.RightToes));
        _chest = animator.GetBoneTransform(HumanBodyBones.Chest) ??
            animator.GetBoneTransform(HumanBodyBones.UpperChest);
        MeasureLegLengths();
    }

    /// <summary>
    /// 指定方向とモードでアバターを固定し、条件を満たす場合にサイズを合わせます。
    /// </summary>
    /// <param name="camera">ゲーム内のHMD視点として使用するカメラ。</param>
    /// <param name="forward">入力位置と同じ座標系におけるユーザーまたはアバターの正面方向。</param>
    /// <param name="tPose">腕を水平に広げるTポーズを使用するか。falseはStandingです。</param>
    /// <param name="canFit">直立・追跡条件を満たし、現在の入力でサイズを合わせられるか。</param>
    /// <param name="left">左足のワールド姿勢。</param>
    /// <param name="right">右足のワールド姿勢。</param>
    public void ShowCalibrationPose(Camera camera, Vector3 forward, bool tPose,
        bool canFit, Pose left, Pose right)
    {
        _calibrationPose.Show(camera, forward, _ik, tPose, canFit, left, right);
    }

    /// <summary>
    /// 校正プレビューを終了し、未確定の姿勢・サイズとドライバー状態を戻します。
    /// </summary>
    public void HideTPosePreview() => _calibrationPose.End();
    /// <summary>
    /// 次の有効な校正プレビューでサイズを取り直すよう要求します。
    /// </summary>
    public void RefitCalibrationSize() => _calibrationPose.RefitSize();

    /// <summary>
    /// 左右の大腿・下腿のワールド長を測り、IK用の寸法を更新します。
    /// </summary>
    private void MeasureLegLengths()
    {
        _leftUpperLength = Vector3.Distance(_leftUpper.position, _leftLower.position);
        _leftLowerLength = Vector3.Distance(_leftLower.position, _leftFoot.position);
        _rightUpperLength = Vector3.Distance(_rightUpper.position, _rightLower.position);
        _rightLowerLength = Vector3.Distance(_rightLower.position, _rightFoot.position);
    }

    /// <summary>
    /// 接続したIK目標を解除し、校正・補正履歴とエラー状態を初期化します。
    /// </summary>
    /// <param name="requireExplicit">自動的な校正を禁止し、ユーザーの明示的な校正を必須にするか。</param>
    public void ResetCalibration(bool requireExplicit = false)
    {
        ReleaseIkTargets();
        _calibrated = false;
        _requireExplicitCalibration = requireExplicit;
        _ikUnavailable = false;
        IkError = null;
        _smoothedChestForward = Vector3.zero;
        _smoothedChestWeight = -1f;
        _hasHeadReference = false;
        _hasAppliedTargets = false;
        _lastPelvisYaw = Quaternion.identity;
    }

    /// <summary>
    /// プレビューを終了し、MOD所有の目標とIK接続を解放します。
    /// </summary>
    public void Dispose()
    {
        try { HideTPosePreview(); }
        finally { ReleaseIkTargets(); }
    }

    /// <summary>
    /// 追跡可否に応じてゲーム目標とMOD目標を切り替え、復帰時の重みを滑らかに戻します。
    /// </summary>
    /// <param name="active">MODによる追跡目標の駆動を有効にするか。</param>
    /// <param name="hasLeft">左足の追跡姿勢が有効か。</param>
    /// <param name="hasRight">右足の追跡姿勢が有効か。</param>
    public void UpdateTrackingState(bool active, bool hasLeft = false, bool hasRight = false)
    {
        if (!_hasIkSnapshot || _boundSolver == null || _ik == null) { _additional.SetActive(false); return; }
        active &= _ik.enabled && _ik.gameObject.activeInHierarchy;
        _additional.SetActive(active);
        if ((!active || !_additional.HeadTrackingAvailable) && _rootController != null) _rootController.enabled = false;
        if (!OwnsIkTargets()) return;
        _hasLeftTracking = hasLeft;
        _hasRightTracking = hasRight;
        if (!active)
        {
            // Do not solve from a stale pelvis pose. Return the game its own
            // targets and constraints until a valid waist sample comes back.
            if (!_usingGameTargets) RestoreIkState();
            _usingGameTargets = true;
            _trackingBlend = 0f;
            return;
        }
        if (_usingGameTargets)
        {
            _usingGameTargets = false;
            BindModTargets();
        }
        var dt = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.1f);
        _trackingBlend = Mathf.MoveTowards(_trackingBlend, 1f, dt / 0.3f);
        ApplyTrackedConstraints();
    }

    /// <summary>
    /// 腰を主入力とする重みと制約を、所有中のVRIKへ設定します。
    /// </summary>
    private void ApplyTrackedConstraints()
    {
        if (_usingGameTargets || _boundSolver == null) return;
        var solver = _boundSolver;
        solver.spine.pelvisPositionWeight =
            Mathf.Clamp01(_config.PelvisPositionWeight) * _trackingBlend;
        solver.spine.pelvisRotationWeight =
            Mathf.Clamp01(_config.PelvisRotationWeight) * _trackingBlend;
        solver.leftLeg.positionWeight = solver.leftLeg.rotationWeight = _hasLeftTracking ? _trackingBlend : 0f;
        solver.rightLeg.positionWeight = solver.rightLeg.rotationWeight = _hasRightTracking ? _trackingBlend : 0f;
        if (_chestGoal != null)
            solver.spine.chestGoalWeight = (_smoothedChestWeight < 0f
                ? Mathf.Clamp01(_config.ChestGoalWeight) : _smoothedChestWeight) * _trackingBlend;
        if (_leftBendGoal != null)
            solver.leftLeg.bendGoalWeight = _hasLeftTracking ? (_leftKneeTracked ? 1f : Mathf.Clamp01(_config.KneeBendGoalWeight)) * _trackingBlend : 0f;
        if (_rightBendGoal != null)
            solver.rightLeg.bendGoalWeight = _hasRightTracking ? (_rightKneeTracked ? 1f : Mathf.Clamp01(_config.KneeBendGoalWeight)) * _trackingBlend : 0f;
        solver.plantFeet = false;
        solver.spine.bodyPosStiffness = 0f;
        solver.spine.bodyRotStiffness = 0f;
        solver.spine.neckStiffness = 0f;
        solver.spine.rotateChestByHands = 0f;
        solver.spine.moveBodyBackWhenCrouching = 0f;
        solver.spine.headClampWeight = Mathf.Min(_previousHeadClampWeight, 0.4f);
        solver.spine.maintainPelvisPosition = 0f;
        solver.spine.minHeadHeight = 0f;
        solver.spine.maxRootAngle = 180f;
        if (solver.locomotion != null) solver.locomotion.weight = 0f;
    }

    /// <summary>
    /// MOD所有の骨盤・左右足・胸・膝目標をソルバーへ接続し直します。
    /// </summary>
    private void BindModTargets()
    {
        var solver = _boundSolver!;
        solver.spine.pelvisTarget = _pelvisTarget;
        solver.leftLeg.target = _leftTarget;
        solver.rightLeg.target = _rightTarget;
        if (_chestGoal != null) solver.spine.chestGoal = _chestGoal;
        if (_leftBendGoal != null) solver.leftLeg.bendGoal = _leftBendGoal;
        if (_rightBendGoal != null) solver.rightLeg.bendGoal = _rightBendGoal;
    }

    /// <summary>
    /// VRIK計算直前に制約と追加目標を再適用し、ゲーム側の更新による上書きを補います。
    /// </summary>
    private void BeforeSolve()
    {
        try
        {
            // The game may update VRIK settings after MelonLoader.LateUpdate.
            if (_calibrated && !_usingGameTargets && OwnsIkTargets())
            {
                ApplyTrackedConstraints();
                _additional.ApplyBindings();
                // VRIK reads the animated pose. Do not feed an unmeasured toe
                // bend (or the previous solve's bend) back into the next solve.
                if (_hasAppliedTargets && _boundSolver!.LOD < 2)
                    ApplyUnmeasuredToeRotations(_hasLeftTracking, _hasRightTracking,
                        _trackingBlend * Mathf.Clamp01(_boundSolver.IKPositionWeight));
            }
        }
        catch (Exception exception)
        {
            _solveError = $"VRIK pre-solve failed: {exception.GetType().Name}: {exception.Message}";
        }
    }

    /// <summary>
    /// VRIK計算後に足先回転を反映し、骨盤誤差と計算時刻を記録します。
    /// </summary>
    private void AfterSolve()
    {
        try
        {
            if (!_calibrated || _usingGameTargets || !_hasAppliedTargets || !OwnsIkTargets()) return;
            if (_boundSolver!.LOD < 2)
                ApplyUnmeasuredToeRotations(_hasLeftTracking, _hasRightTracking,
                    _trackingBlend * Mathf.Clamp01(_boundSolver.IKPositionWeight));
            _additional.ApplyToeRotations(_hasLeftTracking, _hasRightTracking);
            _solveCount++;
            _lastSolveTime = Time.unscaledTime;
            _pelvisErrorAfterSolve = Vector3.Distance(_hips.position, _pelvisTarget!.position);
            _pelvisAngleAfterSolve = Quaternion.Angle(_hips.rotation, _pelvisTarget.rotation);
            _leftFootErrorAfterSolve = Vector3.Distance(LeftLegEnd.position, _leftTarget!.position);
            _rightFootErrorAfterSolve = Vector3.Distance(RightLegEnd.position, _rightTarget!.position);
        }
        catch (Exception exception)
        {
            _solveError = $"VRIK post-solve failed: {exception.GetType().Name}: {exception.Message}";
        }
    }

    /// <summary>
    /// 足先を独立計測していない足だけ、校正時の足首に対する足先角度へ戻します。
    /// </summary>
    /// <param name="left">左足の主入力が有効か。</param>
    /// <param name="right">右足の主入力が有効か。</param>
    /// <param name="weight">追跡とソルバーを合わせた適用重み。</param>
    private void ApplyUnmeasuredToeRotations(bool left, bool right, float weight)
    {
        _leftToeRotation.Apply(left, _additional.DualLeft, weight);
        _rightToeRotation.Apply(right, _additional.DualRight, weight);
    }

    /// <summary>
    /// ソルバーのエラー、または校正時に接続したVRIKソルバー・目標の変更を調べます。
    /// </summary>
    /// <param name="reason">検出したエラーまたは変更の理由。検出しなかった場合は空文字列です。</param>
    /// <returns>ソルバーのエラー、または校正済みの接続の変更を検出した場合はtrue。</returns>
    public bool HasCalibrationBindingChanged(out string reason)
    {
        reason = "";
        if (_solveError != null) { reason = _solveError; return true; }
        if (!_calibrated || !_hasIkSnapshot || OwnsIkTargets()) return false;
        reason = "VRIK solver or tracking targets changed";
        return true;
    }

    /// <summary>
    /// 接続中のソルバーと目標が、MODの管理対象と一致するかを確認します。
    /// </summary>
    /// <returns>管理対象のソルバーと目標接続が保たれていればtrue。</returns>
    private bool OwnsIkTargets()
    {
        if (_ik == null || _boundSolver == null || _ik.solver == null ||
            _ik.solver.Pointer != _boundSolver.Pointer || _ik.references == null ||
            _ik.references.pelvis != _hips ||
            _pelvisTarget == null || _leftTarget == null || _rightTarget == null)
            return false;
        var solver = _boundSolver;
        return _additional.OwnsBindings && solver.spine != null && solver.leftLeg != null && solver.rightLeg != null &&
            solver.spine.pelvisTarget == (_usingGameTargets ? _previousPelvisTarget : _pelvisTarget) &&
            solver.leftLeg.target == (_usingGameTargets ? _previousLeftTarget : _leftTarget) &&
            solver.rightLeg.target == (_usingGameTargets ? _previousRightTarget : _rightTarget) &&
            (_chestGoal is null || solver.spine.chestGoal == (_usingGameTargets ? _previousChestGoal : _chestGoal)) &&
            (_leftBendGoal is null || solver.leftLeg.bendGoal == (_usingGameTargets ? _previousLeftBendGoal : _leftBendGoal)) &&
            (_rightBendGoal is null || solver.rightLeg.bendGoal == (_usingGameTargets ? _previousRightBendGoal : _rightBendGoal));
    }

    /// <summary>
    /// 追加部位の校正に必要なVRIKとボーンを検証します。
    /// </summary>
    /// <param name="poses">部位ごとの有効なワールド姿勢。欠測部位は含めません。</param>
    /// <returns>使用できない理由。検証に成功した場合はnull。</returns>
    public string? ValidateAdditionalRoles(IReadOnlyDictionary<TrackerRole, Pose> poses) =>
        _additional.Validate(poses, _ik);

    /// <summary>
    /// 固定した基準姿勢と安定した追跡入力から装着オフセット・脚寸法・IK目標を設定します。
    /// </summary>
    /// <param name="waist">腰のワールド姿勢。</param>
    /// <param name="left">左足のワールド姿勢。</param>
    /// <param name="right">右足のワールド姿勢。</param>
    /// <param name="camera">手動校正時のHMDカメラ。保存校正からの復帰ではnull。</param>
    /// <param name="additional">部位ごとの追跡姿勢。校正・駆動処理ではゲームのワールド空間を使用します。</param>
    /// <returns>必要な目標を接続し、追跡を開始できる場合はtrue。</returns>
    /// <remarks>手動校正では固定プレビューと静止区間の平均入力を使用します。シーン復帰では保存した基準姿勢を一時適用してから呼び出します。</remarks>
    public bool Calibrate(Pose waist, Pose left, Pose right, Camera? camera = null,
        IReadOnlyDictionary<TrackerRole, Pose>? additional = null)
    {
        _additional.Calibrate(additional ?? new Dictionary<TrackerRole, Pose>());
        _waistCalibrationPosition = waist.Position;
        _pelvisCalibrationPosition = _hips.position;
        _rawWaist = waist;
        _solveCount = 0;
        _lastSolveTime = -1f;
        _scaleAtCalibration = Animator.transform.lossyScale;
        MeasureLegLengths();
        _waistWorldOffset = _hips.position - waist.Position;
        _waistReferenceRotation = waist.Rotation;
        _lastPelvisYaw = Quaternion.identity;
        _hipsReferenceRotation = _hips.rotation;
        var inverseHipsRotation = Quaternion.Inverse(_hipsReferenceRotation);
        _leftHipLocalOffset = inverseHipsRotation * (_leftUpper.position - _hips.position);
        _rightHipLocalOffset = inverseHipsRotation * (_rightUpper.position - _hips.position);
        _leftTrackerReferencePosition = left.Position;
        _rightTrackerReferencePosition = right.Position;
        _leftTargetReferencePosition = LeftLegEnd.position;
        _rightTargetReferencePosition = RightLegEnd.position;
        _leftUserLegReferenceDistance = Vector3.Distance(waist.Position, left.Position);
        _rightUserLegReferenceDistance = Vector3.Distance(waist.Position, right.Position);
        _leftAvatarLegReferenceDistance = Vector3.Distance(_leftUpper.position, _leftFoot.position);
        _rightAvatarLegReferenceDistance = Vector3.Distance(_rightUpper.position, _rightFoot.position);
        if (_chest != null)
            _chestLocalOffset = inverseHipsRotation * (_chest.position - _hips.position);
        _bodyForwardReference = Vector3.ProjectOnPlane(Animator.transform.forward, Vector3.up);
        if (_bodyForwardReference.sqrMagnitude < 0.001f)
            _bodyForwardReference = Vector3.forward;
        _bodyForwardReference.Normalize();
        _leftKneeDirectionReference = KneeDirection(
            _leftUpper.position, _leftLower.position, _leftFoot.position,
            _bodyForwardReference);
        _rightKneeDirectionReference = KneeDirection(
            _rightUpper.position, _rightLower.position, _rightFoot.position,
            _bodyForwardReference);
        _leftLegAxisReference = _leftFoot.position - _leftUpper.position;
        _rightLegAxisReference = _rightFoot.position - _rightUpper.position;
        _smoothedChestForward = Vector3.zero;
        _smoothedChestWeight = -1f;
        _lastLeftPose = left;
        _lastRightPose = right;
        _leftOffset = Quaternion.Inverse(left.Rotation) * (_leftFoot.position - left.Position);
        _rightOffset = Quaternion.Inverse(right.Rotation) * (_rightFoot.position - right.Position);
        _leftRotationOffset = Quaternion.Inverse(left.Rotation) * _leftFoot.rotation;
        _rightRotationOffset = Quaternion.Inverse(right.Rotation) * _rightFoot.rotation;
        _leftEndOffset = RigidPoseOffset.Between(ToNumerics(_leftFoot.position),
            ToNumerics(_leftFoot.rotation), ToNumerics(LeftLegEnd.position), ToNumerics(LeftLegEnd.rotation));
        _rightEndOffset = RigidPoseOffset.Between(ToNumerics(_rightFoot.position),
            ToNumerics(_rightFoot.rotation), ToNumerics(RightLegEnd.position), ToNumerics(RightLegEnd.rotation));
        _leftToeRotation.Capture();
        _rightToeRotation.Capture();
        var head = Animator.GetBoneTransform(HumanBodyBones.Head);
        var headTarget = _ik?.solver?.spine?.headTarget;
        _hasHeadReference = head != null && headTarget != null && camera != null;
        if (_hasHeadReference)
        {
            _headReferenceRotation = head!.rotation;
            _headTargetReferenceRotation = headTarget!.rotation;
            _hmdReferenceRotation = camera!.transform.rotation;
        }
        _hasAppliedTargets = false;
        _calibrated = true;
        _requireExplicitCalibration = false;
        // Keep the fixed reference pose for target/root calibration. Restoring
        // the live pose here would reintroduce the previous solve's offsets.
        return _calibrationPose.Commit(() =>
        {
            AttachIkTargets();
            var needsVrik = additional?.Keys.Any(r => r is not (TrackerRole.Waist or TrackerRole.LeftFoot or TrackerRole.RightFoot)) == true;
            if (_ikUnavailable || ((_ik != null && _ik.enabled || needsVrik) && _pelvisTarget == null))
            {
                IkError ??= "VRIK targets are not ready; retry calibration after loading the avatar";
                _calibrated = false;
                _requireExplicitCalibration = true;
                ReleaseIkTargets();
                return false;
            }
            return true;
        });
    }

    /// <summary>
    /// 校正時と現在のワールドスケールの差が復帰処理を必要とする大きさか調べます。
    /// </summary>
    /// <param name="detail">変更の診断説明を受け取ります。</param>
    /// <returns>校正済みで、いずれかの軸が5%以上変わった場合はtrue。</returns>
    public bool HasCalibrationScaleChanged(out string detail)
    {
        detail = "";
        if (!_calibrated) return false;
        var current = Animator.transform.lossyScale;
        var change = Mathf.Max(
            ScaleDifference(_scaleAtCalibration.x, current.x),
            ScaleDifference(_scaleAtCalibration.y, current.y),
            ScaleDifference(_scaleAtCalibration.z, current.z));
        if (change < 0.05f) return false;
        detail = $"avatar scale changed by {change * 100f:0.#}% " +
            $"(calibrated={_scaleAtCalibration}, current={current})";
        return true;
    }

    /// <summary>
    /// 頭と足の入力に対するアバターの位置関係を診断用文字列にまとめます。
    /// </summary>
    /// <param name="camera">ゲーム内のHMD視点として使用するカメラ。</param>
    /// <param name="left">左足のワールド姿勢。</param>
    /// <param name="right">右足のワールド姿勢。</param>
    /// <returns>校正位置の整合性を確認するための説明。</returns>
    public string CalibrationFitSummary(Camera camera, Pose left, Pose right)
    {
        var head = Animator.GetBoneTransform(HumanBodyBones.Head);
        var avatarHeight = head == null ? 0f :
            head.position.y - (_leftFoot.position.y + _rightFoot.position.y) * 0.5f;
        var trackerHeight = camera.transform.position.y -
            (left.Position.y + right.Position.y) * 0.5f;
        var ratio = trackerHeight > 0.1f ? avatarHeight / trackerHeight : 0f;
        return $"avatar head-to-feet={avatarHeight:0.###}m, " +
            $"HMD-to-foot trackers={trackerHeight:0.###}m, approximate ratio={ratio:0.###}, " +
            $"rotating offsets waist/left/right=({_waistWorldOffset.magnitude:0.000}/" +
            $"{_leftOffset.magnitude:0.000}/{_rightOffset.magnitude:0.000})m";
    }

    /// <summary>
    /// 基準スケールに対する現在値の相対変化量を計算します。
    /// </summary>
    /// <param name="reference">比較基準となる軸方向スケール。</param>
    /// <param name="current">現在の軸方向スケール。</param>
    /// <returns>変化率の絶対値。基準値がほぼゼロなら0。</returns>
    private static float ScaleDifference(float reference, float current)
    {
        var baseScale = Mathf.Abs(reference);
        return baseScale < 0.001f ? 0f : Mathf.Abs(current / reference - 1f);
    }

    /// <summary>
    /// アバターの骨盤・左右足首の現在のワールド位置を返します。
    /// </summary>
    /// <param name="waist">骨盤のワールド位置を受け取ります。</param>
    /// <param name="left">左足首のワールド位置を受け取ります。</param>
    /// <param name="right">右足首のワールド位置を受け取ります。</param>
    public void GetTrackingPoints(out Vector3 waist, out Vector3 left, out Vector3 right)
    {
        waist = _hips.position;
        left = _leftFoot.position;
        right = _rightFoot.position;
    }

    /// <summary>
    /// 腰・骨盤・頭およびソルバーの状態を比較する診断文を作成します。
    /// </summary>
    /// <param name="camera">ゲーム内のHMD視点として使用するカメラ。</param>
    /// <returns>目標適用後の診断文。目標が未適用、または必要な目標が存在しない場合はnull。</returns>
    public string? PoseDiagnostics(Camera camera)
    {
        if (!_hasAppliedTargets || _pelvisTarget == null || _leftTarget == null ||
            _rightTarget == null) return null;
        var pelvisError = Vector3.Distance(_hips.position, _pelvisTarget.position);
        var pelvisAngle = Quaternion.Angle(_hips.rotation, _pelvisTarget.rotation);
        var leftError = Vector3.Distance(LeftLegEnd.position, _leftTarget.position);
        var rightError = Vector3.Distance(RightLegEnd.position, _rightTarget.position);
        var summary = $"waist input={_rawWaist.Position}, pelvis target={_pelvisTarget.position}, " +
            $"waist input/target travel=({Vector3.Distance(_rawWaist.Position, _waistCalibrationPosition):0.000}/" +
            $"{Vector3.Distance(_pelvisTarget.position, _pelvisCalibrationPosition):0.000})m, " +
            $"waist input/target rotation=({Quaternion.Angle(_rawWaist.Rotation, _waistReferenceRotation):0.#}/" +
            $"{Quaternion.Angle(_pelvisTarget.rotation, _hipsReferenceRotation):0.#})deg, " +
            $"IK weight={_boundSolver?.IKPositionWeight:0.###}, LOD={_boundSolver?.LOD}, " +
            $"pelvis weights=({_boundSolver?.spine?.pelvisPositionWeight:0.###}/" +
            $"{_boundSolver?.spine?.pelvisRotationWeight:0.###}), " +
            $"solves={_solveCount}, solve age={(_lastSolveTime < 0f ? -1f : Time.unscaledTime - _lastSolveTime):0.000}s, " +
            $"post-solve pelvis error={_pelvisErrorAfterSolve:0.000}m/{_pelvisAngleAfterSolve:0.#}deg, " +
            $"late pelvis error={pelvisError:0.000}m/{pelvisAngle:0.#}deg, " +
            $"feet bone-target=({leftError:0.000},{rightError:0.000})m, " +
            $"post-solve feet error=({_leftFootErrorAfterSolve:0.000}/{_rightFootErrorAfterSolve:0.000})m, " +
            $"toe neutral correction=({_leftToeRotation.CorrectionDegrees:0.#}/{_rightToeRotation.CorrectionDegrees:0.#})deg, " +
            $"dual toes=({_additional.DualLeft}/{_additional.DualRight}), " +
            $"left input/target travel=({Vector3.Distance(_lastLeftPose.Position, _leftTrackerReferencePosition):0.000}/" +
            $"{Vector3.Distance(_leftTarget.position, _leftTargetReferencePosition):0.000})m, " +
            $"right input/target travel=({Vector3.Distance(_lastRightPose.Position, _rightTrackerReferencePosition):0.000}/" +
            $"{Vector3.Distance(_rightTarget.position, _rightTargetReferencePosition):0.000})m, " +
            $"feet input positions=({_lastLeftPose.Position}/{_lastRightPose.Position}), " +
            $"feet target positions=({_leftTarget.position}/{_rightTarget.position}), " +
            $"chestGoalWeight={_ik?.solver?.spine?.chestGoalWeight:0.###}";
        var head = Animator.GetBoneTransform(HumanBodyBones.Head);
        var headTarget = _ik?.solver?.spine?.headTarget;
        if (!_hasHeadReference || head == null || headTarget == null) return summary;
        var hmdMotion = camera.transform.rotation * Quaternion.Inverse(_hmdReferenceRotation);
        var targetMotion = headTarget.rotation * Quaternion.Inverse(_headTargetReferenceRotation);
        var boneMotion = head.rotation * Quaternion.Inverse(_headReferenceRotation);
        return summary + $", head motion HMD/target/bone=" +
            $"({Quaternion.Angle(Quaternion.identity, hmdMotion):0.#}/" +
            $"{Quaternion.Angle(Quaternion.identity, targetMotion):0.#}/" +
            $"{Quaternion.Angle(Quaternion.identity, boneMotion):0.#})deg, " +
            $"target-bone motion difference={Quaternion.Angle(targetMotion, boneMotion):0.#}deg";
    }

    /// <summary>
    /// フィルター前の腰姿勢を、追跡入力と骨盤の比較診断用に記録します。
    /// </summary>
    /// <param name="waist">腰のワールド姿勢。</param>
    public void RecordRawWaist(Pose waist) => _rawWaist = waist;

    /// <summary>
    /// 校正位置からの腰入力と骨盤の移動量を比較する診断文です。
    /// </summary>
    public string WaistMotionSummary => !_calibrated || _pelvisTarget == null ? "Waist: calibration required" :
        $"Waist: {Vector3.Distance(_rawWaist.Position, _waistCalibrationPosition):0.00}m " +
        $"{Quaternion.Angle(_rawWaist.Rotation, _waistReferenceRotation):0}deg -> target: " +
        $"{Vector3.Distance(_pelvisTarget.position, _pelvisCalibrationPosition):0.00}m " +
        $"{Quaternion.Angle(_pelvisTarget.rotation, _hipsReferenceRotation):0}deg";

    /// <summary>
    /// ソルバーの待機・稼働状態と骨盤誤差を表示する診断文です。
    /// </summary>
    public string SolverStatus => _usingGameTargets ? "Body IK: game (waist lost / paused)" :
        !_calibrated ? "Body IK: calibration required" :
        _ik == null || !_ik.enabled ? "Body IK: bone fallback" :
        _lastSolveTime < 0f || Time.unscaledTime - _lastSolveTime > 0.5f ? "Body IK: waiting for solver" :
        $"Body IK: waist | pelvis error {_pelvisErrorAfterSolve:0.000} m";

    /// <summary>
    /// 選択中ローダーを優先して対象HumanoidとVRIKを探し、同じ骨格なら既存管理を再利用します。
    /// </summary>
    /// <param name="camera">ゲーム内のHMD視点として使用するカメラ。</param>
    /// <param name="config">使用する共有設定。保存処理がないメソッドではファイルへの書き込みは行いません。</param>
    /// <param name="current">再利用を検討する現在のアバター管理。初回はnull。</param>
    /// <returns>対象のアバター管理。ロード中または対象がなければnull。</returns>
    /// <remarks>選択中ローダーのモデルは、校正前に大きすぎて頭がHMDから離れていても候補に残します。</remarks>
    public static AvatarIK? Find(Camera camera, ModConfig config, AvatarIK? current = null)
    {
        Animator? loaderAnimator = null;
        VRIK? loaderIk = null;
        string? loaderKey = null;
        var loaderDistance = float.MaxValue;
        var activeLoader = AvatarLoader.s_instance;
        if (activeLoader != null && activeLoader.gameObject.activeInHierarchy && activeLoader.avatarIsLoading) return null;
        foreach (var loader in Resources.FindObjectsOfTypeAll<AvatarLoader>())
        {
            if (loader == null || !loader.gameObject.activeInHierarchy || loader.vrmObject == null ||
                !loader.vrmObject.activeInHierarchy) continue;
            var selectedAnimator = loader.vrmObject.GetComponentInChildren<Animator>();
            if (!IsSuitable(selectedAnimator)) continue;
            var head = selectedAnimator.GetBoneTransform(HumanBodyBones.Head);
            var distance = Vector3.Distance(head.position, camera.transform.position);
            if (loader != activeLoader && distance > 4f) continue;
            // The selected rig can be far taller than the player before size
            // fitting. Do not lose it to a nearby preview/multiplayer animator.
            var score = loader == activeLoader ? -1f : distance;
            if (score >= loaderDistance) continue;
            loaderDistance = score;
            loaderAnimator = selectedAnimator;
            loaderIk = FindMatchingIk(selectedAnimator, loader.ik);
            loaderKey = AvatarHmdVisibility.AvatarKey(loader);
        }
        if (loaderAnimator != null)
        {
            var found = ReuseOrCreate(loaderAnimator, loaderIk, config, current);
            if (!string.IsNullOrEmpty(loaderKey)) found.AvatarKey = loaderKey;
            else if (found.AvatarKey.Length == 0) found.AvatarKey = "instance:" + loaderAnimator.GetInstanceID();
            return found;
        }

        Animator? best = null;
        var bestDistance = float.MaxValue;
        foreach (var animator in Resources.FindObjectsOfTypeAll<Animator>())
        {
            if (!IsSuitable(animator)) continue;
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            var distance = Vector3.Distance(head.position, camera.transform.position);
            if (distance < 2f && distance < bestDistance) { best = animator; bestDistance = distance; }
        }
        return best == null ? null : ReuseOrCreate(best,
            FindMatchingIk(best, best.GetComponentInParent<VRIK>()), config, current);
    }

    /// <summary>
    /// Animator・主要ボーン・VRIKの一致を確認して既存管理を再利用、または作り直します。
    /// </summary>
    /// <param name="animator">対象HumanoidアバターのAnimator。</param>
    /// <param name="ik">対象アバターのVRIK。見つからない場合はnull。</param>
    /// <param name="config">使用する共有設定。保存処理がないメソッドではファイルへの書き込みは行いません。</param>
    /// <param name="current">再利用を検討する現在のアバター管理。初回はnull。</param>
    /// <returns>対象骨格に対応する管理インスタンス。</returns>
    private static AvatarIK ReuseOrCreate(Animator animator, VRIK? ik, ModConfig config, AvatarIK? current)
    {
        var sameBones = current != null && current.Animator == animator &&
            current._hips == animator.GetBoneTransform(HumanBodyBones.Hips) &&
            current._leftFoot == animator.GetBoneTransform(HumanBodyBones.LeftFoot) &&
            current._rightFoot == animator.GetBoneTransform(HumanBodyBones.RightFoot);
        if (sameBones && current!._ik == ik) return current;
        return new AvatarIK(animator, ik, config,
            sameBones ? current!._calibrationPose : null)
            { AvatarKey = current != null && current.Animator == animator ? current.AvatarKey : "instance:" + animator.GetInstanceID() };
    }

    /// <summary>
    /// 対象の骨盤を参照するVRIKを、指定候補・親・子の順に探します。
    /// </summary>
    /// <param name="animator">対象HumanoidアバターのAnimator。</param>
    /// <param name="preferred">優先的に一致を確認するVRIK。</param>
    /// <returns>対応するVRIK。見つからない場合はnull。</returns>
    private static VRIK? FindMatchingIk(Animator animator, VRIK? preferred)
    {
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        if (IkMatchesAvatar(preferred, hips)) return preferred;
        var parent = animator.GetComponentInParent<VRIK>();
        if (IkMatchesAvatar(parent, hips)) return parent;
        foreach (var candidate in animator.GetComponentsInChildren<VRIK>(true))
            if (IkMatchesAvatar(candidate, hips)) return candidate;
        return null;
    }

    /// <summary>
    /// VRIKの骨盤参照が対象アバターの骨盤と一致するかを確認します。
    /// </summary>
    /// <param name="ik">対象アバターのVRIK。見つからない場合はnull。</param>
    /// <param name="hips">対象アバターの骨盤ボーン。</param>
    /// <returns>有効なVRIKが指定骨盤を参照していればtrue。</returns>
    private static bool IkMatchesAvatar(VRIK? ik, Transform hips) =>
        ik != null && ik.gameObject.activeInHierarchy &&
        ik.references != null && ik.references.pelvis == hips;

    /// <summary>
    /// 全身駆動に必要な頭・骨盤・左右脚のボーンを備えた有効なHumanoidかを確認します。
    /// </summary>
    /// <param name="animator">対象HumanoidアバターのAnimator。</param>
    /// <returns>必要なボーンを取得できる場合はtrue。</returns>
    private static bool IsSuitable(Animator? animator) =>
        animator != null && animator.gameObject.activeInHierarchy && animator.isHuman &&
        animator.GetBoneTransform(HumanBodyBones.Head) != null &&
        animator.GetBoneTransform(HumanBodyBones.Hips) != null &&
        animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg) != null &&
        animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg) != null &&
        animator.GetBoneTransform(HumanBodyBones.LeftFoot) != null &&
        animator.GetBoneTransform(HumanBodyBones.RightUpperLeg) != null &&
        animator.GetBoneTransform(HumanBodyBones.RightLowerLeg) != null &&
        animator.GetBoneTransform(HumanBodyBones.RightFoot) != null;

    /// <summary>
    /// 校正済みの入力から骨盤・足・膝・胸の目標を更新し、VRIKまたは直接ボーン駆動へ反映します。
    /// </summary>
    /// <param name="waist">腰のワールド姿勢。</param>
    /// <param name="left">左足のワールド姿勢。</param>
    /// <param name="right">右足のワールド姿勢。</param>
    /// <param name="camera">ゲーム内のHMD視点として使用するカメラ。</param>
    /// <param name="hasLeft">左足の追跡姿勢が有効か。</param>
    /// <param name="hasRight">右足の追跡姿勢が有効か。</param>
    /// <param name="additional">部位ごとの追跡姿勢。校正・駆動処理ではゲームのワールド空間を使用します。</param>
    public void Apply(Pose waist, Pose left, Pose right, Camera camera,
        bool hasLeft, bool hasRight, IReadOnlyDictionary<TrackerRole, Pose> additional)
    {
        if (!_calibrated)
        {
            if (_requireExplicitCalibration || !hasLeft || !hasRight) return;
            if (!Calibrate(waist, left, right, camera, additional)) return;
        }
        if (hasLeft) _lastLeftPose = left; else left = _lastLeftPose;
        if (hasRight) _lastRightPose = right; else right = _lastRightPose;
        _additional.Update(additional);
        if (!_additional.HeadTrackingAvailable && _rootController != null) _rootController.enabled = false;
        _leftKneeTracked = _additional.TryGet(TrackerRole.LeftKnee, out var leftKnee);
        _rightKneeTracked = _additional.TryGet(TrackerRole.RightKnee, out var rightKnee);

        var pelvisDelta = CorrectPelvisDelta(waist.Rotation, out var pelvisYaw);
        // The tracker is usually mounted behind the body. Tilting that long
        // calibration offset makes the pelvis orbit the tracker on some VRMs.
        var pelvisPosition = waist.Position + pelvisYaw * _waistWorldOffset;
        var pelvisRotation = pelvisDelta * _hipsReferenceRotation;
        var leftPosition = left.Position + left.Rotation * _leftOffset;
        var leftRotation = left.Rotation * _leftRotationOffset;
        var rightPosition = right.Position + right.Rotation * _rightOffset;
        var rightRotation = right.Rotation * _rightRotationOffset;
        var leftHip = pelvisPosition + pelvisRotation * _leftHipLocalOffset;
        var rightHip = pelvisPosition + pelvisRotation * _rightHipLocalOffset;
        if (!_additional.DualLeft) leftPosition = ExtendRaisedLeg(leftPosition, leftHip, waist.Position, left.Position,
            _leftTrackerReferencePosition, _leftUserLegReferenceDistance,
            _leftAvatarLegReferenceDistance, _leftUpperLength + _leftLowerLength);
        if (!_additional.DualRight) rightPosition = ExtendRaisedLeg(rightPosition, rightHip, waist.Position, right.Position,
            _rightTrackerReferencePosition, _rightUserLegReferenceDistance,
            _rightAvatarLegReferenceDistance, _rightUpperLength + _rightLowerLength);
        var leftBend = FromNumerics(AvatarPoseMath.LegBend(
            ToNumerics(pelvisYaw * _leftLegAxisReference),
            ToNumerics(pelvisYaw * _leftKneeDirectionReference), ToNumerics(leftPosition - leftHip)));
        var rightBend = FromNumerics(AvatarPoseMath.LegBend(
            ToNumerics(pelvisYaw * _rightLegAxisReference),
            ToNumerics(pelvisYaw * _rightKneeDirectionReference), ToNumerics(rightPosition - rightHip)));
        if (_pelvisTarget == null) AttachIkTargets();
        if (_pelvisTarget != null && _leftTarget != null && _rightTarget != null &&
            _ik != null && _ik.enabled)
        {
            _hasLeftTracking = hasLeft;
            _hasRightTracking = hasRight;
            _pelvisTarget.SetPositionAndRotation(pelvisPosition, pelvisRotation);
            // With separate ankle + toe trackers, keep the ankle position and
            // use their calibrated bone positions for the foot's pointing axis.
            // Bone length stays fixed; noisy measurements cannot stretch it.
            if (hasLeft && _additional.DualLeft && _additional.TryGet(TrackerRole.LeftToes, out var leftToe))
                leftRotation = FromNumerics(DualFootMath.Rotation(ToNumerics(leftPosition), ToNumerics(leftRotation),
                    _leftEndOffset.Position, ToNumerics(leftToe.Position)));
            if (hasRight && _additional.DualRight && _additional.TryGet(TrackerRole.RightToes, out var rightToe))
                rightRotation = FromNumerics(DualFootMath.Rotation(ToNumerics(rightPosition), ToNumerics(rightRotation),
                    _rightEndOffset.Position, ToNumerics(rightToe.Position)));
            var leftEnd = _leftEndOffset.Apply(ToNumerics(leftPosition), ToNumerics(leftRotation));
            var rightEnd = _rightEndOffset.Apply(ToNumerics(rightPosition), ToNumerics(rightRotation));
            _leftTarget.SetPositionAndRotation(FromNumerics(leftEnd.Position), FromNumerics(leftEnd.Rotation));
            _rightTarget.SetPositionAndRotation(FromNumerics(rightEnd.Position), FromNumerics(rightEnd.Rotation));
            if (_leftBendGoal != null || _rightBendGoal != null)
            {
                // Use the calibrated hip sockets. Reading the solved bones here
                // feeds VRIK's previous output back into the knee guides.
                if (_leftBendGoal != null)
                    _leftBendGoal.position = _leftKneeTracked ? leftKnee.Position : BendGoalPosition(leftHip, leftPosition,
                        _leftUpperLength, leftBend);
                if (_rightBendGoal != null)
                    _rightBendGoal.position = _rightKneeTracked ? rightKnee.Position : BendGoalPosition(rightHip, rightPosition,
                        _rightUpperLength, rightBend);
            }
            if (_rootController != null && !_rootController.enabled && _trackingBlend >= 1f && _additional.HeadTrackingAvailable)
            {
                _rootController.enabled = true;
                // Resume the original calibration; the recovery pose may be a
                // crouch or a kick and must not become the new root reference.
                if (_boundSolver?.spine != null) _boundSolver.spine.maxRootAngle = 180f;
            }
            UpdateVirtualChest(pelvisDelta, pelvisPosition, pelvisRotation);
            ApplyTrackedConstraints();
            _hasAppliedTargets = true;
            return;
        }

        // An enabled solver owns the bones even while its setup is incomplete.
        // Writing a second IK result here would compete with its next update.
        if (_ik != null && _ik.enabled) return;
        _hips.SetPositionAndRotation(pelvisPosition, pelvisRotation);
        var maxKneeFlexion = Mathf.Clamp(_config.KneeMaxFlexionDegrees, 60f, 170f);
        if (hasLeft) SolveLeg(_leftUpper, _leftLower, _leftFoot,
            leftPosition, leftRotation, leftBend, maxKneeFlexion);
        if (hasRight) SolveLeg(_rightUpper, _rightLower, _rightFoot,
            rightPosition, rightRotation, rightBend, maxKneeFlexion);
        ApplyUnmeasuredToeRotations(hasLeft, hasRight, 1f);
    }

    /// <summary>
    /// 胸トラッカーの代わりに腰の向きを使用し、平滑化した胸の目標を更新します。
    /// </summary>
    /// <param name="pelvisDelta">校正時からの補正済み骨盤回転差。</param>
    /// <param name="pelvisPosition">目標とする骨盤のワールド位置。</param>
    /// <param name="pelvisRotation">目標とする骨盤のワールド回転。</param>
    private void UpdateVirtualChest(Quaternion pelvisDelta, Vector3 pelvisPosition,
        Quaternion pelvisRotation)
    {
        if (_chestGoal == null || _chest == null) return;
        var desired = (pelvisDelta * _bodyForwardReference).normalized;
        if (_smoothedChestForward.sqrMagnitude < 0.001f)
            _smoothedChestForward = desired;
        else
            _smoothedChestForward = Vector3.RotateTowards(_smoothedChestForward,
                desired, 240f * Mathf.Deg2Rad * Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.1f), 0f);
        // With no chest tracker, the waist is the only torso orientation input.
        // HMD pitch/yaw and controller motion belong to the head/arms, not this aim.
        _chestGoal.position = pelvisPosition + pelvisRotation * _chestLocalOffset +
            _smoothedChestForward * 0.5f;
        _smoothedChestWeight = Mathf.Clamp01(_config.ChestGoalWeight);
    }
    /// <summary>
    /// 腰の校正時からの回転差を水平旋回と傾きに分け、傾きだけを弱めて制限します。
    /// </summary>
    /// <param name="waistRotation">腰入力の現在のワールド回転。</param>
    /// <param name="yaw">抽出した水平旋回の回転を受け取ります。</param>
    /// <returns>水平旋回を保持した補正後の骨盤回転差。</returns>
    private Quaternion CorrectPelvisDelta(Quaternion waistRotation, out Quaternion yaw)
    {
        var delta = waistRotation * Quaternion.Inverse(_waistReferenceRotation);
        yaw = FromNumerics(AvatarPoseMath.PelvisYaw(ToNumerics(delta), ToNumerics(_lastPelvisYaw)));
        _lastPelvisYaw = yaw;
        var tilt = Quaternion.Inverse(yaw) * delta;
        var softenedTilt = Quaternion.Slerp(Quaternion.identity, tilt,
            Mathf.Clamp01(_config.PelvisTiltWeight));
        return yaw * Quaternion.RotateTowards(Quaternion.identity, softenedTilt,
            Mathf.Clamp(_config.PelvisMaxTiltDegrees, 0f, 90f));
    }

    /// <summary>
    /// 持ち上げた足の目標へ体格差に応じた伸展補正を加え、接地した足は移動させません。
    /// </summary>
    /// <param name="target">要求する足またはボーン目標の位置。基準と同じ座標系です。</param>
    /// <param name="avatarHip">アバター側の股関節のワールド位置。</param>
    /// <param name="userWaist">現在の腰入力のワールド位置。</param>
    /// <param name="userFoot">現在の足入力のワールド位置。</param>
    /// <param name="referenceFoot">校正時の足入力のワールド位置。</param>
    /// <param name="userReferenceDistance">校正時の腰入力から足入力までの距離。単位はmです。</param>
    /// <param name="avatarReferenceDistance">校正時のアバターの股関節から足首までの距離。単位はmです。</param>
    /// <param name="avatarLegLength">アバターの大腿と下腿を合わせた長さ。単位はmです。</param>
    /// <returns>補正後の足目標。補正条件を満たさない場合は元の目標。</returns>
    private static Vector3 ExtendRaisedLeg(Vector3 target, Vector3 avatarHip,
        Vector3 userWaist, Vector3 userFoot, Vector3 referenceFoot,
        float userReferenceDistance, float avatarReferenceDistance, float avatarLegLength)
    {
        if (userReferenceDistance < 0.3f || avatarReferenceDistance < 0.3f ||
            avatarLegLength < 0.3f) return target;
        var rise = userFoot.y - referenceFoot.y;
        var travel = Vector3.Distance(userFoot, referenceFoot);
        // A planted foot must remain at its tracked world position when the
        // pelvis sways. Add reach only when the player actually lifts a foot.
        var amount = Mathf.InverseLerp(0.08f, 0.30f, rise) *
                     Mathf.InverseLerp(0.12f, 0.30f, travel);
        if (amount <= 0f) return target;
        var leg = target - avatarHip;
        var distance = leg.magnitude;
        if (distance < 0.01f) return target;
        var userExtension = Mathf.Clamp(
            Vector3.Distance(userWaist, userFoot) / userReferenceDistance, 0.6f, 1.1f);
        var maxReach = avatarLegLength - 0.005f;
        var desired = Mathf.Lerp(avatarReferenceDistance * userExtension,
            maxReach * Mathf.Min(userExtension, 1f), amount);
        var correction = Mathf.Clamp(desired - distance, 0f, 0.25f) * amount;
        return target + leg / distance * correction;
    }

    /// <summary>
    /// 脚の軸に対する膝の曲がる方向を求め、脚が直線なら正面方向を代用します。
    /// </summary>
    /// <param name="hip">股関節の位置。足と膝の位置と同じ座標系です。</param>
    /// <param name="knee">膝の位置。ほかの関節位置と同じ座標系です。</param>
    /// <param name="foot">足首の位置。ほかの関節位置と同じ座標系です。</param>
    /// <param name="forward">入力位置と同じ座標系におけるユーザーまたはアバターの正面方向。</param>
    /// <returns>膝の曲げ方向。射影が縮退する場合は指定正面方向。</returns>
    private static Vector3 KneeDirection(Vector3 hip, Vector3 knee, Vector3 foot,
        Vector3 forward)
    {
        var axis = foot - hip;
        var bend = Vector3.ProjectOnPlane(knee - hip, axis);
        if (bend.sqrMagnitude < 0.0004f)
            bend = Vector3.ProjectOnPlane(forward, axis);
        return bend.sqrMagnitude > 0.0001f ? bend.normalized : forward;
    }

    /// <summary>
    /// 股関節・脚軸・曲げ方向からVRIK用の膝ガイド位置を作成します。
    /// </summary>
    /// <param name="hip">股関節の位置。足と膝の位置と同じ座標系です。</param>
    /// <param name="foot">足首の位置。ほかの関節位置と同じ座標系です。</param>
    /// <param name="upperLength">大腿の長さ。位置入力と同じ単位です。</param>
    /// <param name="bend">脚の曲がる側を指定する方向ベクトル。</param>
    /// <returns>ワールド空間の膝ガイド位置。</returns>
    private static Vector3 BendGoalPosition(Vector3 hip, Vector3 foot,
        float upperLength, Vector3 bend)
    {
        var axis = foot - hip;
        return hip + axis.normalized * (upperLength * 0.5f) + bend * upperLength;
    }

    /// <summary>
    /// ゲームのIK状態を保存してMODの目標・制約・コールバックを接続します。
    /// </summary>
    private void AttachIkTargets()
    {
        if (_ikUnavailable || _pelvisTarget != null || _ik == null || !_ik.enabled ||
            !_ik.gameObject.activeInHierarchy)
            return;
        try
        {
            var solver = _ik.solver;
            if (solver == null || !solver.initiated || solver.spine == null ||
                solver.leftLeg == null || solver.rightLeg == null)
                return;

            _previousPelvisTarget = solver.spine.pelvisTarget;
            _previousLeftTarget = solver.leftLeg.target;
            _previousRightTarget = solver.rightLeg.target;
            _previousChestGoal = solver.spine.chestGoal;
            _previousLeftBendGoal = solver.leftLeg.bendGoal;
            _previousRightBendGoal = solver.rightLeg.bendGoal;
            _previousLeftBendGoalWeight = solver.leftLeg.bendGoalWeight;
            _previousRightBendGoalWeight = solver.rightLeg.bendGoalWeight;
            _previousChestGoalWeight = solver.spine.chestGoalWeight;
            _previousPelvisPositionWeight = solver.spine.pelvisPositionWeight;
            _previousPelvisRotationWeight = solver.spine.pelvisRotationWeight;
            _previousLeftPositionWeight = solver.leftLeg.positionWeight;
            _previousLeftRotationWeight = solver.leftLeg.rotationWeight;
            _previousRightPositionWeight = solver.rightLeg.positionWeight;
            _previousRightRotationWeight = solver.rightLeg.rotationWeight;
            _previousLocomotionWeight = solver.locomotion != null ? solver.locomotion.weight : 0f;
            _previousPlantFeet = solver.plantFeet;
            _previousNeckStiffness = solver.spine.neckStiffness;
            _previousHeadClampWeight = solver.spine.headClampWeight;
            _previousMaintainPelvisPosition = solver.spine.maintainPelvisPosition;
            _previousMinHeadHeight = solver.spine.minHeadHeight;
            _previousMaxRootAngle = solver.spine.maxRootAngle;
            _previousBodyPosStiffness = solver.spine.bodyPosStiffness;
            _previousBodyRotStiffness = solver.spine.bodyRotStiffness;
            _previousRotateChestByHands = solver.spine.rotateChestByHands;
            _previousMoveBodyBack = solver.spine.moveBodyBackWhenCrouching;
            _boundSolver = solver;
            _hasIkSnapshot = true;
            _usingGameTargets = false;
            _solveError = null;
            _hasLeftTracking = _hasRightTracking = true;

            _pelvisTarget = CreateTarget("Pelvis", _hips);
            _leftTarget = CreateTarget("LeftFoot", LeftLegEnd);
            _rightTarget = CreateTarget("RightFoot", RightLegEnd);
            if (_chest != null &&
                _ik.references != null && _ik.references.chest != null &&
                _config.ChestGoalWeight > 0f)
                _chestGoal = CreateTarget("ChestAim", _chest);
            if (_config.KneeBendGoalWeight > 0f || _additional.Has(TrackerRole.LeftKnee) || _additional.Has(TrackerRole.RightKnee))
            {
                if (_previousLeftBendGoal == null || _additional.Has(TrackerRole.LeftKnee))
                    _leftBendGoal = CreateTarget("LeftKnee", _leftLower);
                if (_previousRightBendGoal == null || _additional.Has(TrackerRole.RightKnee))
                    _rightBendGoal = CreateTarget("RightKnee", _rightLower);
            }
            solver.spine.pelvisTarget = _pelvisTarget;
            _trackingBlend = 1f;
            solver.spine.pelvisPositionWeight = Mathf.Clamp01(_config.PelvisPositionWeight);
            solver.spine.pelvisRotationWeight = Mathf.Clamp01(_config.PelvisRotationWeight);
            if (_chestGoal != null)
            {
                solver.spine.chestGoal = _chestGoal;
                solver.spine.chestGoalWeight = Mathf.Clamp01(_config.ChestGoalWeight);
            }
            solver.leftLeg.target = _leftTarget;
            solver.leftLeg.positionWeight = 1f;
            solver.leftLeg.rotationWeight = 1f;
            solver.rightLeg.target = _rightTarget;
            solver.rightLeg.positionWeight = 1f;
            solver.rightLeg.rotationWeight = 1f;
            if (_leftBendGoal != null)
            {
                solver.leftLeg.bendGoal = _leftBendGoal;
                solver.leftLeg.bendGoalWeight = Mathf.Clamp01(_config.KneeBendGoalWeight);
            }
            if (_rightBendGoal != null)
            {
                solver.rightLeg.bendGoal = _rightBendGoal;
                solver.rightLeg.bendGoalWeight = Mathf.Clamp01(_config.KneeBendGoalWeight);
            }
            if (solver.locomotion != null) solver.locomotion.weight = 0f;
            // With real foot and pelvis targets, VRIK's head-driven standing pose
            // constraints can compete with the trackers and stiffen the neck.
            solver.plantFeet = false;
            solver.spine.neckStiffness = 0f;
            solver.spine.headClampWeight = Mathf.Min(_previousHeadClampWeight, 0.4f);
            solver.spine.maintainPelvisPosition = 0f;
            solver.spine.minHeadHeight = 0f;
            _additional.Attach(solver);
            AttachRootController();
            ApplyTrackedConstraints();
            _beforeSolve = (Action)BeforeSolve;
            solver.OnPreUpdate += _beforeSolve;
            _afterSolve = (Action)AfterSolve;
            solver.OnPostUpdate += _afterSolve;
        }
        catch (Exception exception)
        {
            _ikUnavailable = true;
            IkError = $"VRIK target setup failed: {exception.GetType().Name}: {exception.Message}";
            try { ReleaseIkTargets(); } catch { /* Release remaining objects even after partial setup. */ }
        }
    }

    /// <summary>
    /// 指定ボーンの姿勢で、シーン移動時も残るIK目標を作成します。
    /// </summary>
    /// <param name="name">作成するオブジェクトまたはUI部品の名前。</param>
    /// <param name="bone">対象ボーンのTransform。</param>
    /// <returns>作成した目標のTransform。</returns>
    private static Transform CreateTarget(string name, Transform bone)
    {
        var target = new GameObject($"SynthRidersFullBody.{name}Target");
        UnityEngine.Object.DontDestroyOnLoad(target);
        target.transform.SetPositionAndRotation(bone.position, bone.rotation);
        return target.transform;
    }

    /// <summary>
    /// 必要に応じてVRIKルートコントローラーを取得・追加し、現在の目標で校正します。
    /// </summary>
    private void AttachRootController()
    {
        if (!_config.UseVrikRootController || _ik?.references?.root == null ||
            _ik.solver == null) return;
        if (!_ik.solver.initiated || _ik.solver.spine?.headTarget == null)
        {
            IkError = "VRIK root control skipped: solver or head target is not ready";
            return;
        }
        try
        {
            var root = _ik.references.root;
            _rootController = root.GetComponent<VRIKRootController>();
            _createdRootController = _rootController == null;
            if (_createdRootController)
                _rootController = root.gameObject.AddComponent<VRIKRootController>();
            else
                _previousRootControllerEnabled = _rootController!.enabled;
            _rootController!.enabled = true;
            _rootController.Calibrate();
        }
        catch (Exception exception)
        {
            IkError = $"VRIK root controller unavailable: {exception.GetType().Name}: " +
                exception.Message;
            if (_rootController != null)
            {
                _rootController.enabled = false;
                if (_createdRootController) UnityEngine.Object.Destroy(_rootController);
                else _rootController.enabled = _previousRootControllerEnabled;
            }
            _rootController = null;
            _createdRootController = false;
        }
    }

    /// <summary>
    /// コールバックと追加目標を解除し、ゲームのIK状態を戻して所有目標を破棄します。
    /// </summary>
    private void ReleaseIkTargets()
    {
        _additional.Detach();
        var restoreRootCalibration = false;
        try
        {
            restoreRootCalibration = OwnsIkTargets();
            if (_rootController != null) _rootController.enabled = false;
            if (_boundSolver != null && _beforeSolve != null)
                _boundSolver.OnPreUpdate -= _beforeSolve;
            if (_boundSolver != null && _afterSolve != null)
                _boundSolver.OnPostUpdate -= _afterSolve;
            RestoreIkState();
        }
        finally
        {
            DestroyTarget(_pelvisTarget);
            DestroyTarget(_leftTarget);
            DestroyTarget(_rightTarget);
            DestroyTarget(_chestGoal);
            DestroyTarget(_leftBendGoal);
            DestroyTarget(_rightBendGoal);
            try
            {
                if (_rootController != null)
                {
                    if (_createdRootController)
                    {
                        _rootController.enabled = false;
                        UnityEngine.Object.Destroy(_rootController);
                    }
                    else
                    {
                        _rootController.enabled = _previousRootControllerEnabled;
                        if (_previousRootControllerEnabled && restoreRootCalibration)
                        {
                            try { _rootController.Calibrate(); }
                            catch { /* Game-owned component will resume its own update. */ }
                        }
                    }
                }
            }
            finally
            {
                _pelvisTarget = _leftTarget = _rightTarget = null;
                _chestGoal = null;
                _leftBendGoal = _rightBendGoal = null;
                _rootController = null;
                _createdRootController = false;
                _hasIkSnapshot = false;
                _boundSolver = null;
                _beforeSolve = null;
                _afterSolve = null;
                _usingGameTargets = false;
                _solveError = null;
            }
        }
    }

    /// <summary>
    /// 校正前に保存したVRIKの目標・重み・制約を、まだ存在するソルバーへ復元します。
    /// </summary>
    private void RestoreIkState()
    {
        if (!_hasIkSnapshot || _boundSolver == null) return;
        // Restore the solver we actually modified, even if the game has
        // since replaced VRIK.solver with another instance.
        var solver = _boundSolver;
        if (solver.spine != null && _pelvisTarget is not null &&
            solver.spine.pelvisTarget == _pelvisTarget)
        {
            solver.spine.pelvisTarget = _previousPelvisTarget;
            solver.spine.pelvisPositionWeight = _previousPelvisTarget != null ? _previousPelvisPositionWeight : 0f;
            solver.spine.pelvisRotationWeight = _previousPelvisTarget != null ? _previousPelvisRotationWeight : 0f;
        }
        if (solver.spine != null && _chestGoal is not null &&
            solver.spine.chestGoal == _chestGoal)
        {
            solver.spine.chestGoal = _previousChestGoal;
            solver.spine.chestGoalWeight = _previousChestGoal != null ? _previousChestGoalWeight : 0f;
        }
        if (solver.leftLeg != null && _leftTarget is not null && solver.leftLeg.target == _leftTarget)
        {
            solver.leftLeg.target = _previousLeftTarget;
            solver.leftLeg.positionWeight = _previousLeftTarget != null ? _previousLeftPositionWeight : 0f;
            solver.leftLeg.rotationWeight = _previousLeftTarget != null ? _previousLeftRotationWeight : 0f;
        }
        if (solver.rightLeg != null && _rightTarget is not null && solver.rightLeg.target == _rightTarget)
        {
            solver.rightLeg.target = _previousRightTarget;
            solver.rightLeg.positionWeight = _previousRightTarget != null ? _previousRightPositionWeight : 0f;
            solver.rightLeg.rotationWeight = _previousRightTarget != null ? _previousRightRotationWeight : 0f;
        }
        if (solver.leftLeg != null && _leftBendGoal is not null && solver.leftLeg.bendGoal == _leftBendGoal)
        {
            solver.leftLeg.bendGoal = _previousLeftBendGoal;
            solver.leftLeg.bendGoalWeight = _previousLeftBendGoal != null ? _previousLeftBendGoalWeight : 0f;
        }
        if (solver.rightLeg != null && _rightBendGoal is not null && solver.rightLeg.bendGoal == _rightBendGoal)
        {
            solver.rightLeg.bendGoal = _previousRightBendGoal;
            solver.rightLeg.bendGoalWeight = _previousRightBendGoal != null ? _previousRightBendGoalWeight : 0f;
        }
        if (solver.locomotion != null) solver.locomotion.weight = _previousLocomotionWeight;
        solver.plantFeet = _previousPlantFeet;
        if (solver.spine != null)
        {
            solver.spine.neckStiffness = _previousNeckStiffness;
            solver.spine.headClampWeight = _previousHeadClampWeight;
            solver.spine.maintainPelvisPosition = _previousMaintainPelvisPosition;
            solver.spine.minHeadHeight = _previousMinHeadHeight;
            solver.spine.maxRootAngle = _previousMaxRootAngle;
            solver.spine.bodyPosStiffness = _previousBodyPosStiffness;
            solver.spine.bodyRotStiffness = _previousBodyRotStiffness;
            solver.spine.rotateChestByHands = _previousRotateChestByHands;
            solver.spine.moveBodyBackWhenCrouching = _previousMoveBodyBack;
        }
    }

    /// <summary>
    /// MOD所有の目標が存在する場合、そのGameObjectを破棄します。
    /// </summary>
    /// <param name="target">破棄するMOD所有の目標。存在しない場合はnull。</param>
    private static void DestroyTarget(Transform? target)
    {
        try { if (target != null) UnityEngine.Object.Destroy(target.gameObject); }
        catch { /* A destroyed target must not prevent the other targets being released. */ }
    }

    /// <summary>
    /// 2ボーン解法で膝と足の到達位置を求め、骨の長さを保って脚の回転を適用します。
    /// </summary>
    /// <param name="upper">大腿のTransform。</param>
    /// <param name="lower">下腿のTransform。</param>
    /// <param name="foot">足首のTransform。</param>
    /// <param name="target">要求する足またはボーン目標の位置。基準と同じ座標系です。</param>
    /// <param name="footRotation">足首へ設定するワールド回転。</param>
    /// <param name="bend">脚の曲がる側を指定する方向ベクトル。</param>
    /// <param name="maxFlexionDegrees">膝の最大屈曲角。単位は度です。</param>
    private static void SolveLeg(Transform upper, Transform lower, Transform foot,
        Vector3 target, Quaternion footRotation, Vector3 bend, float maxFlexionDegrees)
    {
        var hip = upper.position;
        var upperLength = Vector3.Distance(hip, lower.position);
        var lowerLength = Vector3.Distance(lower.position, foot.position);
        if (!AvatarPoseMath.SolveTwoBone(ToNumerics(hip), ToNumerics(target),
                ToNumerics(foot.position - hip), ToNumerics(bend), upperLength, lowerLength,
                maxFlexionDegrees, out var knee, out var solvedFoot)) return;
        var kneeTarget = FromNumerics(knee);
        target = FromNumerics(solvedFoot);
        upper.rotation = Quaternion.FromToRotation(lower.position - hip, kneeTarget - hip) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(foot.position - lower.position, target - lower.position) * lower.rotation;
        foot.rotation = footRotation;
    }

    /// <summary>
    /// Unityの数学型を、成分の値を保ってSystem.Numericsの型へ変換します。
    /// </summary>
    /// <param name="value">変換または検証する値。</param>
    /// <returns>同じ成分を持つSystem.Numericsの値。</returns>
    private static System.Numerics.Vector3 ToNumerics(Vector3 value) => new(value.x, value.y, value.z);
    /// <summary>
    /// System.Numericsの数学型を、成分の値を保ってUnityの型へ変換します。
    /// </summary>
    /// <param name="value">変換または検証する値。</param>
    /// <returns>同じ成分を持つUnityの値。</returns>
    private static Vector3 FromNumerics(System.Numerics.Vector3 value) => new(value.X, value.Y, value.Z);
    /// <summary>
    /// Unityの数学型を、成分の値を保ってSystem.Numericsの型へ変換します。
    /// </summary>
    /// <param name="value">変換または検証する値。</param>
    /// <returns>同じ成分を持つSystem.Numericsの値。</returns>
    private static System.Numerics.Quaternion ToNumerics(Quaternion value) =>
        new(value.x, value.y, value.z, value.w);
    /// <summary>
    /// System.Numericsの数学型を、成分の値を保ってUnityの型へ変換します。
    /// </summary>
    /// <param name="value">変換または検証する値。</param>
    /// <returns>同じ成分を持つUnityの値。</returns>
    private static Quaternion FromNumerics(System.Numerics.Quaternion value) =>
        new(value.X, value.Y, value.Z, value.W);
}
