using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SynthRidersFullBody;

/// <summary>
/// 設定パネルからMOD本体へ渡す操作要求です。
/// </summary>
internal enum PanelCommand
{
    /// <summary>
    /// 操作要求なし。
    /// </summary>
    None, 
    /// <summary>
    /// トラッカー割り当ての変更通知。
    /// </summary>
    AssignmentChanged, 
    /// <summary>
    /// 校正画面を開き直す要求。
    /// </summary>
    Recalibrate, 
    /// <summary>
    /// 全身追跡の有効状態を反転する要求。
    /// </summary>
    ToggleTracking, 
    /// <summary>
    /// 詳細パネルを閉じる要求。
    /// </summary>
    Close, 
    /// <summary>
    /// 校正カウントダウンを開始する要求。
    /// </summary>
    StartCalibration,
    /// <summary>準備確認から固定姿勢の選択へ進みます。</summary>
    PrepareCalibration,
    /// <summary>計測と固定姿勢を解除し、準備確認へ戻します。</summary>
    CancelCalibration,
    /// <summary>
    /// TポーズとStandingを切り替える要求。
    /// </summary>
    TogglePoseMode, 
    /// <summary>
    /// カメラガイドの表示を反転する要求。
    /// </summary>
    ToggleCameraGuide, 
    /// <summary>
    /// アバターの影を無効にする設定を反転する要求。
    /// </summary>
    ToggleAvatarShadows, 
    /// <summary>
    /// アバターのライトを無効にする設定を反転する要求。
    /// </summary>
    ToggleAvatarLights,
    /// <summary>
    /// アバターのHMD内表示を反転する要求。
    /// </summary>
    ToggleAvatarInHeadset,
    /// <summary>
    /// Ultimate Trackerの位置・回転の180度補正を切り替え、再校正を要求します。
    /// </summary>
    ToggleUltimateRotation,
    /// <summary>表情設定の変更を保存する要求。全身校正は無効化しません。</summary>
    ExpressionsChanged,
    /// <summary>UIの共通設定を保存します。</summary>
    UiPreferencesChanged,
    /// <summary>現在位置を基準に詳細パネルだけを置き直します。</summary>
    RepositionUi
}

/// <summary>
/// 固定位置の全身設定・校正・部位割り当て・描画設定とLIVE TRACKERSのUIを管理します。
/// </summary>
internal sealed partial class SettingsPanel
{
    private string _cameraGuideStatus = "";
    private string _cameraGuideLocation = "";
    /// <summary>
    /// カメラの検出結果と方向・距離を、描画設定画面の表示用に保持します。
    /// </summary>
    /// <param name="status">ユーザーまたはログへ表示する状態の説明。</param>
    /// <param name="location">カメラまでの方向・距離を示す表示文。</param>
    public void SetCameraGuideDetails(string status, string location)
    {
        _cameraGuideStatus = status;
        _cameraGuideLocation = location;
    }
    private static readonly Color PanelFill = new(0.10f, 0.065f, 0.22f, 0.97f);
    private static readonly Color Cyan = new(0.06f, 0.87f, 0.94f, 0.98f);
    private static readonly Color Pink = new(0.96f, 0.03f, 0.78f, 0.98f);
    private readonly TextMeshProUGUI?[] _rows = new TextMeshProUGUI?[PanelControls.RowCount];
    private readonly PanelControlState[] _controls = new PanelControlState[PanelControls.Count];
    private readonly PanelControlView?[] _views = new PanelControlView?[PanelControls.Count];
    private readonly PanelInputGate _input = new();
    private readonly PanelNavigation _navigation = new();
    private readonly CalibrationFlow _calibration = new();
    private CalibrationReadiness _readiness;
    private CalibrationStageView? _stageView;
    private readonly TextMeshProUGUI?[] _dashboardRows = new TextMeshProUGUI?[3];
    private readonly Image?[] _dashboardCards = new Image?[3];
    private readonly GameObject?[] _trackerMarkers = new GameObject?[3];
    private readonly GameObject?[] _avatarMarkers = new GameObject?[3];
    private readonly List<Material> _markerMaterials = new();
    private readonly Dictionary<string, (Pose Pose, float Time, float Speed, float Angular)> _lastSamples = new();
    private GameObject? _root;
    private GameObject? _dashboard;
    private TextMeshProUGUI? _title;
    private TextMeshProUGUI? _status;
    private TextMeshProUGUI? _footer;
    private TextMeshProUGUI? _dashboardStatus;
    private TextMeshProUGUI? _dashboardNextText;
    private Image? _leftCursor;
    private Image? _rightCursor;
    private bool _calibrationScreen => _navigation.Current == PanelPage.Calibration;
    private Vector3 _calibrationForward = Vector3.forward;
    private readonly CalibrationDirectionGuide _directionGuide = new();
    private readonly CalibrationGhostGuide _ghostGuide = new();
    private float _countdownRemaining = -1f;
    private string _bodyStatus = "Body IK: waiting";
    private string _waistMotion = "";
    private TPoseAssessment _poseAssessment;
    private bool _tPoseMode = true;
    private bool _roleScreen => _navigation.Current == PanelPage.Trackers;
    private bool _visualScreen => _navigation.Current == PanelPage.Display;
    private bool _expressionScreen => _navigation.Current == PanelPage.Expressions;
    private ExpressionButton? _expressionButton;
    private int _expressionHand;
    private int _expressionPage;
    private int _expressionPages = 1;
    private ModConfig? _expressionConfig;
    private AvatarExpressions? _expressions;
    private ExpressionInput _expressionInput;
    private readonly string?[] _visibleExpressions = new string?[3];

    /// <summary>表情設定画面を表示中か。ここではボタンの表情プレビューを許可します。</summary>
    public bool IsExpressionScreen => IsOpen && _expressionScreen;

    /// <summary>表情一覧・設定・入力状態を受け取り、設定画面の表示に使用します。</summary>
    public void SetExpressionContext(ModConfig config, AvatarExpressions expressions, ExpressionInput input)
    {
        _expressionConfig = config;
        _expressions = expressions;
        _expressionInput = input;
    }
    private TrackerRole? _selectedRole;
    private int _rolePage;
    private int _devicePage;
    private int _dashboardPage;
    private int _dashboardPages = 1;
    private string _waistHeightStatus = "";
    private float _nextTextUpdate;
    private bool _reopenAfterReset;

    /// <summary>
    /// 詳細パネルが有効な状態で表示されているかを示します。
    /// </summary>
    public bool IsOpen => _root != null && _root.activeSelf;
    /// <summary>詳細画面の操作や閉じた直後の押下を、背後の入口へ渡さないようにします。</summary>
    public bool BlocksEntryInput => IsOpen || !_input.Armed;
    /// <summary>
    /// 開いている詳細パネルが校正画面かを示します。
    /// </summary>
    public bool IsCalibrationScreen => IsOpen && _calibrationScreen;
    /// <summary>
    /// 現在の校正画面が成功結果を保持しているかを示します。
    /// </summary>
    public bool IsCalibrationSucceeded => IsCalibrationScreen && _calibration.Succeeded;
    /// <summary>
    /// 現在の校正モードがTポーズかを示します。
    /// </summary>
    public bool UseTPose => _tPoseMode;
    /// <summary>
    /// 校正開始時に固定したワールド空間の正面方向です。
    /// </summary>
    public Vector3 CalibrationForward => _calibrationForward;
    /// <summary>
    /// 正面方向を鏡と開始時の視線のどちらから決めたかを示します。
    /// </summary>
    public string CalibrationDirectionSource => _directionGuide.Source;
    /// <summary>
    /// 未完了の校正画面でアバターの固定姿勢を表示する必要があるかを示します。
    /// </summary>
    public bool ShowCalibrationPose => IsCalibrationScreen && _calibration.Preview;
    /// <summary>有効なカウントダウン中か。計測側もこの状態を確認してから確定します。</summary>
    public bool IsCalibrationCountingDown => IsCalibrationScreen && _calibration.CountingDown;

    /// <summary>
    /// IK状態と腰の移動量を、設定画面の診断表示用に保持します。
    /// </summary>
    /// <param name="status">ユーザーまたはログへ表示する状態の説明。</param>
    /// <param name="waistMotion">腰入力と骨盤の移動量を示す診断文。</param>
    public void SetBodyDiagnostics(string status, string waistMotion)
    {
        _bodyStatus = status;
        _waistMotion = waistMotion;
    }

    /// <summary>
    /// 表示オブジェクトとキャッシュを破棄し、必要なら再生成後に画面を開き直す状態にします。
    /// </summary>
    public void ResetUi()
    {
        _feedback.Reset();
        try { _reopenAfterReset |= IsOpen; }
        catch { _reopenAfterReset = true; }
        _directionGuide.Dispose();
        _ghostGuide.Dispose();
        SafeDestroy(_root);
        foreach (var marker in _trackerMarkers) SafeDestroy(marker);
        foreach (var marker in _avatarMarkers) SafeDestroy(marker);
        foreach (var material in _markerMaterials)
            if (material != null) UnityEngine.Object.Destroy(material);
        _markerMaterials.Clear();
        _root = _dashboard = null;
        _stageView = null;
        _mountGuide = null;
        DiscardSelection();
        if (_calibration.Preview) _calibration.Cancel();
        _countdownRemaining = -1f;
        _title = _status = _footer = _dashboardStatus = _dashboardNextText = null;
        _leftCursor = _rightCursor = null;
        Array.Clear(_rows);
        Array.Clear(_views);
        Array.Clear(_controls);
        Array.Clear(_dashboardRows);
        Array.Clear(_dashboardCards);
        Array.Clear(_trackerMarkers);
        Array.Clear(_avatarMarkers);
        _lastSamples.Clear();
        _selectedRole = null;
        Array.Clear(_visibleDevices);
        _input.Block();
        _nextTextUpdate = 0f;
    }

    /// <summary>
    /// 既に破棄された可能性を考慮して、UIオブジェクトを安全に破棄します。
    /// </summary>
    /// <param name="gameObject">破棄するオブジェクト。未作成または破棄済みなら処理しません。</param>
    private static void SafeDestroy(GameObject? gameObject)
    {
        if (gameObject == null) return;
        try
        {
            gameObject.SetActive(false);
            UnityEngine.Object.Destroy(gameObject);
        }
        catch { /* The Unity object may already have been destroyed. */ }
    }

    /// <summary>
    /// TポーズとStandingを切り替え、前回の校正結果を解除します。
    /// </summary>
    public void TogglePoseMode()
    {
        if (_calibration.Stage != CalibrationStage.Pose) return;
        _tPoseMode = !_tPoseMode;
        _nextTextUpdate = 0f;
    }

    /// <summary>
    /// 校正画面を選択し、指定配置またはHMD基準の位置で開きます。
    /// </summary>
    /// <param name="camera">ゲーム内のHMD視点として使用するカメラ。</param>
    /// <param name="panelPose">パネルを固定するワールド姿勢。nullならHMD基準の初期配置を使用します。</param>
    public void OpenCalibration(Camera camera, PanelPlacement? panelPose = null)
    {
        DiscardSelection();
        _placement = panelPose;
        EnsureCreated();
        _reopenAfterReset = false;
        _navigation.Open(true);
        ClearCalibrationResult();
        SetOpen(true, camera);
    }

    /// <summary>
    /// 全身設定のトップ画面を、指定配置またはHMD基準の位置で開きます。
    /// </summary>
    /// <param name="camera">ゲーム内のHMD視点として使用するカメラ。</param>
    /// <param name="panelPose">パネルを固定するワールド姿勢。nullならHMD基準の初期配置を使用します。</param>
    public void OpenSettings(Camera camera, PanelPlacement? panelPose = null)
    {
        DiscardSelection();
        _placement = panelPose;
        EnsureCreated();
        _reopenAfterReset = false;
        _navigation.Open(false);
        SetOpen(true, camera);
    }

    /// <summary>
    /// 曲のプレイ中などメニュー外へ移ったときに、詳細画面と案内を閉じます。
    /// </summary>
    /// <param name="camera">ゲーム内のHMD視点として使用するカメラ。</param>
    public void CloseForGameplay(Camera camera)
    {
        _reopenAfterReset = false;
        _directionGuide.Hide();
        _ghostGuide.Hide();
        if (IsOpen) SetOpen(false, camera);
    }

    /// <summary>
    /// シーン移動時に詳細画面を閉じ、古いシーンの表示・入力状態を解除します。
    /// </summary>
    public void CloseForSceneChange()
    {
        _reopenAfterReset = false;
        _directionGuide.Hide();
        _ghostGuide.Hide();
        if (_root != null) SetOpen(false, null);
    }

    /// <summary>
    /// 前回の校正結果を消し、新しい校正を受け付ける表示へ戻します。
    /// </summary>
    public void ClearCalibrationResult()
    {
        _calibration.Reset();
        _countdownRemaining = -1f;
        _nextTextUpdate = 0f;
    }

    /// <summary>校正データの解除後、準備が整っている場合だけ姿勢選択を表示します。</summary>
    public bool SelectCalibrationPose()
    {
        var selected = _calibration.SelectPose(_readiness.Ready);
        _nextTextUpdate = 0f;
        return selected;
    }

    /// <summary>姿勢選択からのみ計測を始め、設定一覧を畳む状態にします。</summary>
    public bool BeginCalibrationCountdown()
    {
        var started = _calibration.Start(_readiness.Ready);
        if (started) _countdownRemaining = _tPoseMode ? 5f : 3f;
        _nextTextUpdate = 0f;
        return started;
    }

    /// <summary>
    /// 校正の結果と成否を保存し、表示と固定姿勢の扱いを更新します。
    /// </summary>
    /// <param name="result">ユーザーへ表示する校正結果の説明。</param>
    /// <param name="succeeded">校正処理が成功したか。</param>
    /// <param name="warning">結果とともに画面を閉じるまで表示する、校正を停止しない警告。</param>
    public void FinishCalibration(string result, bool succeeded, string? warning = null)
    {
        if (!_calibration.Complete(result, succeeded, warning)) return;
        _countdownRemaining = -1f;
        _nextTextUpdate = 0f;
    }

    /// <summary>
    /// トラッカーとアバターの比較マーカー、姿勢ゴースト、正面ガイドを更新します。
    /// </summary>
    /// <param name="avatar">処理対象のアバター。</param>
    /// <param name="hasPoses">腰と左右の主足入力の姿勢がすべて有効か。</param>
    /// <param name="waist">腰のワールド姿勢。</param>
    /// <param name="left">左足のワールド姿勢。</param>
    /// <param name="right">右足のワールド姿勢。</param>
    /// <param name="hasHands">左右の手の姿勢を取得できているか。</param>
    /// <param name="leftHand">左手のワールド姿勢。</param>
    /// <param name="rightHand">右手のワールド姿勢。</param>
    public void UpdateCalibrationMarkers(AvatarIK? avatar, bool hasPoses,
        Pose waist, Pose left, Pose right, bool hasHands, Pose leftHand, Pose rightHand)
    {
        _directionGuide.Update(IsCalibrationScreen && _calibration.Stage != CalibrationStage.Review,
            hasPoses, left, right, _countdownRemaining, _tPoseMode, _poseAssessment, IsCalibrationScreen);
        _ghostGuide.Update(avatar, ShowCalibrationPose, hasHands, leftHand, rightHand);
        var hasCalibrationData = IsOpen && !_visualScreen &&
            (_calibrationScreen || _roleScreen) && avatar != null &&
            avatar.Animator != null && avatar.Animator.gameObject.activeInHierarchy && hasPoses;
        var visible = hasCalibrationData && _roleScreen;
        for (var i = 0; i < 3; i++)
        {
            _trackerMarkers[i]?.SetActive(visible);
            _avatarMarkers[i]?.SetActive(visible);
        }
        if (!hasCalibrationData)
        {
            _waistHeightStatus = "";
            return;
        }
        avatar!.GetTrackingPoints(out var avatarWaist, out var avatarLeft, out var avatarRight);
        var waistAboveFeet = waist.Position.y - (left.Position.y + right.Position.y) * 0.5f;
        _waistHeightStatus = waistAboveFeet < 0.35f
            ? $"Waist is only {waistAboveFeet:0.00} m above feet: check tracker choice"
            : $"Waist is {waistAboveFeet:0.00} m above feet";
        if (!visible) return;
        _trackerMarkers[0]!.transform.position = waist.Position;
        _trackerMarkers[1]!.transform.position = left.Position;
        _trackerMarkers[2]!.transform.position = right.Position;
        _avatarMarkers[0]!.transform.position = avatarWaist;
        _avatarMarkers[1]!.transform.position = avatarLeft;
        _avatarMarkers[2]!.transform.position = avatarRight;
    }

    /// <summary>
    /// 表示状態と入力の押下エッジを処理し、ボタン操作や確定した割り当てをMODへ通知します。
    /// </summary>
    /// <param name="camera">ゲーム内のHMD視点として使用するカメラ。</param>
    /// <param name="tracking">デバイス一覧・割り当て・追跡情報を提供する管理オブジェクト。</param>
    /// <param name="avatarReady">駆動可能なアバターが現在見つかっているか。</param>
    /// <param name="trackingReady">校正に必要な腰と左右足の姿勢が取得できているか。</param>
    /// <param name="enabled">対象機能を有効にするか。</param>
    /// <param name="cameraGuideEnabled">HMD内のカメラガイドを表示する設定。</param>
    /// <param name="captureCameraFound">ガイド対象の配信用カメラを検出できたか。</param>
    /// <param name="cameraGuideUnavailable">カメラガイドの表示でエラーが発生しているか。</param>
    /// <param name="shadowsOff">アバターの影の投射と受け取りを無効にするか。</param>
    /// <param name="lightsOff">アバター配下のライトを無効にするか。</param>
    /// <param name="avatarHmdAvailable">アバターのHMD表示設定を現在操作できるか。</param>
    /// <param name="avatarVisibleInHmd">アバターをHMD内に表示する現在の設定。</param>
    /// <param name="menuOpen">ゲームのアバター選択メニューが開いているか。</param>
    /// <param name="trigger">いずれかの手のトリガーが押されているか。</param>
    /// <param name="leftPointer">左手のポインター姿勢と同じ個体のトリガー状態。</param>
    /// <param name="rightPointer">右手のポインター姿勢と同じ個体のトリガー状態。</param>
    /// <param name="countdownRemaining">残り秒数。負数はカウントダウンしていない状態です。</param>
    /// <param name="tPose">現在の校正姿勢の判定結果。</param>
    /// <returns>このフレームで要求された操作。該当しなければNone。</returns>
    public PanelCommand Tick(Camera camera, Tracking tracking, bool avatarReady,
        bool trackingReady, bool enabled, bool cameraGuideEnabled,
        bool captureCameraFound, bool cameraGuideUnavailable,
        bool shadowsOff, bool lightsOff, bool avatarHmdAvailable,
        bool avatarVisibleInHmd,
        bool menuOpen, bool trigger,
        ControllerPointer leftPointer, ControllerPointer rightPointer,
        float countdownRemaining, TPoseAssessment tPose)
    {
        _countdownRemaining = countdownRemaining;
        _poseAssessment = tPose;
        var press = _input.Update(trigger, leftPointer.Trigger, rightPointer.Trigger,
            leftPointer.HasPose, rightPointer.HasPose);
        if (!menuOpen) { CloseForGameplay(camera); return PanelCommand.None; }
        EnsureCreated();
        if (_reopenAfterReset)
        {
            SetOpen(true, camera);
            _reopenAfterReset = false;
            press = default;
        }
        if (!IsOpen) return PanelCommand.None;
        if (_calibrationScreen) _readiness = tracking.GetCalibrationReadiness(avatarReady, trackingReady);
        var showMount = _roleScreen && _selectedRole is TrackerRole.LeftFoot or TrackerRole.RightFoot or TrackerRole.LeftToes or TrackerRole.RightToes;
        _mountGuide?.Show(showMount, _selectedRole);
        _dashboard?.SetActive(!_calibrationScreen && !showMount);
        if (!_calibrationScreen) _stageView?.Hide();
        if (Time.unscaledTime >= _nextTextUpdate)
        {
            _nextTextUpdate = Time.unscaledTime + 0.15f;
            UpdateText(tracking, avatarReady, trackingReady, enabled,
                cameraGuideEnabled, captureCameraFound, cameraGuideUnavailable,
                shadowsOff, lightsOff, avatarHmdAvailable, avatarVisibleInHmd,
                countdownRemaining, tPose);
        }
        UpdateControls(tracking, avatarReady, trackingReady, enabled, cameraGuideEnabled,
            shadowsOff, lightsOff, avatarHmdAvailable, avatarVisibleInHmd);
        var gazePoint = Vector3.zero;
        var gazeHit = !leftPointer.HasPose && !rightPointer.HasPose && TryGetGazePoint(camera, out gazePoint);
        var leftPoint = Vector3.zero;
        var rightPoint = Vector3.zero;
        var leftHit = leftPointer.HasPose && TryGetPointerPoint(leftPointer.Pose, out leftPoint);
        var rightHit = rightPointer.HasPose && TryGetPointerPoint(rightPointer.Pose, out rightPoint);
        UpdateCursor(_leftCursor, leftHit, leftPoint);
        UpdateCursor(_rightCursor, rightHit, rightPoint);
        var focused = -1;
        var activation = new PanelActivation(-1, UiHand.None);
        // A press from a hand outside the panel never activates the other hand's hover.
        if (press.Any)
        {
            activation = PanelControls.Activation(press, leftHit ? HitControl(leftPoint) : -1,
                rightHit ? HitControl(rightPoint) : -1, gazeHit ? HitControl(gazePoint) : -1);
            focused = activation.Target;
        }
        else if (rightHit && (rightPointer.Trigger || !trigger)) focused = HitControl(rightPoint);
        else if (leftHit && (leftPointer.Trigger || !trigger)) focused = HitControl(leftPoint);
        else if (gazeHit) focused = HitControl(gazePoint);
        for (var i = 0; i < _views.Length; i++) _views[i]?.Render(_controls[i], i == focused);
        if (!press.Any || focused < 0) return PanelCommand.None;
        _views[focused]?.Pulse();
        PlayFeedback(tracking, activation.Hand);
        _input.Block();
        _nextTextUpdate = 0f;
        if (focused == PanelControls.Close)
        {
            SetOpen(false, camera);
            return PanelCommand.Close;
        }
        if (focused == PanelControls.Back) return GoBack();
        if (focused == PanelControls.DashboardDetails)
        {
            var firstDevice = _dashboardPage * (_uiConfig.UiTrackerDetails ? 1 : 3);
            _uiConfig.UiTrackerDetails = !_uiConfig.UiTrackerDetails;
            _dashboardPage = firstDevice / (_uiConfig.UiTrackerDetails ? 1 : 3);
            return PanelCommand.UiPreferencesChanged;
        }
        if (_appearanceScreen && (focused < PanelControls.RowCount || focused is >= PanelControls.TextStandard and <= PanelControls.Farther))
            return HandleAppearance(focused);
        if (focused is >= PanelControls.Trackers and <= PanelControls.Details)
        {
            var cancel = ShowCalibrationPose;
            Navigate(focused switch
            {
                PanelControls.Trackers => PanelPage.Trackers,
                PanelControls.Display => PanelPage.Display,
                PanelControls.Expressions => PanelPage.Expressions,
                _ => PanelPage.Details
            });
            return cancel ? PanelCommand.CancelCalibration : PanelCommand.None;
        }
        if (focused is PanelControls.DashboardPrevious or PanelControls.DashboardNext)
        {
            if (_calibrationScreen) _stageView?.ChangePage(focused == PanelControls.DashboardNext ? 1 : -1);
            else _dashboardPage = Math.Clamp(_dashboardPage + (focused == PanelControls.DashboardNext ? 1 : -1), 0, _dashboardPages - 1);
            return PanelCommand.None;
        }
        if (focused is PanelControls.PagePrevious or PanelControls.PageNext)
        {
            ChangePage(focused == PanelControls.PageNext ? 1 : -1);
            return PanelCommand.None;
        }
        if (focused is PanelControls.TPose or PanelControls.Standing)
            return (focused == PanelControls.TPose) != _tPoseMode ? PanelCommand.TogglePoseMode : PanelCommand.None;
        return HandleRow(focused, tracking, camera);
    }

    /// <summary>有効と判定済みの操作行だけを実行します。情報表示はこの経路に入りません。</summary>
    private PanelCommand HandleRow(int row, Tracking tracking, Camera camera)
    {
        if (_expressionScreen) return HandleExpressionRow(row);
        if (_visualScreen && row == 4)
        {
            _appearanceScreen = true;
            return PanelCommand.None;
        }
        if (_visualScreen)
            return row switch
            {
                0 => PanelCommand.ToggleCameraGuide,
                1 => PanelCommand.ToggleAvatarShadows,
                2 => PanelCommand.ToggleAvatarLights,
                3 => PanelCommand.ToggleAvatarInHeadset,
                _ => PanelCommand.None
            };
        if (_roleScreen) return HandleAssignmentRow(row, tracking, camera);
        if (_ultimateBefore is { } before)
        {
            if (row == 5) { DiscardSelection(); return PanelCommand.None; }
            if (row == 3 && tracking.UltimateCorrectionEnabled == before)
            {
                DiscardSelection();
                return PanelCommand.ToggleUltimateRotation;
            }
            return PanelCommand.None;
        }
        if (_calibrationScreen)
        {
            if (row == 0)
            {
                if (_calibration.Stage == CalibrationStage.Preparation) return PanelCommand.PrepareCalibration;
                if (_calibration.Stage == CalibrationStage.Pose) return PanelCommand.StartCalibration;
                if (_calibration.CountingDown)
                {
                    _calibration.Cancel();
                    _countdownRemaining = -1f;
                    return PanelCommand.CancelCalibration;
                }
                if (_calibration.Succeeded)
                {
                    SetOpen(false, camera);
                    return PanelCommand.Close;
                }
                ClearCalibrationResult();
            }
            else if (row == 1 && _calibration.Stage == CalibrationStage.Review && _calibration.Succeeded)
                ClearCalibrationResult();
            else if (row == 5 || row == 1 && _calibration.Stage == CalibrationStage.Review)
            {
                var cancel = ShowCalibrationPose;
                Navigate(PanelPage.Trackers);
                return cancel ? PanelCommand.CancelCalibration : PanelCommand.None;
            }
            return PanelCommand.None;
        }
        if (row == 0) return PanelCommand.Recalibrate;
        if (row == 1) Navigate(PanelPage.Trackers);
        if (row == 2) return PanelCommand.ToggleTracking;
        if (row == 3) Navigate(PanelPage.Display);
        if (row == 4) Navigate(PanelPage.Expressions);
        if (row == 5) _ultimateBefore = tracking.UltimateCorrectionEnabled;
        return PanelCommand.None;
    }

    /// <summary>未確定の個体と下位画面の選択を破棄します。保存済みの割り当ては変更しません。</summary>
    private void DiscardSelection()
    {
        _appearanceScreen = false;
        _assignmentDraft = _automaticDraft = null;
        _confirmingAutomatic = false;
        _ultimateBefore = null;
        _deviceChoices.Clear();
        _selectedRole = null;
        _expressionButton = null;
        Array.Clear(_visibleDevices);
        Array.Clear(_visibleExpressions);
    }

    /// <summary>タブへ移動し、直前の押下と未確定の選択を引き継がないようにします。</summary>
    private void Navigate(PanelPage page)
    {
        if (ShowCalibrationPose) { _calibration.Cancel(); _countdownRemaining = -1f; }
        DiscardSelection();
        _navigation.SelectTab(page);
        _nextTextUpdate = 0f;
        _input.Block();
    }

    /// <summary>個体・表情選択では一覧へ、それ以外では上位画面へ戻ります。</summary>
    private PanelCommand GoBack()
    {
        _nextTextUpdate = 0f;
        _input.Block();
        if (IsCalibrationScreen && _calibration.Stage != CalibrationStage.Preparation)
        {
            var cancel = _calibration.Preview;
            _calibration.Cancel();
            _countdownRemaining = -1f;
            return cancel ? PanelCommand.CancelCalibration : PanelCommand.None;
        }
        var nested = _appearanceScreen || _selectedRole != null || _expressionButton != null || _assignmentDraft != null || _automaticDraft != null || _ultimateBefore != null;
        DiscardSelection();
        if (!nested) _navigation.Back();
        return PanelCommand.None;
    }

    /// <summary>画面の現在状態から、描画と入力で共有する操作種別と有効状態を構築します。</summary>
    private void UpdateControls(Tracking tracking, bool avatarReady, bool trackingReady,
        bool enabled, bool cameraGuide, bool shadowsOff, bool lightsOff, bool hmdAvailable, bool hmdVisible)
    {
        Array.Clear(_controls);
        _controls[PanelControls.Back] = new(PanelControlKind.Navigation, _navigation.CanGoBack || _ultimateBefore != null);
        _controls[PanelControls.Close] = new(PanelControlKind.Navigation);
        _controls[PanelControls.Trackers] = new(PanelControlKind.Choice, true, _roleScreen);
        _controls[PanelControls.Display] = new(PanelControlKind.Choice, true, _visualScreen);
        _controls[PanelControls.Expressions] = new(PanelControlKind.Choice, true, _expressionScreen);
        _controls[PanelControls.Details] = new(PanelControlKind.Choice, true, _navigation.Current == PanelPage.Details);
        if (!_calibrationScreen && _dashboard != null && _dashboard.activeSelf)
        {
            _controls[PanelControls.DashboardDetails] = new(PanelControlKind.Toggle, true, _uiConfig.UiTrackerDetails);
            PanelControls.Paging(_controls, PanelControls.DashboardPrevious, PanelControls.DashboardNext, _dashboardPage, _dashboardPages);
        }
        if (_appearanceScreen) UpdateAppearanceControls();
        else if (_visualScreen)
        {
            _controls[0] = new(PanelControlKind.Toggle, true, cameraGuide);
            _controls[1] = new(PanelControlKind.Toggle, true, !shadowsOff);
            _controls[2] = new(PanelControlKind.Toggle, true, !lightsOff);
            _controls[3] = new(PanelControlKind.Toggle, hmdAvailable, hmdVisible);
            _controls[4] = new(PanelControlKind.Navigation);
            _controls[5] = new(PanelControlKind.Information);
        }
        else if (_roleScreen) UpdateAssignmentControls(tracking);
        else if (_ultimateBefore is { } before)
        {
            for (var i = 0; i < 3; i++) _controls[i] = new(PanelControlKind.Information);
            _controls[3] = new(PanelControlKind.Primary, tracking.UltimateCorrectionEnabled == before);
            _controls[5] = new(PanelControlKind.Navigation);
        }
        else if (_expressionScreen)
        {
            if (_expressionConfig == null || _expressions == null) return;
            _controls[0] = new(PanelControlKind.Toggle, true, _expressionButton is { } b
                ? GetExpressionBinding(b).Toggle : _expressionConfig.ExpressionsEnabled);
            for (var i = 1; i <= 3; i++)
                _controls[i] = _expressionButton is { } button
                    ? new(_visibleExpressions[i - 1] == null ? PanelControlKind.Hidden : PanelControlKind.Choice,
                        true, _visibleExpressions[i - 1] == GetExpressionBinding(button).Expression)
                    : new(PanelControlKind.Navigation);
            _controls[5] = new(PanelControlKind.Information);
            if (_expressionButton != null) SetPaging(_expressionPage, _expressionPages);
            else _controls[4] = new(PanelControlKind.Navigation);
        }
        else if (_calibrationScreen)
        {
            _calibration.ApplyControls(_controls, _readiness.Ready, _tPoseMode);
            if (_calibration.Stage == CalibrationStage.Review && _stageView is { MessagePages: > 1 } view)
            {
                _controls[PanelControls.DashboardPrevious] = new(PanelControlKind.Navigation, view.MessagePage > 1);
                _controls[PanelControls.DashboardNext] = new(PanelControlKind.Navigation, view.MessagePage < view.MessagePages);
                _views[PanelControls.DashboardNext]!.Label.text = UiText.Get("Next page");
            }
        }
        else
        {
            _controls[0] = new(PanelControlKind.Primary);
            _controls[1] = _controls[3] = _controls[4] = new(PanelControlKind.Navigation);
            _controls[2] = new(PanelControlKind.Toggle, true, enabled);
            _controls[5] = new(PanelControlKind.Navigation);
        }
    }

    /// <summary>
    /// 設定Canvas・行ボタン・カーソル・比較マーカーを未作成時に構築します。
    /// </summary>
    private void EnsureCreated()
    {
        if (_root != null) return;
        _root = VrUi.CreateCanvas("SynthRidersFullBody.SettingsPanel", new Vector2(1600f, 800f), VrPanelLayout.PanelScale);
        VrUi.Image(_root, "Background", new Vector2(1600f, 800f), Vector2.zero,
            PanelFill);
        VrUi.HexOutline(_root, "NeonPanelFrame", new Vector2(1570f, 770f),
            Vector2.zero, new Color(0.46f, 0.20f, 0.69f, 0.28f), 2f, 95f);
        VrUi.Image(_root, "Divider", new Vector2(3f, 650f), new Vector2(105f, -20f),
            new Color(0.24f, 0.20f, 0.49f, 0.75f));
        _title = VrUi.Text(_root, "Title", new Vector2(900f, 60f), new Vector2(0f, 350f),
            34f, Color.white, TextAlignmentOptions.Center);
        _title.enableWordWrapping = true;
        _status = VrUi.Text(_root, "Status", new Vector2(820f, 46f), new Vector2(-340f, 215f),
            25f, Color.white, TextAlignmentOptions.Center);
        _status.enableWordWrapping = true;
        for (var i = 0; i < _views.Length; i++)
        {
            _views[i] = new PanelControlView(_root, i, i is >= PanelControls.Back and <= PanelControls.Details ? 25f : 30f);
            if (i < _rows.Length) _rows[i] = _views[i]!.Label;
        }
        UiLocalization.Bind(_views[PanelControls.Back]!.Label, "Back");
        UiLocalization.Bind(_views[PanelControls.Close]!.Label, "Close panel");
        UiLocalization.Bind(_views[PanelControls.Trackers]!.Label, "Trackers");
        UiLocalization.Bind(_views[PanelControls.Display]!.Label, "Display");
        UiLocalization.Bind(_views[PanelControls.Expressions]!.Label, "Expressions");
        UiLocalization.Bind(_views[PanelControls.Details]!.Label, "Details");
        _dashboardNextText = _views[PanelControls.DashboardNext]!.Label;
        UiLocalization.Bind(_views[PanelControls.DashboardPrevious]!.Label, "Previous page");
        UiLocalization.Bind(_views[PanelControls.DashboardDetails]!.Label, "Show details");
        _footer = VrUi.Text(_root, "Help", new Vector2(820f, 100f), new Vector2(-340f, -325f),
            22f, new Color(0.7f, 0.8f, 0.9f), TextAlignmentOptions.Center);
        _footer.enableWordWrapping = true;
        CreateDashboard();
        _stageView = new CalibrationStageView(_root);
        _mountGuide = new TrackerMountGuide(_root);
        _leftCursor = VrUi.Image(_root, "LeftControllerCursor", new Vector2(19f, 19f),
            Vector2.zero, Cyan);
        _rightCursor = VrUi.Image(_root, "RightControllerCursor", new Vector2(19f, 19f),
            Vector2.zero, Pink);
        _leftCursor.gameObject.SetActive(false);
        _rightCursor.gameObject.SetActive(false);
        for (var i = 0; i < 3; i++)
        {
            _trackerMarkers[i] = CreateMarker($"Tracker{i}", new Color(0.1f, 0.9f, 1f));
            _avatarMarkers[i] = CreateMarker($"Avatar{i}", new Color(1f, 0.75f, 0.1f));
        }
        _root.SetActive(false);
    }

    /// <summary>
    /// LIVE TRACKERSの情報カードを作成します。操作部品は共通定義で作成済みです。
    /// </summary>
    private void CreateDashboard()
    {
        _dashboard = new GameObject("LiveTrackerDashboard");
        _dashboard.transform.SetParent(_root!.transform, false);
        var rect = _dashboard.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(690f, 800f);
        rect.anchoredPosition = new Vector2(445f, 0f);
        UiLocalization.Bind(VrUi.Text(_dashboard, "DashboardTitle", new Vector2(300f, 62f),
            new Vector2(-165f, 270f), 27f, Color.white,
            TextAlignmentOptions.Center), "LIVE TRACKERS");
        _dashboardStatus = VrUi.Text(_dashboard, "DashboardStatus", new Vector2(620f, 50f),
            new Vector2(0f, 207f), 22f, Color.white, TextAlignmentOptions.Center);
        for (var i = 0; i < _dashboardRows.Length; i++)
        {
            var y = 100f - i * 150f;
            _dashboardCards[i] = VrUi.Image(_dashboard, $"TrackerCard{i}",
                new Vector2(630f, 142f), new Vector2(0f, y),
                new Color(0.07f, 0.07f, 0.18f, 0.97f));
            _dashboardRows[i] = VrUi.Text(_dashboard, $"TrackerCardText{i}",
                new Vector2(600f, 134f), new Vector2(0f, y),
                22f, new Color(0.52f, 0.91f, 0.98f), TextAlignmentOptions.MidlineLeft);
        }
    }

    /// <summary>
    /// 比較表示用の色付き球を作り、物理衝突と影を無効にします。
    /// </summary>
    /// <param name="name">作成するオブジェクトまたはUI部品の名前。</param>
    /// <param name="color">表示部品に設定する色とアルファ値。</param>
    /// <returns>初期状態で非表示のマーカー。</returns>
    private GameObject CreateMarker(string name, Color color)
    {
        var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.SetActive(false);
        marker.name = $"SynthRidersFullBody.{name}";
        marker.layer = 2;
        marker.transform.localScale = Vector3.one * 0.065f;
        var renderer = marker.GetComponent<Renderer>();
        if (renderer != null)
        {
            var material = renderer.material;
            _markerMaterials.Add(material);
            material.color = color;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        var collider = marker.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            UnityEngine.Object.Destroy(collider);
        }
        UnityEngine.Object.DontDestroyOnLoad(marker);
        return marker;
    }

    /// <summary>
    /// 現在の画面と追跡状態に合わせて、見出し・行・案内の文字列を更新します。
    /// </summary>
    /// <param name="tracking">デバイス一覧・割り当て・追跡情報を提供する管理オブジェクト。</param>
    /// <param name="avatarReady">駆動可能なアバターが現在見つかっているか。</param>
    /// <param name="trackingReady">校正に必要な腰と左右足の姿勢が取得できているか。</param>
    /// <param name="enabled">対象機能を有効にするか。</param>
    /// <param name="cameraGuideEnabled">HMD内のカメラガイドを表示する設定。</param>
    /// <param name="captureCameraFound">ガイド対象の配信用カメラを検出できたか。</param>
    /// <param name="cameraGuideUnavailable">カメラガイドの表示でエラーが発生しているか。</param>
    /// <param name="shadowsOff">アバターの影の投射と受け取りを無効にするか。</param>
    /// <param name="lightsOff">アバター配下のライトを無効にするか。</param>
    /// <param name="avatarHmdAvailable">アバターのHMD表示設定を現在操作できるか。</param>
    /// <param name="avatarVisibleInHmd">アバターをHMD内に表示する現在の設定。</param>
    /// <param name="countdownRemaining">残り秒数。負数はカウントダウンしていない状態です。</param>
    /// <param name="tPose">現在の校正姿勢の判定結果。</param>
    private void UpdateText(Tracking tracking, bool avatarReady, bool trackingReady,
        bool enabled, bool cameraGuideEnabled, bool captureCameraFound,
        bool cameraGuideUnavailable, bool shadowsOff, bool lightsOff,
        bool avatarHmdAvailable, bool avatarVisibleInHmd,
        float countdownRemaining, TPoseAssessment tPose)
    {
        foreach (var row in _rows) if (row != null) row.text = "";
        if (!_calibrationScreen) UpdateDashboardText(tracking);
        if (_appearanceScreen) { UpdateAppearanceText(); return; }
        if (_visualScreen)
        {
            UpdateVisualText(cameraGuideEnabled, captureCameraFound,
                cameraGuideUnavailable, shadowsOff, lightsOff,
                avatarHmdAvailable, avatarVisibleInHmd);
            return;
        }
        for (var i = 0; i < _rows.Length; i++) VrUi.SetFontSize(_rows[i]!, 33f);
        if (_expressionScreen) { UpdateExpressionText(); return; }
        if (_roleScreen) { UpdateRoleText(tracking); return; }
        if (_ultimateBefore != null) { UpdateUltimateText(); return; }
        _title!.text = UiText.Get(_calibrationScreen
            ? (_tPoseMode ? "T-POSE CALIBRATION" : "STANDING CALIBRATION")
            : "FULL BODY SETTINGS");
        _status!.text = _calibrationScreen
            ? UiText.Format("FRONT = {0}  |  Trackers: {1}  |  Avatar: {2}", UiText.Get(_directionGuide.Source), tracking.TrackerCount, UiText.Get(avatarReady ? "OK" : "missing"))
            : UiText.Format("{0}: {1}  |  HMD: {2}  |  Avatar: {3}", tracking.SourceLabel, tracking.TrackerCount, UiText.Get(tracking.HmdPresent ? "OK" : "missing"), UiText.Get(avatarReady ? "OK" : "missing"));
        if (_calibrationScreen) UpdateCalibrationText(tracking, tPose);
        else
        {
            _rows[0]!.text = UiText.Get(_recalibrationRequired ? "Go to calibration" : "CALIBRATE FULL BODY  >").TrimEnd(' ', '>');
            _rows[1]!.text = UiText.Get("TRACKER ROLES  >").TrimEnd(' ', '>');
            _rows[2]!.text = UiText.Format("Tracking: {0}", UiText.OnOff(enabled));
            _rows[3]!.text = UiText.Get("VISUAL OPTIONS  >").TrimEnd(' ', '>');
            _rows[4]!.text = UiText.Get("EXPRESSIONS  >").TrimEnd(' ', '>');
            _rows[5]!.text = UiText.Format("Ultimate 180 correction: {0}", UiText.OnOff(tracking.UltimateCorrectionEnabled));
            VrUi.SetFontSize(_rows[5]!, 30f);
            _footer!.text = UiText.Get(_recalibrationRequired ? "Recalibration required." :
                "Use only when forward AND sideways motion are reversed.");
        }
    }

    /// <summary>手順ごとに必要な操作・準備状態・案内を表示します。計測中の設定一覧は畳みます。</summary>
    private void UpdateCalibrationText(Tracking tracking, TPoseAssessment assessment)
    {
        var stage = _calibration.Stage;
        _stageView?.Update(_calibration, _tPoseMode, _countdownRemaining, assessment.Warning);
        foreach (var row in _rows) if (row != null) VrUi.SetFontSize(row, 28f);
        var stageName = stage switch
        {
            CalibrationStage.Preparation => "Preparation",
            CalibrationStage.Pose => "Choose a pose",
            CalibrationStage.Countdown => "Countdown",
            _ => "Check movement"
        };
        _title!.text = UiText.Format("Step {0} / 4: {1}", (int)stage + 1, UiText.Get(stageName));
        _status!.text = UiText.Get(_directionGuide.UsesMirror ? "FRONT = MIRROR" : "FRONT = THIS ARROW");
        _rows[5]!.text = UiText.Get("Check tracker assignments");
        if (stage == CalibrationStage.Preparation)
        {
            _rows[0]!.text = UiText.Get("Continue: choose pose");
            _rows[1]!.text = UiText.Format("Avatar: {0}", UiText.Get(_readiness.AvatarReady ? "Ready" : "Load an avatar in the game menu"));
            _rows[2]!.text = ReadinessLabel("Waist", _readiness.Waist);
            _rows[3]!.text = ReadinessLabel("Left foot", _readiness.Left) + FootRoleLabel(tracking, true);
            _rows[4]!.text = ReadinessLabel("Right foot", _readiness.Right) + FootRoleLabel(tracking, false);
            _footer!.text = UiText.Get(_calibration.Canceled ? "Calibration canceled. Choose a pose to try again." :
                _readiness.Ready ? "Your assignments are ready. Continuing starts a new calibration." :
                "Load an avatar and check missing devices in Trackers.");
            if (_readiness.AdditionalIssue != null) _footer.text += "\n" + UiText.Status(_readiness.AdditionalIssue);
        }
        else if (stage == CalibrationStage.Pose)
        {
            _rows[0]!.text = UiText.Get(_tPoseMode ? "START T-POSE CALIBRATION" : "CALIBRATE STANDING");
            _views[PanelControls.TPose]!.Label.text = (_tPoseMode ? UiText.Get("[Selected] ") : "") + UiText.Get("T-pose");
            _views[PanelControls.Standing]!.Label.text = (!_tPoseMode ? UiText.Get("[Selected] ") : "") + UiText.Get("standing");
            _rows[2]!.text = UiText.Get("Face the FRONT arrow / mirror before checking feet");
            _rows[3]!.text = UiText.Get(_tPoseMode ? "After Start, spread your arms at shoulder height. You have 5 seconds."
                : "Relax your arms and stand with feet apart. You have 3 seconds.");
            _rows[4]!.text = _readiness.Ready ? UiText.Get("The avatar holds the selected pose until calibration finishes.")
                : !_readiness.AvatarReady ? UiText.Get("Load an avatar in the game menu")
                : _readiness.AdditionalIssue != null ? UiText.Status(_readiness.AdditionalIssue)
                : UiText.Get("Waist / feet unavailable: open Trackers to check assignments and tracking");
            _footer!.text = _readiness.AdditionalIssue != null ? UiText.Status(_readiness.AdditionalIssue) :
                assessment.Warning != null ? UiText.Format("WARNING: {0}", UiText.Status(assessment.Warning)) :
                UiText.Get("Look at the eye-level target. Back cancels the pose preview.");
        }
        else if (stage == CalibrationStage.Countdown)
        {
            _rows[0]!.text = UiText.Get("Cancel calibration");
            _rows[1]!.text = UiText.Get("LOOK AT THE DOT");
            _rows[2]!.text = UiText.Get(_tPoseMode ? "HOLD T-POSE" : "HOLD STILL");
            _footer!.text = assessment.Ready ? UiText.Get("Back or Close cancels the countdown") : UiText.Status(assessment.Status);
        }
        else
        {
            _rows[0]!.text = UiText.Get(_calibration.Succeeded ? "Done" : "Try again");
            _rows[1]!.text = UiText.Get(_calibration.Succeeded ? "Try again" : "Check tracker assignments");
            _rows[2]!.text = UiText.Get(_calibration.Succeeded ? "Calibration complete" : "Calibration did not finish");
            _rows[3]!.text = UiText.Get(_calibration.Succeeded ? "Check waist and foot movement in the mirror." : "See the reason on the right.");
            _footer!.text = UiText.Get(_calibration.Succeeded ? "Done closes this panel. It does not automatically verify pose accuracy."
                : "The pose preview has been released. Retry when you are ready.");
        }
    }

    /// <summary>準備OK・未設定・喪失・重複を個々の部位名とともに表示します。</summary>
    private static string ReadinessLabel(string role, CalibrationDeviceState state) =>
        UiText.Format("{0}: {1}", UiText.Get(role), UiText.Get(state switch
        {
            CalibrationDeviceState.Ready => "Ready",
            CalibrationDeviceState.Unassigned => "Unassigned",
            CalibrationDeviceState.Conflict => "Assignment conflict",
            _ => "Tracking lost"
        }));

    /// <summary>足首・足先のどちらを主入力として確認しているか、設定済みの場合だけ補足します。</summary>
    private static string FootRoleLabel(Tracking tracking, bool left)
    {
        var role = tracking.PrimaryFoot(left);
        return tracking.HasAssignment(role) ? " (" + UiText.Get(TrackerRoles.Label(role)) + ")" : "";
    }

    /// <summary>選択肢として未割り当て・解除・現在のVRMが持つ表情を列挙します。</summary>
    private List<ExpressionChoice> ExpressionOptions()
    {
        var choices = new List<ExpressionChoice> { new("", UiText.Get("Unassigned")), new("$reset", UiText.Get("Reset expression")) };
        if (_expressions != null) choices.AddRange(_expressions.Choices);
        return choices;
    }

    /// <summary>保存したIDに対応する表示名を返し、現在のモデルに存在しない表情を明示します。</summary>
    private string ExpressionLabel(string? id) => ExpressionOptions().FirstOrDefault(c => c.Id == id).Label
        ?? UiText.Get(string.IsNullOrEmpty(id) ? "Unassigned" : "Unavailable on this avatar");

    /// <summary>現在編集中のボタンの設定を取得し、未設定時は押下中のみの空設定を返します。</summary>
    private ExpressionBinding GetExpressionBinding(ExpressionButton button) =>
        _expressionConfig?.ExpressionBindings?.GetValueOrDefault(button.ToString()) ?? new ExpressionBinding();

    /// <summary>表情ボタン一覧または表情選択画面を表示します。</summary>
    private void UpdateExpressionText()
    {
        if (_expressionConfig == null || _expressions == null)
        {
            _title!.text = UiText.Get("CONTROLLER EXPRESSIONS");
            _status!.text = UiText.Get("Unavailable");
            _footer!.text = UiText.Get("Load an avatar in the game menu");
            return;
        }
        foreach (var text in _rows) if (text != null) VrUi.SetFontSize(text, 27f);
        _status!.text = UiText.Status(_expressions.Status);
        _title!.text = UiText.Get("CONTROLLER EXPRESSIONS");
        Array.Clear(_visibleExpressions);
        _rows[5]!.text = UiText.Get("These options do not require recalibration.");
        if (_expressionButton is { } button)
        {
            var binding = GetExpressionBinding(button);
            var options = ExpressionOptions();
            var pages = Math.Max(1, (options.Count + 2) / 3);
            _expressionPages = pages;
            _expressionPage = Math.Min(_expressionPage, pages - 1);
            _title.text = UiText.Get(ExpressionControls.Label(button));
            _rows[0]!.text = UiText.Format("Mode: {0}  >", UiText.Get(binding.Toggle ? "Toggle" : "Hold")).TrimEnd(' ', '>');
            for (var i = 0; i < 3; i++)
            {
                var index = _expressionPage * 3 + i;
                if (index >= options.Count) { _rows[i + 1]!.text = ""; continue; }
                var choice = options[index];
                _visibleExpressions[i] = choice.Id;
                _rows[i + 1]!.text = (choice.Id == binding.Expression ? UiText.Get("[Selected] ") : "") + choice.Label;
            }
            _status!.text += " | " + PageLabel(_expressionPage, pages);
            var available = (_expressionInput.Available & (1 << (int)button)) != 0;
            _footer!.text = UiText.Format("Current: {0}\n", ExpressionLabel(binding.Expression)) +
                UiText.Get(!_expressionConfig.ExpressionsEnabled ? "Enable Expressions on the previous page to preview" :
                    available ? "Press the assigned button to preview in the mirror" : "Button unavailable: check controller connection / input support");
        }
        else
        {
            _rows[0]!.text = UiText.Format("Expressions: {0}", UiText.OnOff(_expressionConfig.ExpressionsEnabled));
            for (var i = 0; i < 3; i++)
            {
                var candidate = (ExpressionButton)(_expressionHand * 3 + i);
                var binding = GetExpressionBinding(candidate);
                _rows[i + 1]!.text = $"{UiText.Get(ExpressionControls.Label(candidate))}\n{ExpressionLabel(binding.Expression)}";
                VrUi.SetFontSize(_rows[i + 1]!, 24f);
            }
            _rows[4]!.text = UiText.Format("Show {0} controller  >", UiText.Get(_expressionHand == 0 ? "RIGHT" : "LEFT")).TrimEnd(' ', '>');
            _footer!.text = UiText.Get("Hold = while pressed | Toggle = press again to release\nGame actions on assigned buttons also run");
        }
    }

    /// <summary>表情画面の押下を処理し、変更があれば保存要求を返します。</summary>
    private PanelCommand HandleExpressionRow(int row)
    {
        if (_expressionConfig == null) return PanelCommand.None;
        _expressionConfig.ExpressionBindings ??= new Dictionary<string, ExpressionBinding>();
        _nextTextUpdate = 0f;
        if (_expressionButton is { } button)
        {
            var binding = GetExpressionBinding(button);
            if (row == 0)
                _expressionConfig.ExpressionBindings[button.ToString()] = binding with { Toggle = !binding.Toggle };
            else if (row is >= 1 and <= 3 && _visibleExpressions[row - 1] is { } id)
                _expressionConfig.ExpressionBindings[button.ToString()] = binding with { Expression = id };
            else
            {
                return PanelCommand.None;
            }
            return PanelCommand.ExpressionsChanged;
        }
        if (row == 0)
        {
            _expressionConfig.ExpressionsEnabled = !_expressionConfig.ExpressionsEnabled;
            return PanelCommand.ExpressionsChanged;
        }
        if (row is >= 1 and <= 3)
        {
            _expressionButton = (ExpressionButton)(_expressionHand * 3 + row - 1);
            _expressionPage = 0;
        }
        if (row == 4) _expressionHand = 1 - _expressionHand;
        return PanelCommand.None;
    }

    /// <summary>
    /// 共有の描画設定、HMD表示、カメラの検出・方向情報を表示します。
    /// </summary>
    /// <param name="cameraGuideEnabled">HMD内のカメラガイドを表示する設定。</param>
    /// <param name="captureCameraFound">ガイド対象の配信用カメラを検出できたか。</param>
    /// <param name="cameraGuideUnavailable">カメラガイドの表示でエラーが発生しているか。</param>
    /// <param name="shadowsOff">アバターの影の投射と受け取りを無効にするか。</param>
    /// <param name="lightsOff">アバター配下のライトを無効にするか。</param>
    /// <param name="avatarHmdAvailable">アバターのHMD表示設定を現在操作できるか。</param>
    /// <param name="avatarVisibleInHmd">アバターをHMD内に表示する現在の設定。</param>
    private void UpdateVisualText(bool cameraGuideEnabled, bool captureCameraFound,
        bool cameraGuideUnavailable, bool shadowsOff, bool lightsOff,
        bool avatarHmdAvailable, bool avatarVisibleInHmd)
    {
        _title!.text = UiText.Get("VISUAL OPTIONS");
        _status!.text = UiText.Status(!cameraGuideEnabled ? "Camera guide: OFF" :
            cameraGuideUnavailable ? "Camera guide: unavailable (see log)" : _cameraGuideStatus);
        for (var i = 0; i < _rows.Length; i++) VrUi.SetFontSize(_rows[i]!, 31f);
        _rows[0]!.text = UiText.Format("Camera guide in headset: {0}", UiText.OnOff(cameraGuideEnabled));
        _rows[1]!.text = UiText.Format("VRM cast/receive shadows: {0}", UiText.OnOff(!shadowsOff));
        _rows[2]!.text = UiText.Format("VRM lights: {0}", UiText.OnOff(!lightsOff));
        _rows[3]!.text = avatarHmdAvailable
            ? UiText.Format("VRM in headset: {0}", UiText.OnOff(avatarVisibleInHmd))
            : UiText.Get("VRM in headset: load an avatar");
        _rows[4]!.text = UiText.Get("Panel & feedback");
        _rows[5]!.text = cameraGuideEnabled && captureCameraFound ? UiText.Status(_cameraGuideLocation) : "";
        _footer!.text = UiText.Get("Cyan sphere marks the camera; visual options are shared") + "\n" +
            UiText.Get("These options do not require recalibration.");
    }

    /// <summary>
    /// 現在のデバイス一覧とページ番号に合わせてLIVE TRACKERSを更新します。
    /// </summary>
    /// <param name="tracking">デバイス一覧・割り当て・追跡情報を提供する管理オブジェクト。</param>
    private void UpdateDashboardText(Tracking tracking)
    {
        var trackers = tracking.GetTelemetry();
        var pageSize = _uiConfig.UiTrackerDetails ? 1 : 3;
        _dashboardPages = Math.Max(1, (trackers.Count + pageSize - 1) / pageSize);
        _dashboardPage = Math.Min(_dashboardPage, _dashboardPages - 1);
        _dashboardStatus!.text =
            UiText.Format("{0} devices  |  Page {1}/{2}", trackers.Count, _dashboardPage + 1, _dashboardPages) +
            (_uiConfig.UiTrackerDetails ? "\n" + UiText.Get("Raw coordinates: compare only within the same API") : "");
        for (var i = 0; i < _dashboardRows.Length; i++)
        {
            var index = _dashboardPage * pageSize + i;
            var visible = i < pageSize;
            _dashboardRows[i]!.gameObject.SetActive(visible);
            _dashboardCards[i]!.gameObject.SetActive(visible);
            if (!visible) continue;
            var y = _uiConfig.UiTrackerDetails ? -50f : 100f - i * 150f;
            var height = _uiConfig.UiTrackerDetails ? 460f : 142f;
            _dashboardCards[i]!.rectTransform.sizeDelta = new(630, height);
            _dashboardCards[i]!.rectTransform.anchoredPosition = new(0, y);
            _dashboardRows[i]!.rectTransform.sizeDelta = new(600, height - 8);
            _dashboardRows[i]!.rectTransform.anchoredPosition = new(0, y);
            VrUi.SetFontSize(_dashboardRows[i]!, 27f);
            _dashboardRows[i]!.enableWordWrapping = true;
            _dashboardRows[i]!.text = index < trackers.Count
                ? FormatDashboardLine(tracking, trackers[index])
                : i == 0 ? UiText.Get("No active tracker found") : "";
            _dashboardCards[i]!.color = index < trackers.Count && IsMoving(trackers[index].Device.Key)
                ? new Color(.12f, .35f, .43f, 1f) : new Color(.08f, .10f, .23f, 1f);
        }
        _dashboardNextText!.text = UiText.Get("Next page");
    }

    /// <summary>
    /// デバイスの識別情報と生の追跡座標・速度を表示用の文字列へ整形します。
    /// </summary>
    /// <param name="tracking">デバイス一覧・割り当て・追跡情報を提供する管理オブジェクト。</param>
    /// <param name="tracker">表示する個体の識別情報と追跡計測値。</param>
    /// <returns>個体情報・座標・速度、または追跡喪失の表示文。</returns>
    private string FormatDashboardLine(Tracking tracking, TrackerTelemetry tracker)
    {
        var model = string.IsNullOrWhiteSpace(tracker.Name) ? UiText.Get("(unknown model)") : tracker.Name;
        var serial = string.IsNullOrWhiteSpace(tracker.Serial) ? UiText.Get("(no serial)") : tracker.Serial;
        var key = tracker.Device.Key;
        var state = MotionLabel(tracker);
        var text = $"{tracker.Source} #{tracker.Id}  [{UiText.Get(state)}]\nS/N {serial}\n{model}";
        if (!_uiConfig.UiTrackerDetails || !tracker.HasPose) return text;
        var speed = _lastSamples.TryGetValue(key, out var sample) ? sample.Speed : 0f;
        var position = tracker.Position;
        return text + "\n\n" +
            $"X {position.x:+0.00;-0.00;0.00}  Y {position.y:+0.00;-0.00;0.00}  Z {position.z:+0.00;-0.00;0.00} m\n" +
            $"{speed:0.00} m/s";

    }

    /// <summary>
    /// 同じ個体の前回サンプルと比較し、並進速度と角速度の履歴を更新します。
    /// </summary>
    /// <param name="key">デバイスの動作履歴を区別する識別キー。</param>
    /// <param name="pose">同じ取得元の追跡空間で計測した現在の生姿勢。</param>
    /// <returns>現在または短時間キャッシュされた並進速度。単位はm/s。</returns>
    private float UpdateTrackerSpeed(string key, Pose pose)
    {
        var now = Time.unscaledTime;
        var speed = 0f;
        var angular = 0f;
        if (_lastSamples.TryGetValue(key, out var previous))
        {
            var elapsed = now - previous.Time;
            if (elapsed < 0.05f) return previous.Speed;
            if (elapsed < 1f)
            {
                speed = Vector3.Distance(pose.Position, previous.Pose.Position) / elapsed;
                angular = Quaternion.Angle(pose.Rotation, previous.Pose.Rotation) / elapsed;
            }
        }
        _lastSamples[key] = (pose, now, speed, angular);
        return speed;
    }

    /// <summary>
    /// 直近の並進速度または角速度が動作中の閾値を超えるかを確認します。
    /// </summary>
    /// <param name="key">デバイスの動作履歴を区別する識別キー。</param>
    /// <returns>0.05m/s以上、または15度/s以上ならtrue。</returns>
    private bool IsMoving(string key) => _lastSamples.TryGetValue(key, out var sample) &&
        (sample.Speed >= 0.05f || sample.Angular >= 15f);

    /// <summary>
    /// 個体の追跡状態と動作履歴から、割り当て画面用の状態名を作成します。
    /// </summary>
    /// <param name="tracker">表示する個体の識別情報と追跡計測値。</param>
    /// <returns>LOST、MOVING、TRACKEDのいずれか。</returns>
    private string MotionLabel(TrackerTelemetry tracker)
    {
        if (!tracker.HasPose) { _lastSamples.Remove(tracker.Device.Key); return "LOST"; }
        UpdateTrackerSpeed(tracker.Device.Key, tracker.Pose);
        return IsMoving(tracker.Device.Key) ? "MOVING" : "TRACKED";
    }

    /// <summary>
    /// パネルの開閉を反映し、開くときの固定配置と入力待機、閉じるときの案内解除を行います。
    /// </summary>
    /// <param name="open">詳細パネルを開くか。</param>
    /// <param name="camera">開くときの配置と正面に使うHMDカメラ。閉じる場合はnullでも構いません。</param>
    private void SetOpen(bool open, Camera? camera)
    {
        if (_root == null) return;
        if (open)
        {
            if (camera == null) return;
            _input.Block();
            if (_calibrationScreen) _calibrationForward = _directionGuide.Begin(camera);
            else _directionGuide.Hide();
            _placement ??= DefaultPlacement(camera);
            ApplyAppearance();
            _dashboardPage = 0;
            _lastSamples.Clear();
            _nextTextUpdate = 0f;
        }
        _root.SetActive(open);
        if (open) return;
        _directionGuide.Hide();
        _ghostGuide.Hide();
        _navigation.Open(false);
        _input.Block();
        DiscardSelection();
        _dashboardPage = 0;
        _lastSamples.Clear();
        ClearCalibrationResult();
        _waistHeightStatus = "";
        for (var i = 0; i < 3; i++)
        {
            _trackerMarkers[i]?.SetActive(false);
            _avatarMarkers[i]?.SetActive(false);
        }
    }

    /// <summary>
    /// カーソルの表示状態とパネル内の位置を更新します。
    /// </summary>
    /// <param name="cursor">更新するポインター表示。未作成の場合はnull。</param>
    /// <param name="visible">対象表示を有効にするか。</param>
    /// <param name="point">Canvasローカル座標で表した交点。XとYはUIピクセルです。</param>
    private static void UpdateCursor(Image? cursor, bool visible, Vector3 point)
    {
        if (cursor == null) return;
        cursor.gameObject.SetActive(visible);
        if (visible) cursor.rectTransform.anchoredPosition = new Vector2(point.x, point.y);
    }

    /// <summary>描画と共通の矩形・有効状態から操作対象を取得します。</summary>
    private int HitControl(Vector3 point) => PanelControls.Hit(_controls, point.x, point.y);

    /// <summary>
    /// コントローラー前方のレイをパネルへ投影し、操作可能な交点を取得します。
    /// </summary>
    /// <param name="controller">ポインターの始点と前方向を表すワールド姿勢。</param>
    /// <param name="point">成功時に、Canvasローカル座標の交点を受け取ります。XとYの単位はUIピクセルです。</param>
    /// <returns>レイが距離・範囲条件を満たしてパネルに当たればtrue。</returns>
    private bool TryGetPointerPoint(Pose controller, out Vector3 point)
    {
        var ray = new Ray(controller.Position, controller.Rotation * Vector3.forward);
        if (!TryGetPanelPoint(ray, out point)) return false;
        return Mathf.Abs(point.x) <= 800f && Mathf.Abs(point.y) <= 400f;
    }

    /// <summary>
    /// HMDの視線レイをパネルへ投影し、コントローラーがないときの交点を取得します。
    /// </summary>
    /// <param name="camera">ゲーム内のHMD視点として使用するカメラ。</param>
    /// <param name="point">成功時に、Canvasローカル座標の交点を受け取ります。XとYの単位はUIピクセルです。</param>
    /// <returns>有効な視線交点が得られた場合はtrue。</returns>
    private bool TryGetGazePoint(Camera camera, out Vector3 point) =>
        TryGetPanelPoint(new Ray(camera.transform.position, camera.transform.forward),
            out point);

    /// <summary>
    /// ワールド空間のレイとパネル面の交点を、Canvasのローカル座標へ変換します。
    /// </summary>
    /// <param name="ray">ワールド空間の入力レイ。</param>
    /// <param name="point">成功時に、Canvasローカル座標の交点を受け取ります。XとYの単位はUIピクセルです。</param>
    /// <returns>前方かつ操作距離内に交点がある場合はtrue。</returns>
    private bool TryGetPanelPoint(Ray ray, out Vector3 point)
    {
        point = default;
        if (_root == null) return false;
        var plane = new Plane(_root.transform.forward, _root.transform.position);
        if (!plane.Raycast(ray, out var distance) || distance > VrUi.InteractionDistance) return false;
        point = _root.transform.InverseTransformPoint(ray.GetPoint(distance));
        return true;
    }

    /// <summary>
    /// 入口ボタンへ向かうレイが、手前の開いた詳細パネルで遮られるかを判定します。
    /// </summary>
    /// <param name="ray">ワールド空間の入力レイ。</param>
    /// <param name="buttonDistance">レイの始点から入口ボタンまでの距離。単位はmです。</param>
    /// <returns>ボタンより手前のパネル表示領域に当たればtrue。</returns>
    public bool OccludesRay(Ray ray, float buttonDistance)
    {
        if (!IsOpen) return false;
        var plane = new Plane(_root!.transform.forward, _root.transform.position);
        if (!plane.Raycast(ray, out var distance) || distance > buttonDistance) return false;
        var point = _root.transform.InverseTransformPoint(ray.GetPoint(distance));
        return Mathf.Abs(point.x) <= 800f && Mathf.Abs(point.y) <= 400f;
    }
}
