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
    ToggleUltimateRotation
}

/// <summary>
/// 固定位置の全身設定・校正・部位割り当て・描画設定とLIVE TRACKERSのUIを管理します。
/// </summary>
internal sealed class SettingsPanel
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
    private static readonly float[] RowY = { 214f, 139f, 64f, -11f, -86f, -161f, -236f };
    private static readonly Color PanelFill = new(0.10f, 0.065f, 0.22f, 0.97f);
    private static readonly Color RowFill = new(0.085f, 0.12f, 0.26f, 0.97f);
    private static readonly Color Cyan = new(0.06f, 0.87f, 0.94f, 0.98f);
    private static readonly Color Pink = new(0.96f, 0.03f, 0.78f, 0.98f);
    private static readonly Color Violet = new(0.49f, 0.18f, 0.91f, 0.98f);
    private readonly TextMeshProUGUI?[] _rows = new TextMeshProUGUI?[7];
    private readonly Image?[] _buttons = new Image?[7];
    private readonly TextMeshProUGUI?[] _dashboardRows = new TextMeshProUGUI?[5];
    private readonly Image?[] _dashboardCards = new Image?[5];
    private readonly GameObject?[] _trackerMarkers = new GameObject?[3];
    private readonly GameObject?[] _avatarMarkers = new GameObject?[3];
    private readonly List<Material> _markerMaterials = new();
    private readonly Dictionary<string, (Pose Pose, float Time, float Speed, float Angular)> _lastSamples = new();
    private GameObject? _root;
    private GameObject? _dashboard;
    private GameObject? _primaryFrame;
    private TextMeshProUGUI? _title;
    private TextMeshProUGUI? _status;
    private TextMeshProUGUI? _footer;
    private TextMeshProUGUI? _dashboardStatus;
    private TextMeshProUGUI? _dashboardNextText;
    private Image? _dashboardNextBackground;
    private TextMeshProUGUI? _dashboardVisualText;
    private Image? _dashboardVisualBackground;
    private Image? _leftCursor;
    private Image? _rightCursor;
    private bool _calibrationScreen;
    private Vector3 _calibrationForward = Vector3.forward;
    private readonly CalibrationDirectionGuide _directionGuide = new();
    private readonly CalibrationGhostGuide _ghostGuide = new();
    private Pose? _panelPose;
    private float _countdownRemaining = -1f;
    private string _bodyStatus = "Body IK: waiting";
    private string _waistMotion = "";
    private TPoseAssessment _poseAssessment;
    private bool _tPoseMode = true;
    private bool _roleScreen;
    private bool _visualScreen;
    private int _armedRoleRow = -1;
    private float _armedRoleUntil;
    private string? _armedDeviceKey;
    private TrackerRole? _selectedRole;
    private int _rolePage;
    private int _devicePage;
    private readonly string?[] _visibleDeviceKeys = new string?[3];
    private int _dashboardPage;
    private int _dashboardPages = 1;
    private string? _calibrationResult;
    private string? _calibrationWarning;
    private string _waistHeightStatus = "";
    private bool _calibrationSucceeded;
    private bool _previousTrigger = true;
    private bool _previousLeftTrigger = true;
    private bool _previousRightTrigger = true;
    private bool _inputArmed;
    private float _closeHoldStart = -1f;
    private float _emptyAreaHoldStart = -1f;
    private float _nextTextUpdate;
    private bool _reopenAfterReset;

    /// <summary>
    /// 詳細パネルが有効な状態で表示されているかを示します。
    /// </summary>
    public bool IsOpen => _root != null && _root.activeSelf;
    /// <summary>
    /// 開いている詳細パネルが校正画面かを示します。
    /// </summary>
    public bool IsCalibrationScreen => IsOpen && _calibrationScreen && !_roleScreen && !_visualScreen;
    /// <summary>
    /// 現在の校正画面が成功結果を保持しているかを示します。
    /// </summary>
    public bool IsCalibrationSucceeded => IsCalibrationScreen && _calibrationSucceeded;
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
    public bool ShowCalibrationPose => IsCalibrationScreen && !_calibrationSucceeded;

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
        _root = _dashboard = _primaryFrame = null;
        _title = _status = _footer = _dashboardStatus = _dashboardNextText =
            _dashboardVisualText = null;
        _dashboardNextBackground = _dashboardVisualBackground = _leftCursor =
            _rightCursor = null;
        Array.Clear(_rows);
        Array.Clear(_buttons);
        Array.Clear(_dashboardRows);
        Array.Clear(_dashboardCards);
        Array.Clear(_trackerMarkers);
        Array.Clear(_avatarMarkers);
        _lastSamples.Clear();
        _armedRoleRow = -1;
        _selectedRole = null;
        _armedDeviceKey = null;
        Array.Clear(_visibleDeviceKeys);
        _previousTrigger = true;
        _previousLeftTrigger = true;
        _previousRightTrigger = true;
        _inputArmed = false;
        _closeHoldStart = -1f;
        _emptyAreaHoldStart = -1f;
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
        _tPoseMode = !_tPoseMode;
        ClearCalibrationResult();
    }

    /// <summary>
    /// 校正画面を選択し、指定配置またはHMD基準の位置で開きます。
    /// </summary>
    /// <param name="camera">ゲーム内のHMD視点として使用するカメラ。</param>
    /// <param name="panelPose">パネルを固定するワールド姿勢。nullならHMD基準の初期配置を使用します。</param>
    public void OpenCalibration(Camera camera, Pose? panelPose = null)
    {
        _selectedRole = null;
        _armedDeviceKey = null;
        _panelPose = panelPose;
        EnsureCreated();
        _reopenAfterReset = false;
        _calibrationScreen = true;
        _roleScreen = false;
        _visualScreen = false;
        ClearCalibrationResult();
        _previousTrigger = true;
        _previousLeftTrigger = true;
        _previousRightTrigger = true;
        SetOpen(true, camera);
    }

    /// <summary>
    /// 全身設定のトップ画面を、指定配置またはHMD基準の位置で開きます。
    /// </summary>
    /// <param name="camera">ゲーム内のHMD視点として使用するカメラ。</param>
    /// <param name="panelPose">パネルを固定するワールド姿勢。nullならHMD基準の初期配置を使用します。</param>
    public void OpenSettings(Camera camera, Pose? panelPose = null)
    {
        _selectedRole = null;
        _armedDeviceKey = null;
        _panelPose = panelPose;
        EnsureCreated();
        _reopenAfterReset = false;
        _calibrationScreen = false;
        _roleScreen = false;
        _visualScreen = false;
        _previousTrigger = true;
        _previousLeftTrigger = true;
        _previousRightTrigger = true;
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
        _calibrationResult = null;
        _calibrationWarning = null;
        _calibrationSucceeded = false;
        _nextTextUpdate = 0f;
    }

    /// <summary>
    /// 校正の結果と成否を保存し、表示と固定姿勢の扱いを更新します。
    /// </summary>
    /// <param name="result">ユーザーへ表示する校正結果の説明。</param>
    /// <param name="succeeded">校正処理が成功したか。</param>
    /// <param name="warning">結果とともに画面を閉じるまで表示する、校正を停止しない警告。</param>
    public void FinishCalibration(string result, bool succeeded, string? warning = null)
    {
        _calibrationResult = result;
        _calibrationWarning = warning;
        _calibrationSucceeded = succeeded;
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
        _directionGuide.Update(ShowCalibrationPose, hasPoses, left, right, _countdownRemaining, _tPoseMode, _poseAssessment, IsCalibrationScreen);
        _ghostGuide.Update(avatar, ShowCalibrationPose && _tPoseMode, hasHands, leftHand, rightHand);
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
        if (!menuOpen)
        {
            CloseForGameplay(camera);
            _previousTrigger = trigger;
            _previousLeftTrigger = leftPointer.Trigger;
            _previousRightTrigger = rightPointer.Trigger;
            return PanelCommand.None;
        }
        EnsureCreated();
        if (_reopenAfterReset)
        {
            SetOpen(true, camera);
            _reopenAfterReset = false;
        }
        if (_root == null) return PanelCommand.None;
        var useGaze = !leftPointer.HasPose && !rightPointer.HasPose;
        var gazeClicked = useGaze && trigger && !_previousTrigger;
        var leftClicked = leftPointer.Trigger && !_previousLeftTrigger;
        var rightClicked = rightPointer.Trigger && !_previousRightTrigger;
        var clicked = gazeClicked || leftClicked || rightClicked;
        _previousTrigger = trigger;
        _previousLeftTrigger = leftPointer.Trigger;
        _previousRightTrigger = rightPointer.Trigger;
        if (!IsOpen) return PanelCommand.None;
        // Opening the panel consumes the press until both triggers are released.
        // Otherwise the opening press can also satisfy a held-close action.
        if (!trigger && !leftPointer.Trigger && !rightPointer.Trigger) _inputArmed = true;
        clicked &= _inputArmed;
        _dashboard?.SetActive(!_visualScreen);

        if (Time.unscaledTime >= _nextTextUpdate)
        {
            _nextTextUpdate = Time.unscaledTime + 0.15f;
            UpdateText(tracking, avatarReady, trackingReady, enabled,
                cameraGuideEnabled, captureCameraFound, cameraGuideUnavailable,
                shadowsOff, lightsOff, avatarHmdAvailable, avatarVisibleInHmd,
                countdownRemaining, tPose);
        }
        var gazePoint = Vector3.zero;
        var gazeHit = useGaze && TryGetGazePoint(camera, out gazePoint);
        var leftPoint = Vector3.zero;
        var rightPoint = Vector3.zero;
        var leftHit = leftPointer.HasPose && TryGetPointerPoint(leftPointer.Pose, out leftPoint);
        var rightHit = rightPointer.HasPose && TryGetPointerPoint(rightPointer.Pose, out rightPoint);
        UpdateCursor(_leftCursor, leftHit, leftPoint);
        UpdateCursor(_rightCursor, rightHit, rightPoint);
        var hasFocusPoint = false;
        var focusTrigger = false;
        var focusPoint = Vector3.zero;
        if (clicked)
        {
            if (rightClicked && rightHit)
            {
                focusPoint = rightPoint;
                hasFocusPoint = true;
                focusTrigger = rightPointer.Trigger;
            }
            else if (leftClicked && leftHit)
            {
                focusPoint = leftPoint;
                hasFocusPoint = true;
                focusTrigger = leftPointer.Trigger;
            }
            else if (gazeHit)
            {
                focusPoint = gazePoint;
                hasFocusPoint = true;
                focusTrigger = trigger;
            }
        }
        else if (rightHit && (rightPointer.Trigger || !trigger))
        {
            focusPoint = rightPoint;
            hasFocusPoint = true;
            focusTrigger = rightPointer.Trigger;
        }
        else if (leftHit && (leftPointer.Trigger || !trigger))
        {
            focusPoint = leftPoint;
            hasFocusPoint = true;
            focusTrigger = leftPointer.Trigger;
        }
        else if (gazeHit)
        {
            focusPoint = gazePoint;
            hasFocusPoint = true;
            focusTrigger = trigger;
        }
        focusTrigger &= _inputArmed;
        var row = hasFocusPoint ? HitRow(focusPoint) : -1;
        var dashboardNextFocused = hasFocusPoint && HitDashboardNext(focusPoint);
        var dashboardVisualFocused = hasFocusPoint && HitDashboardVisual(focusPoint);
        if (row < 0 && !dashboardNextFocused && !dashboardVisualFocused &&
            focusTrigger && countdownRemaining < 0f &&
            hasFocusPoint && Mathf.Abs(focusPoint.x) <= 800f &&
            Mathf.Abs(focusPoint.y) <= 400f)
        {
            if (_emptyAreaHoldStart < 0f) _emptyAreaHoldStart = Time.unscaledTime;
            else if (Time.unscaledTime - _emptyAreaHoldStart >= 1.25f)
            {
                SetOpen(false, camera);
                return PanelCommand.Close;
            }
        }
        else _emptyAreaHoldStart = -1f;
        if (row == 6 && focusTrigger)
        {
            if (_closeHoldStart < 0f) _closeHoldStart = Time.unscaledTime;
        }
        else _closeHoldStart = -1f;
        if (_dashboardNextBackground != null && !_visualScreen)
            _dashboardNextBackground.color = dashboardNextFocused
                ? new Color(0.08f, 0.44f, 0.56f, 0.98f)
                : RowFill;
        if (_dashboardVisualBackground != null && !_visualScreen)
            _dashboardVisualBackground.color = dashboardVisualFocused
                ? new Color(0.08f, 0.44f, 0.56f, 0.98f)
                : RowFill;
        var calibrationReady = trackingReady && avatarReady;
        _primaryFrame?.SetActive(_calibrationScreen && !_roleScreen && !_visualScreen);
        for (var i = 0; i < _buttons.Length; i++)
        {
            if (_buttons[i] != null)
                _buttons[i]!.color = i == row &&
                    (!_visualScreen || i <= 4 || i == 6)
                    ? _calibrationScreen && !_roleScreen && !_visualScreen && i == 0
                        ? Pink
                        : new Color(0.06f, 0.43f, 0.55f, 0.98f)
                    : (_visualScreen && i == 5)
                        ? PanelFill
                        : _calibrationScreen && !_roleScreen && !_visualScreen && i == 0
                            ? calibrationReady
                                ? new Color(0.73f, 0.03f, 0.60f, 0.98f)
                                : new Color(0.23f, 0.09f, 0.29f, 0.97f)
                        : RowFill;
            if (_rows[i] != null)
                _rows[i]!.color = i == row ||
                    _calibrationScreen && !_roleScreen && !_visualScreen && i == 0
                    ? Color.white : new Color(0.56f, 0.88f, 0.97f, 1f);
        }
        if (_roleScreen && _armedRoleRow is >= 0 and <= 3 &&
            Time.unscaledTime <= _armedRoleUntil && _buttons[_armedRoleRow] != null)
            _buttons[_armedRoleRow]!.color = Pink;
        // A held trigger also closes the panel. This covers trigger presses
        // that began just before the gaze entered the Close row.
        if (row == 6 && focusTrigger &&
            (clicked || Time.unscaledTime - _closeHoldStart >= 0.35f))
        {
            SetOpen(false, camera);
            return PanelCommand.Close;
        }
        if (!clicked) return PanelCommand.None;
        if (dashboardNextFocused)
        {
            _dashboardPage = (_dashboardPage + 1) % _dashboardPages;
            _nextTextUpdate = 0f;
            return PanelCommand.None;
        }
        if (dashboardVisualFocused)
        {
            _visualScreen = true;
            _nextTextUpdate = 0f;
            return PanelCommand.None;
        }
        if (row < 0) return PanelCommand.None;
        if (_visualScreen)
        {
            if (row == 0) return PanelCommand.ToggleCameraGuide;
            if (row == 1) return PanelCommand.ToggleAvatarShadows;
            if (row == 2) return PanelCommand.ToggleAvatarLights;
            if (row == 3) return avatarHmdAvailable ?
                PanelCommand.ToggleAvatarInHeadset : PanelCommand.None;
            if (row == 4)
            {
                _visualScreen = false;
                _nextTextUpdate = 0f;
            }
            return PanelCommand.None;
        }
        if (_roleScreen)
        {
            if (_selectedRole is { } selected)
            {
                if (row is >= 0 and <= 2 && _visibleDeviceKeys[row] is { } key)
                {
                    if (_armedDeviceKey == key && Time.unscaledTime <= _armedRoleUntil)
                    {
                        tracking.Assign(selected, key);
                        _armedDeviceKey = null;
                        _armedRoleRow = -1;
                        _selectedRole = null;
                        _nextTextUpdate = 0f;
                        return PanelCommand.AssignmentChanged;
                    }
                    _armedDeviceKey = key;
                    _armedRoleRow = row;
                    _armedRoleUntil = Time.unscaledTime + 4f;
                }
                else if (row == 3)
                {
                    _devicePage = (_devicePage + 1) % Math.Max(1, (tracking.Devices.Count + 2) / 3);
                    _armedDeviceKey = null;
                }
                else if (row == 4)
                {
                    tracking.Assign(selected, null);
                    _selectedRole = null;
                    _nextTextUpdate = 0f;
                    return PanelCommand.AssignmentChanged;
                }
                else if (row == 5) _selectedRole = null;
                _nextTextUpdate = 0f;
                return PanelCommand.None;
            }
            if (row is >= 0 and <= 2)
            {
                _selectedRole = TrackerRoles.All[_rolePage * 3 + row];
                _devicePage = 0;
                _armedDeviceKey = null;
                _armedRoleRow = -1;
                Array.Clear(_visibleDeviceKeys);
            }
            else if (row == 3)
            {
                if (_armedDeviceKey == "AUTO" && Time.unscaledTime <= _armedRoleUntil)
                {
                    tracking.AutoAssign(camera);
                    _armedDeviceKey = null;
                    _armedRoleRow = -1;
                    _nextTextUpdate = 0f;
                    return PanelCommand.AssignmentChanged;
                }
                _armedDeviceKey = "AUTO";
                _armedRoleRow = 3;
                _armedRoleUntil = Time.unscaledTime + 4f;
            }
            else if (row == 4) _rolePage = (_rolePage + 1) % (TrackerRoles.All.Length / 3);
            else if (row == 5)
            {
                _roleScreen = false;
                _armedRoleRow = -1;
            }
            _nextTextUpdate = 0f;
            return PanelCommand.None;
        }
        if (_calibrationScreen)
        {
            if (row == 0)
                return countdownRemaining < 0f && trackingReady && avatarReady
                    ? PanelCommand.StartCalibration : PanelCommand.None;
            if (row == 1) return PanelCommand.TogglePoseMode;
            if (row == 2)
            {
                _roleScreen = true;
                _armedRoleRow = -1;
                _nextTextUpdate = 0f;
                return PanelCommand.None;
            }
            if (row == 3)
            {
                _calibrationScreen = false;
                _nextTextUpdate = 0f;
            }
            return PanelCommand.None;
        }
        if (row == 0) return PanelCommand.Recalibrate;
        if (row == 1)
        {
            _roleScreen = true;
            _armedRoleRow = -1;
            _nextTextUpdate = 0f;
            return PanelCommand.None;
        }
        if (row == 2) return PanelCommand.ToggleTracking;
        if (row == 5) return PanelCommand.ToggleUltimateRotation;
        if (row == 3)
        {
            _visualScreen = true;
            _nextTextUpdate = 0f;
        }
        return PanelCommand.None;
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
        VrUi.Image(_root, "CalibrationTab", new Vector2(820f, 65f),
            new Vector2(-340f, 350f), Violet);
        VrUi.HexOutline(_root, "CalibrationTabEdge", new Vector2(825f, 69f),
            new Vector2(-340f, 350f), new Color(0.68f, 0.37f, 1f, 0.8f), 2f, 42f);
        VrUi.Image(_root, "Divider", new Vector2(3f, 740f), new Vector2(105f, 0f),
            new Color(0.24f, 0.20f, 0.49f, 0.75f));
        _title = VrUi.Text(_root, "Title", new Vector2(820f, 52f), new Vector2(-340f, 350f),
            35f, Color.white, TextAlignmentOptions.Center);
        _status = VrUi.Text(_root, "Status", new Vector2(820f, 45f), new Vector2(-340f, 292f),
            27f, Color.white, TextAlignmentOptions.Center);
        for (var i = 0; i < _rows.Length; i++)
        {
            _buttons[i] = VrUi.Image(_root, $"Button{i}", new Vector2(820f, 70f),
                new Vector2(-340f, RowY[i]), RowFill);
            _rows[i] = VrUi.Text(_root, $"ButtonLabel{i}", new Vector2(780f, 62f),
                new Vector2(-340f, RowY[i]), 33f, Color.white, TextAlignmentOptions.MidlineLeft);
        }
        _primaryFrame = VrUi.HexOutline(_root, "CalibrationButtonFrame",
            new Vector2(830f, 77f), new Vector2(-340f, RowY[0]),
            new Color(1f, 0.30f, 0.88f, 0.72f), 2f, 42f);
        _primaryFrame.SetActive(false);
        _footer = VrUi.Text(_root, "Help", new Vector2(840f, 66f), new Vector2(-340f, -342f),
            22f, new Color(0.7f, 0.8f, 0.9f), TextAlignmentOptions.Center);
        CreateDashboard();
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
    /// LIVE TRACKERSの表示領域とページ送り・描画設定への入口を作成します。
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
        VrUi.Image(_dashboard, "DashboardTab", new Vector2(630f, 65f),
            new Vector2(0f, 350f), new Color(0.25f, 0.13f, 0.47f, 0.98f));
        VrUi.HexOutline(_dashboard, "DashboardTabEdge", new Vector2(635f, 69f),
            new Vector2(0f, 350f), new Color(0.53f, 0.26f, 0.81f, 0.65f), 2f, 42f);
        VrUi.Text(_dashboard, "DashboardTitle", new Vector2(620f, 52f),
            new Vector2(0f, 350f), 35f, Color.white,
            TextAlignmentOptions.Center).text = "LIVE TRACKERS";
        _dashboardStatus = VrUi.Text(_dashboard, "DashboardStatus", new Vector2(620f, 42f),
            new Vector2(0f, 292f), 25f, Color.white, TextAlignmentOptions.Center);
        for (var i = 0; i < _dashboardRows.Length; i++)
        {
            var y = 205f - i * 95f;
            _dashboardCards[i] = VrUi.Image(_dashboard, $"TrackerCard{i}",
                new Vector2(630f, 88f), new Vector2(0f, y),
                new Color(0.07f, 0.07f, 0.18f, 0.97f));
            _dashboardRows[i] = VrUi.Text(_dashboard, $"TrackerCardText{i}",
                new Vector2(600f, 82f), new Vector2(0f, y),
                22f, new Color(0.52f, 0.91f, 0.98f), TextAlignmentOptions.MidlineLeft);
        }
        _dashboardNextBackground = VrUi.Image(_dashboard, "DashboardNext",
            new Vector2(630f, 62f), new Vector2(0f, -282f),
            RowFill);
        _dashboardNextText = VrUi.Text(_dashboard, "DashboardNextText",
            new Vector2(600f, 58f), new Vector2(0f, -282f),
            27f, Color.white, TextAlignmentOptions.Center);
        _dashboardVisualBackground = VrUi.Image(_dashboard, "DashboardVisual",
            new Vector2(630f, 55f), new Vector2(0f, -348f), RowFill);
        _dashboardVisualText = VrUi.Text(_dashboard, "DashboardVisualText",
            new Vector2(600f, 52f), new Vector2(0f, -348f),
            25f, Color.white, TextAlignmentOptions.Center);
        _dashboardVisualText.text = "VISUAL OPTIONS  >";
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
        if (_visualScreen)
        {
            UpdateVisualText(cameraGuideEnabled, captureCameraFound,
                cameraGuideUnavailable, shadowsOff, lightsOff,
                avatarHmdAvailable, avatarVisibleInHmd);
            return;
        }
        UpdateDashboardText(tracking);
        for (var i = 0; i < _rows.Length; i++) _rows[i]!.fontSize = 33f;
        if (_roleScreen) { UpdateRoleText(tracking); return; }
        _title!.text = _calibrationScreen
            ? (_tPoseMode ? "T-POSE CALIBRATION" : "STANDING CALIBRATION")
            : "FULL BODY SETTINGS";
        _status!.text = _calibrationScreen
            ? $"FRONT = {_directionGuide.Source}  |  Trackers: {tracking.TrackerCount}  |  Avatar: {(avatarReady ? "OK" : "missing")}" 
            : $"{tracking.SourceLabel}: {tracking.TrackerCount}  |  HMD: {(tracking.HmdPresent ? "OK" : "missing")}  |  Avatar: {(avatarReady ? "OK" : "missing")}";
        if (_calibrationScreen)
        {
            _rows[0]!.text = countdownRemaining >= 0f
                ? _tPoseMode
                    ? $"T-pose then hold: {Mathf.CeilToInt(countdownRemaining)}"
                    : $"Hold still: {Mathf.CeilToInt(countdownRemaining)}"
                : _calibrationResult == null
                    ? _tPoseMode
                        ? "START T-POSE CALIBRATION"
                        : "CALIBRATE STANDING"
                : _calibrationSucceeded ? "RECALIBRATE" : "RETRY CALIBRATION";
            _rows[1]!.text = $"Pose: {(_tPoseMode ? "T-pose" : "standing")}  >";
            _rows[2]!.text = "TRACKER ROLES  >";
            _rows[3]!.text = "Back to settings";
            _rows[4]!.text = _bodyStatus;
            _rows[5]!.text = _waistMotion;
            _rows[4]!.fontSize = _rows[5]!.fontSize = 24f;
            _rows[6]!.text = "Close panel";
            var calibrationStatus = _calibrationResult ?? tPose.Status;
            var warning = _calibrationResult != null ? _calibrationWarning : tPose.Warning;
            var poseHint = warning != null ? "WARNING: " + warning :
                _calibrationSucceeded ? "Close panel; entry buttons stay available" :
                _waistHeightStatus.StartsWith("Waist is only", StringComparison.Ordinal)
                    ? _waistHeightStatus
                    : _tPoseMode
                        ? "Size auto-fits; START; face dot; arms level; feet apart"
                        : "Size auto-fits; face dot; stand still with feet apart";
            _footer!.text = $"{calibrationStatus}\n{poseHint}";
        }
        else
        {
            _rows[0]!.text = "CALIBRATE FULL BODY  >";
            _rows[1]!.text = "TRACKER ROLES  >";
            _rows[2]!.text = $"Tracking: {(enabled ? "ON" : "OFF")}";
            _rows[3]!.text = "VISUAL OPTIONS  >";
            _rows[4]!.text = _bodyStatus;
            _rows[5]!.text = $"Ultimate 180 correction: {(tracking.UltimateCorrectionEnabled ? "ON" : "OFF")}";
            _rows[4]!.fontSize = 24f;
            _rows[5]!.fontSize = 30f;
            _rows[6]!.text = "Close panel";
            _footer!.text = "Ultimate correction: for reversed forward AND sideways motion\nAfter changing this option, calibrate again";
        }
    }

    /// <summary>
    /// 部位一覧または個体選択ページを、割り当てと確認待ち状態に合わせて更新します。
    /// </summary>
    /// <param name="tracking">デバイス一覧・割り当て・追跡情報を提供する管理オブジェクト。</param>
    private void UpdateRoleText(Tracking tracking)
    {
        foreach (var text in _rows) if (text != null) text.fontSize = 27f;
        if (_selectedRole is { } selected)
        {
            _title!.text = $"ASSIGN: {TrackerRoles.Label(selected).ToUpperInvariant()}";
            var devices = tracking.GetTelemetry();
            var pages = Math.Max(1, (devices.Count + 2) / 3);
            _devicePage = Math.Min(_devicePage, pages - 1);
            _status!.text = $"Move the device to identify it | Page {_devicePage + 1}/{pages}";
            for (var row = 0; row < 3; row++)
            {
                var index = _devicePage * 3 + row;
                _visibleDeviceKeys[row] = index < devices.Count ? devices[index].Device.Key : null;
                _rows[row]!.fontSize = 22f;
                if (index >= devices.Count) { _rows[row]!.text = ""; continue; }
                var d = devices[index];
                var confirm = _armedDeviceKey == d.Device.Key && Time.unscaledTime <= _armedRoleUntil;
                _rows[row]!.text = $"{d.Source} #{d.Id}  {d.Serial}  {(confirm ? "CONFIRM" : MotionLabel(d))}\n{d.Name}";
            }
            _rows[3]!.text = "Next device page  >";
            _rows[4]!.text = "CLEAR / USE GAME DEFAULT";
            _rows[5]!.text = "Back to roles";
            _footer!.text = "Choose the same device twice to confirm\nA device can belong to one role only";
        }
        else
        {
            _title!.text = "TRACKER ROLES";
            _status!.text = $"Choose a body part | Page {_rolePage + 1}/{TrackerRoles.All.Length / 3}";
            for (var row = 0; row < 3; row++)
            {
                var role = TrackerRoles.All[_rolePage * 3 + row];
                _rows[row]!.text = $"{TrackerRoles.Label(role)}  >\n{tracking.AssignmentUiLabel(role)}";
            }
            _rows[3]!.text = _armedDeviceKey == "AUTO" && Time.unscaledTime <= _armedRoleUntil ? "AUTO: CONFIRM (stand facing mirror)" : "AUTO: suggest waist / ankles / toes";
            _rows[4]!.text = "Next body parts  >";
            _rows[5]!.text = _calibrationScreen ? "Back to calibration" : "Back to settings";
            _footer!.text = tracking.AssignmentStatus;
        }
        _rows[6]!.text = "Close panel";
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
        _title!.text = "VISUAL OPTIONS";
        _status!.text = !cameraGuideEnabled ? "Camera guide: OFF" :
            cameraGuideUnavailable ? "Camera guide: unavailable (see log)" : _cameraGuideStatus;
        for (var i = 0; i < _rows.Length; i++) _rows[i]!.fontSize = 31f;
        _rows[0]!.text = $"Camera guide in headset: {(cameraGuideEnabled ? "ON" : "OFF")}";
        _rows[1]!.text = $"VRM cast/receive shadows: {(shadowsOff ? "OFF" : "ON")}";
        _rows[2]!.text = $"VRM lights: {(lightsOff ? "OFF" : "ON")}";
        _rows[3]!.text = avatarHmdAvailable
            ? $"VRM in headset: {(avatarVisibleInHmd ? "ON" : "OFF")}" 
            : "VRM in headset: load an avatar";
        _rows[4]!.text = _roleScreen ? "Back to tracker roles" :
            _calibrationScreen ? "Back to calibration" : "Back to settings";
        _rows[5]!.text = cameraGuideEnabled && captureCameraFound ? _cameraGuideLocation : "";
        _rows[6]!.text = "Close panel";
        _footer!.text = "Cyan sphere marks the camera; visual options are shared";
    }

    /// <summary>
    /// 現在のデバイス一覧とページ番号に合わせてLIVE TRACKERSを更新します。
    /// </summary>
    /// <param name="tracking">デバイス一覧・割り当て・追跡情報を提供する管理オブジェクト。</param>
    private void UpdateDashboardText(Tracking tracking)
    {
        var trackers = tracking.GetTelemetry();
        _dashboardPages = Math.Max(1, (trackers.Count + 4) / 5);
        _dashboardPage = Math.Min(_dashboardPage, _dashboardPages - 1);
        _dashboardStatus!.text =
            $"{trackers.Count} devices  |  Page {_dashboardPage + 1}/{_dashboardPages}";
        for (var i = 0; i < _dashboardRows.Length; i++)
        {
            var index = _dashboardPage * 5 + i;
            _dashboardRows[i]!.text = index < trackers.Count
                ? FormatDashboardLine(tracking, trackers[index])
                : i == 0 ? "No active tracker found" : "";
        }
        _dashboardNextText!.text = _dashboardPages > 1 ? "Next tracker page  >" : "All trackers shown";
    }

    /// <summary>
    /// デバイスの識別情報と生の追跡座標・速度を表示用の文字列へ整形します。
    /// </summary>
    /// <param name="tracking">デバイス一覧・割り当て・追跡情報を提供する管理オブジェクト。</param>
    /// <param name="tracker">表示する個体の識別情報と追跡計測値。</param>
    /// <returns>個体情報・座標・速度、または追跡喪失の表示文。</returns>
    private string FormatDashboardLine(Tracking tracking, TrackerTelemetry tracker)
    {
        var model = string.IsNullOrWhiteSpace(tracker.Name) ? "(unknown model)" : tracker.Name;
        var serial = string.IsNullOrWhiteSpace(tracker.Serial) ? "(no serial)" : tracker.Serial;
        var key = tracker.Device.Key;
        if (!tracker.HasPose)
        {
            _lastSamples.Remove(key);
            return $"{tracker.Source} #{tracker.Id}  {model}\nS/N {serial}\nTracking lost";
        }
        var speed = UpdateTrackerSpeed(key, tracker.Pose);
        var position = tracker.Position;
        return $"{tracker.Source} #{tracker.Id}  {model}\nS/N {serial}\n" +
            $"X {position.x:+0.00;-0.00;0.00}  Y {position.y:+0.00;-0.00;0.00}  " +
            $"Z {position.z:+0.00;-0.00;0.00} m  {speed:0.00}m/s" +
            (IsMoving(key) ? " MOVING" : "");
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
            _inputArmed = false;
            var horizontal = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            if (horizontal.sqrMagnitude < 0.01f) horizontal = Vector3.forward;
            if (_calibrationScreen) _calibrationForward = _directionGuide.Begin(camera);
            else _directionGuide.Hide();
            var placement = _panelPose ?? new Pose(camera.transform.position + horizontal * 1.4f,
                Quaternion.LookRotation(horizontal, Vector3.up));
            _root.transform.SetPositionAndRotation(placement.Position, placement.Rotation);
            _dashboardPage = 0;
            _lastSamples.Clear();
            _nextTextUpdate = 0f;
            _closeHoldStart = -1f;
            _emptyAreaHoldStart = -1f;
        }
        _root.SetActive(open);
        if (open) return;
        _directionGuide.Hide();
        _ghostGuide.Hide();
        _calibrationScreen = false;
        _roleScreen = false;
        _selectedRole = null;
        _armedDeviceKey = null;
        _visualScreen = false;
        _armedRoleRow = -1;
        _dashboardPage = 0;
        _lastSamples.Clear();
        _closeHoldStart = -1f;
        _emptyAreaHoldStart = -1f;
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

    /// <summary>
    /// パネルローカル座標から、操作行の当たり判定を行います。
    /// </summary>
    /// <param name="point">Canvasローカル座標で表した交点。XとYはUIピクセルです。</param>
    /// <returns>該当行の0始まりインデックス。範囲外なら-1。</returns>
    private int HitRow(Vector3 point)
    {
        if (Mathf.Abs(point.x + 340f) > 410f) return -1;
        // The bottom action remains easy to hit when the HMD gaze jitters.
        if (point.y <= -196f && point.y >= -310f) return 6;
        for (var i = 0; i < RowY.Length - 1; i++)
            if (Mathf.Abs(point.y - RowY[i]) < 35f) return i;
        return -1;
    }

    /// <summary>
    /// LIVE TRACKERSのページ送り領域に当たるかを判定します。
    /// </summary>
    /// <param name="point">Canvasローカル座標で表した交点。XとYはUIピクセルです。</param>
    /// <returns>複数ページがあり、有効なページ送り領域内ならtrue。</returns>
    private bool HitDashboardNext(Vector3 point)
    {
        if (_visualScreen || _dashboardPages <= 1) return false;
        return Mathf.Abs(point.x - 445f) < 315f && Mathf.Abs(point.y + 282f) < 31f;
    }

    /// <summary>
    /// LIVE TRACKERS下の描画設定ボタンに当たるかを判定します。
    /// </summary>
    /// <param name="point">Canvasローカル座標で表した交点。XとYはUIピクセルです。</param>
    /// <returns>描画設定画面以外で、有効なボタン領域内ならtrue。</returns>
    private bool HitDashboardVisual(Vector3 point)
    {
        if (_visualScreen) return false;
        return Mathf.Abs(point.x - 445f) < 315f && Mathf.Abs(point.y + 348f) < 28f;
    }

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
