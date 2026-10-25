using MelonLoader;
using Il2CppSynth.Avatars;
using UnityEngine;

[assembly: MelonInfo(typeof(SynthRidersFullBody.FullBodyMod), "Synth Riders Full Body", "0.59.8", "SynthRidersFullBody")]

namespace SynthRidersFullBody;

/// <summary>
/// MelonLoaderのイベントを受け、入力取得・UI・校正・全身駆動・描画設定を統括します。
/// </summary>
public sealed class FullBodyMod : MelonMod
{
    private Tracking? _tracking;
    private ModConfig? _config;
    private string? _configPath;
    private readonly SettingsPanel _panel = new();
    private readonly AvatarMenuButton _menuButton = new();
    private readonly TrackerPoseFilter _poseFilter = new();
    private readonly CalibrationSampler _calibrationSampler = new();
    private readonly AvatarRendering _avatarRendering = new();
    private readonly CaptureCameraGuide _captureCameraGuide = new();
    private readonly MenuBustCamera _menuBustCamera = new();
    private string _lastMenuCameraDiagnostic = "";
    private readonly AvatarHmdVisibility _avatarHmdVisibility = new();
    private readonly AvatarExpressions _expressions = new();
    private ExpressionInput _expressionInput;
    private string _expressionDiagnostic = "";
    private float _nextExpressionDiagnostic;
    private float _nextSpaceWarning;
    private float _retryExpressionsAt;
    private AvatarIK? _avatar;
    private float _calibrationEnd;
    private float _nextScan;
    private bool _lastReady;
    private bool _hasScanResult;
    private string _lastDeviceSummary = "";
    private float _panelRetryAt;
    private float _menuButtonRetryAt;
    private float _nextPanelErrorLog;
    private float _nextMenuButtonErrorLog;
    private string _lastPanelError = "";
    private string _lastMenuButtonError = "";
    private string _lastDriveMode = "";
    private string _lastMenuPlacement = "";
    private bool _hasPoseResult;
    private bool _lastPoseReady;
    private float _nextPoseDiagnostics;
    private float _nextAutomaticPoseDiagnostics;
    private float _nextPoseDiagnosticsError;
    private float _retryTrackingAt;
    private float _nextTrackingErrorLog;
    private string _lastTrackingError = "";
    private float _nextConfigErrorLog;
    private float _nextVisualErrorLog;
    private float _nextHeadsetVisibilityErrorLog;
    private string _lastCaptureCameraDiagnosticKey = "";
    private bool _calibrationRequired;
    private CalibrationSession? _sessionCalibration;
    private readonly CalibrationRecoveryGate _recoveryGate = new();
    private bool _recoveryPending;
    private bool _rebaseOnRecovery;
    private float _nextRecoveryAttempt;
    private int _headCameraId;

    /// <summary>
    /// 設定と追跡管理を初期化し、起動時の構成をログへ記録します。
    /// </summary>
    public override void OnInitializeMelon()
    {
        try
        {
            _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserData", "SynthRidersFullBody.json");
            try { _config = ModConfig.Load(_configPath); }
            catch (Exception exception)
            {
                LoggerInstance.Warning($"Configuration could not be loaded from {_configPath}: " +
                    $"{exception.GetType().Name}: {exception.Message}. " +
                    "Using defaults for this session; the existing file was not changed.");
                _config = new ModConfig();
                _configPath = null;
            }
            _tracking = new Tracking(_config);
            _panel.SetUiConfiguration(_config);
            // Restore avatar renderers before capture-camera listeners inspect them.
            try { _avatarHmdVisibility.Initialize(); }
            catch (Exception exception) { LoggerInstance.Warning($"HMD renderer guard initialization failed: {exception}"); }
            try { _menuBustCamera.Initialize(); }
            catch (Exception exception) { LoggerInstance.Warning($"Menu bust camera initialization failed: {exception}"); }
            TrySaveConfig();
            LoggerInstance.Msg($"Configuration: {_configPath ?? "session defaults"}");
            LoggerInstance.Msg($"Avatar correction: 1€ filter={_config.SmoothingEnabled}, " +
                $"position={_config.PositionMinCutoffHz:0.##}Hz/{_config.PositionBeta:0.##}, " +
                $"rotation={_config.RotationMinCutoffHz:0.##}Hz/{_config.RotationBeta:0.##}, " +
                $"pelvis tilt={_config.PelvisMaxTiltDegrees:0.#}° " +
                $"at weight={_config.PelvisTiltWeight:0.##}, " +
                $"VRIK position weight={_config.PelvisPositionWeight:0.##}, " +
                $"VRIK rotation weight={_config.PelvisRotationWeight:0.##}, " +
                $"VRIK root controller={_config.UseVrikRootController}, " +
                $"virtual chest weight={_config.ChestGoalWeight:0.##}, " +
                $"knee flexion={_config.KneeMaxFlexionDegrees:0.#}°, " +
                $"VRIK knee bend weight={_config.KneeBendGoalWeight:0.##}.");
        }
        catch (Exception exception)
        {
            LoggerInstance.Error($"Initialization failed: {exception}");
        }
    }

    /// <summary>
    /// MOD終了時にUI・IK目標・描画変更を解放し、ゲーム側の状態を復元します。
    /// </summary>
    public override void OnDeinitializeMelon()
    {
        ResetExpressions(true);
        _avatarHmdVisibility.Dispose();
        _avatarRendering.Restore();
        _captureCameraGuide.Dispose();
        _menuBustCamera.Dispose();
        try { _avatar?.Dispose(); }
        catch (Exception exception)
        {
            LoggerInstance.Warning($"Avatar cleanup during shutdown failed: {exception.Message}");
        }
        _avatar = null;
        try { _panel.ResetUi(); }
        catch { /* Unity objects may already be destroyed during shutdown. */ }
        try { _menuButton.ResetUi(); }
        catch { /* Unity objects may already be destroyed during shutdown. */ }
        UiLocalization.Clear();
    }

    /// <summary>
    /// シーン依存のUIと参照をリセットし、保持した校正の復帰を予約します。
    /// </summary>
    /// <param name="buildIndex">MelonLoaderから通知されたシーンのビルド番号。</param>
    /// <param name="sceneName">MelonLoaderから通知されたシーン名。</param>
    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        ResetExpressions(true);
        _panel.CloseForSceneChange();
        _menuButton.ResetUi();
        _avatar?.HideTPosePreview();
        _calibrationEnd = 0f;
        _calibrationSampler.Reset();
        _nextScan = 0f;
        _avatarRendering.RequestRescan();
        _avatarHmdVisibility.RequestRefresh();
        _captureCameraGuide.RequestRescan();
        _menuBustCamera.RequestRescan();
        _headCameraId = 0;
        if (_sessionCalibration != null) QueueCalibrationRecovery("scene loaded: " + sceneName, true);
        LoggerInstance.Msg($"Scene loaded: {sceneName}; VRM visual settings will be reapplied.");
    }

    /// <summary>
    /// 入力・アバター探索・校正状態・UIを更新し、有効な追跡姿勢をアバターへ適用します。
    /// </summary>
    public override void OnLateUpdate()
    {
        _avatarHmdVisibility.RecoverRenderState();
        if (_tracking == null || _config == null || Time.unscaledTime < _retryTrackingAt)
            return;
        try
        {
            UiLocalization.Update();
            var camera = FindHeadCamera();
            if (camera == null)
            {
                ResetExpressions(false);
                _panel.CloseForSceneChange();
                _menuButton.Hide();
                _avatar?.HideTPosePreview();
                _avatar?.UpdateTrackingState(false);
                _calibrationEnd = 0f;
                _calibrationSampler.Reset();
                _poseFilter.Reset();
                _captureCameraGuide.Hide();
                _menuBustCamera.Update(null, null, false, false);
                return;
            }
            var avatarMenuOpen = _menuButton.IsAvatarMenuOpen();
            if (_headCameraId != 0 && _headCameraId != camera.GetInstanceID() && _sessionCalibration != null)
                QueueCalibrationRecovery("head camera replaced", true);
            _headCameraId = camera.GetInstanceID();
            if (_avatar != null && _avatar.Animator == null)
            {
                try { _avatar.Dispose(); } catch { /* Destroyed rig; saved calibration contains values only. */ }
                _avatar = null;
                _nextScan = 0f;
                if (_sessionCalibration != null) QueueCalibrationRecovery("avatar temporarily unavailable", false);
            }
            if (!avatarMenuOpen)
            {
                _panel.CloseForGameplay(camera);
                _calibrationEnd = 0f;
                _calibrationSampler.Reset();
            }

            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 2f;
                var ready = _tracking.Refresh();
                if (!_hasScanResult || ready != _lastReady)
                    LoggerInstance.Msg(ready ? $"Body tracker roles ready via {_tracking.SourceLabel}."
                        : "Waiting for HMD, waist and two foot tracker poses.");
                _lastReady = ready;
                _hasScanResult = true;
                if (_tracking.DeviceSummary != _lastDeviceSummary)
                {
                    _lastDeviceSummary = _tracking.DeviceSummary;
                    LoggerInstance.Msg($"Tracking source: {_tracking.SourceLabel}\n{_lastDeviceSummary}");
                }
                var currentAvatar = AvatarIK.Find(camera, _config, _avatar);
                if (!ReferenceEquals(_avatar, currentAvatar) ||
                    _avatar?.Animator != currentAvatar?.Animator ||
                    _avatar?.IkComponent != currentAvatar?.IkComponent)
                {
                    try { _avatar?.Dispose(); } catch { /* A scene may have already destroyed the old rig. */ }
                    _avatar = currentAvatar;
                    _lastDriveMode = "";
                    if (_sessionCalibration != null) QueueCalibrationRecovery("avatar instance or IK changed", false);
                    else InvalidateCalibration();
                    _nextPoseDiagnostics = 0f;
                    if (_avatar != null && _sessionCalibration == null)
                    {
                        LoggerInstance.Msg("Avatar changed; full body calibration required.");
                    }
                }
                else if (_avatar != null &&
                         _avatar.HasCalibrationScaleChanged(out var scaleChange))
                {
                    QueueCalibrationRecovery(scaleChange, false);
                }
            }

            if (_avatar != null && _avatar.HasCalibrationBindingChanged(out var bindingChange))
            {
                QueueCalibrationRecovery(bindingChange, false);
            }
            TryRecoverCalibration(camera);

            try
            {
                _avatarRendering.Update(_avatarHmdVisibility.GetActiveAvatarRoot() ??
                    _avatar?.Animator?.gameObject,
                    _config.DisableAvatarShadows, _config.DisableAvatarLights);
            }
            catch (Exception exception)
            {
                if (Time.unscaledTime >= _nextVisualErrorLog)
                {
                    LoggerInstance.Warning($"VRM rendering update failed: {exception}");
                    _nextVisualErrorLog = Time.unscaledTime + 30f;
                }
                _avatarRendering.Restore();
            }
            try
            {
                if (_avatarHmdVisibility.ApplySaved(_config))
                    LoggerInstance.Msg($"Saved VRM headset visibility reapplied. {_avatarHmdVisibility.LastApplication}");
                var renderDiagnostic = _avatarHmdVisibility.TakeRenderDiagnostic();
                if (renderDiagnostic != null) LoggerInstance.Msg($"VRM HMD render coverage: {renderDiagnostic}");
                var visibilityError = _avatarHmdVisibility.Error ?? _avatarHmdVisibility.RenderError;
                if (visibilityError != null && Time.unscaledTime >= _nextHeadsetVisibilityErrorLog)
                {
                    LoggerInstance.Warning($"VRM headset visibility update failed: {visibilityError}");
                    _nextHeadsetVisibilityErrorLog = Time.unscaledTime + 30f;
                }
            }
            catch (Exception exception)
            {
                if (Time.unscaledTime >= _nextHeadsetVisibilityErrorLog)
                {
                    LoggerInstance.Warning($"VRM headset visibility update failed: {exception}");
                    _nextHeadsetVisibilityErrorLog = Time.unscaledTime + 30f;
                }
            }
            try
            {
                _menuBustCamera.Update(camera, _avatar?.Animator, _config.MenuBustShot, _panel.ShowCalibrationPose || _recoveryPending);
                _panel.SetMenuCameraStatus(_menuBustCamera.Status);
                var menuCameraDiagnostic = _menuBustCamera.Diagnostic;
                if (_lastMenuCameraDiagnostic != menuCameraDiagnostic)
                {
                    _lastMenuCameraDiagnostic = menuCameraDiagnostic;
                    LoggerInstance.Msg($"Menu bust camera: {menuCameraDiagnostic}");
                }
                _captureCameraGuide.Update(camera, _config.ShowCaptureCameraGuide, _menuBustCamera);
                var cameraDiagnosticKey = $"{camera.GetInstanceID()}|{_captureCameraGuide.DiagnosticKey}";
                if (_lastCaptureCameraDiagnosticKey != cameraDiagnosticKey)
                {
                    _lastCaptureCameraDiagnosticKey = cameraDiagnosticKey;
                    LoggerInstance.Msg($"Capture camera guide: {_captureCameraGuide.Status}.");
                    if (_config.ShowCaptureCameraGuide)
                        LoggerInstance.Msg($"Capture camera diagnostics: {_captureCameraGuide.Diagnostics(camera)}");
                }
                if (_captureCameraGuide.Error != null &&
                    Time.unscaledTime >= _nextVisualErrorLog)
                {
                    LoggerInstance.Warning(_captureCameraGuide.Error);
                    _nextVisualErrorLog = Time.unscaledTime + 30f;
                }
            }
            catch (Exception exception)
            {
                if (Time.unscaledTime >= _nextVisualErrorLog)
                {
                    LoggerInstance.Warning($"Capture camera guide update failed: {exception}");
                    _nextVisualErrorLog = Time.unscaledTime + 30f;
                }
                _captureCameraGuide.Hide();
                _menuBustCamera.Update(null, null, false, false);
            }

            PrepareExpressions();
            _tracking.ReadControllerTriggers(out var trigger,
                out var leftTrigger, out var rightTrigger);
            // SourceLabel describes current HMD availability, not a change to
            // the explicitly assigned devices. A brief outage must not erase calibration.
            var frame = _tracking.ReadWorldPoses(camera);
            if (_tracking.SpaceWarning is { } warning && Time.unscaledTime >= _nextSpaceWarning)
            {
                LoggerInstance.Warning(warning);
                _nextSpaceWarning = Time.unscaledTime + 30f;
            }
            var hasPoses = frame.Complete;
            var waist = frame.Waist;
            var left = frame.Left;
            var right = frame.Right;
            if (!frame.HasWaist) _poseFilter.Reset();
            else _avatar?.RecordRawWaist(waist);
            _panel.SetCalibrationRequired(_calibrationRequired);
            _panel.SetBodyDiagnostics(!_config.Enabled ? "Body IK: OFF" :
                _avatar == null ? "Body IK: avatar missing" :
                _recoveryPending ? "Body IK: restoring saved calibration..." :
                _calibrationRequired ? "Body IK: calibration required" :
                !frame.HasWaist ? "Body IK: game (waist not tracked)" : _avatar.SolverStatus,
                !frame.HasWaist ? "Waist: input unavailable" : _avatar?.WaistMotionSummary ?? "");
            var hasLeftHand = _tracking.TryGetControllerWorldPose(camera, true, out var leftHand);
            var hasRightHand = _tracking.TryGetControllerWorldPose(camera, false, out var rightHand);
            var hasHands = hasLeftHand && hasRightHand;
            var hasLeftPointer = _tracking.TryGetControllerPointerWorldPose(camera, true, out var leftAim);
            var hasRightPointer = _tracking.TryGetControllerPointerWorldPose(camera, false, out var rightAim);
            var leftPointer = new ControllerPointer(leftAim, hasLeftPointer, leftTrigger);
            var rightPointer = new ControllerPointer(rightAim, hasRightPointer, rightTrigger);
            var tPose = TPoseCalibration.Check(camera, _panel.CalibrationForward,
                hasHands, leftHand, rightHand,
                hasPoses, waist, left, right, _panel.UseTPose);
            var missingAssigned = _tracking.MissingAssignedPose();
            if (missingAssigned != null) tPose = new TPoseAssessment(false, missingAssigned);
            if (Time.unscaledTime >= _menuButtonRetryAt)
            {
                try
                {
                    var action = _menuButton.Tick(camera, trigger, leftPointer,
                        rightPointer, _panel, _calibrationRequired);
                    if (action != AvatarMenuAction.None) _panel.PlayFeedback(_tracking, _menuButton.ActivationHand);
                    if (_lastMenuPlacement != _menuButton.Placement)
                    {
                        _lastMenuPlacement = _menuButton.Placement;
                        LoggerInstance.Msg($"Full body menu placement: {_lastMenuPlacement}.");
                    }
                    if (action == AvatarMenuAction.Settings)
                        _panel.OpenSettings(camera, _menuButton.PanelReference(camera));
                    else if (action == AvatarMenuAction.Calibrate)
                        OpenCalibration(camera);
                }
                catch (Exception exception)
                {
                    var error = $"{exception.GetType().Name}: {exception.Message}";
                    if (error != _lastMenuButtonError ||
                        Time.unscaledTime >= _nextMenuButtonErrorLog)
                    {
                        LoggerInstance.Error($"Avatar menu button update failed; retrying: {exception}");
                        _lastMenuButtonError = error;
                        _nextMenuButtonErrorLog = Time.unscaledTime + 30f;
                    }
                    _menuButton.ResetUi();
                    _menuButtonRetryAt = Time.unscaledTime + 5f;
                }
            }

            if (Time.unscaledTime >= _panelRetryAt)
            {
                try
                {
                    _panel.SetCameraGuideDetails(_captureCameraGuide.Status, _captureCameraGuide.Location);
                    _panel.SetExpressionContext(_config, _expressions, _expressionInput);
                    var command = _panel.Tick(camera, _tracking,
                        _avatar != null && _avatar.Animator != null && _avatar.Animator.gameObject.activeInHierarchy,
                        hasPoses, _config.Enabled,
                        _config.ShowCaptureCameraGuide,
                        _captureCameraGuide.HasCaptureCamera,
                        _captureCameraGuide.Error != null,
                        _config.DisableAvatarShadows, _config.DisableAvatarLights,
                        _avatarHmdVisibility.TryGet(out var visibleInHmd), visibleInHmd,
                        avatarMenuOpen, trigger,
                        leftPointer, rightPointer,
                        _calibrationEnd > 0f ? Mathf.Max(0f, _calibrationEnd - Time.unscaledTime) : -1f,
                        tPose);
                    HandlePanelCommand(command, camera);
                    if (_panel.ShowCalibrationPose)
                    {
                        var standing = TPoseCalibration.Check(camera, _panel.CalibrationForward,
                            hasHands, leftHand, rightHand, hasPoses, waist, left, right, false);
                        _avatar?.ShowCalibrationPose(camera, _panel.CalibrationForward, _panel.UseTPose,
                            standing.Ready, left, right);
                    }
                    else _avatar?.HideTPosePreview();
                    _panel.UpdateCalibrationMarkers(_avatar, hasPoses, waist, left, right,
                        hasHands, leftHand, rightHand);
                }
                catch (Exception exception)
                {
                    var error = $"{exception.GetType().Name}: {exception.Message}";
                    if (error != _lastPanelError || Time.unscaledTime >= _nextPanelErrorLog)
                    {
                        LoggerInstance.Error($"Settings panel update failed; retrying: {exception}");
                        _lastPanelError = error;
                        _nextPanelErrorLog = Time.unscaledTime + 30f;
                    }
                    _panel.ResetUi();
                    _avatar?.HideTPosePreview();
                    _calibrationEnd = 0f;
                    _calibrationSampler.Reset();
                    _panelRetryAt = Time.unscaledTime + 5f;
                }
            }

            if (!_hasPoseResult || hasPoses != _lastPoseReady)
            {
                LoggerInstance.Msg(hasPoses ? "Tracker poses available." : "Tracker poses unavailable.");
                _lastPoseReady = hasPoses;
                _hasPoseResult = true;
            }
            UpdateExpressions();
            if (_calibrationEnd > 0f)
            {
                if (!_panel.IsCalibrationCountingDown)
                {
                    _calibrationEnd = 0f;
                    _calibrationSampler.Reset();
                }
                else if (Time.unscaledTime >= _calibrationEnd)
                {
                    _calibrationEnd = 0f;
                    var stable = _calibrationSampler.TryGet(Time.unscaledTime, out var stableWaist,
                        out var stableLeft, out var stableRight, out var stabilityReason, out var stableAdditional);
                    var roleError = stable ? _avatar?.ValidateAdditionalRoles(stableAdditional) : null;
                    if (stable && _avatar != null && !_avatar.CalibrationSizeReady)
                        roleError ??= _avatar.CalibrationSizeStatus;
                    var saved = stable && _avatar != null && _tracking.TryGetCalibrationSpace(out var source, out var space)
                        ? _avatar.CaptureSession(stableWaist, stableLeft, stableRight, stableAdditional, source, space, camera) : null;
                    if (tPose.Warning != null)
                        LoggerInstance.Warning($"Calibration posture warning (non-blocking): {tPose.Warning}.");
                    if (hasPoses && stable && tPose.Ready && roleError == null &&
                        _avatar != null && _avatar.Animator != null &&
                        _avatar.Animator.gameObject.activeInHierarchy &&
                        _avatar.Calibrate(stableWaist, stableLeft, stableRight, camera, stableAdditional))
                    {
                        _avatar.HideTPosePreview();
                        _nextPoseDiagnostics = _nextAutomaticPoseDiagnostics = Time.unscaledTime + 2f;
                        _poseFilter.Reset();
                        _config.Enabled = true;
                        _calibrationRequired = false;
                        _sessionCalibration = saved;
                        _tracking.ConfirmAssignments();
                        TrySaveConfig();
                        _panel.FinishCalibration("Calibration complete", true, tPose.Warning);
                        LoggerInstance.Msg($"Full body calibration completed. Drive mode: {_avatar.DriveMode}.");
                        LogCalibrationDiagnostics(camera, frame, "completed");
                        LoggerInstance.Msg($"Tracker heights at calibration: waist={stableWaist.Position.y:0.00} m, " +
                            $"left={stableLeft.Position.y:0.00} m, right={stableRight.Position.y:0.00} m; " +
                            $"waist above feet={stableWaist.Position.y - (stableLeft.Position.y + stableRight.Position.y) * 0.5f:0.00} m.");
                        LoggerInstance.Msg($"Avatar rig: {_avatar.RigSummary}.");
                        LoggerInstance.Msg($"Calibrated tracker roles: waist={_tracking.AssignmentLabel(TrackerRole.Waist)}, " +
                            $"left ({TrackerRoles.Label(_tracking.PrimaryFoot(true))})={_tracking.AssignmentLabel(_tracking.PrimaryFoot(true))}, " +
                            $"right ({TrackerRoles.Label(_tracking.PrimaryFoot(false))})={_tracking.AssignmentLabel(_tracking.PrimaryFoot(false))}.");
                        foreach (var role in TrackerRoles.All.Where(_tracking.HasAssignment))
                            LoggerInstance.Msg($"Assigned {TrackerRoles.Label(role)}: {_tracking.AssignmentLabel(role)}.");
                        LoggerInstance.Msg($"Calibration reference pose: {_avatar.CalibrationPoseSource}.");
                        LoggerInstance.Msg($"Calibration size: {_avatar.CalibrationSizeStatus}.");
                        LoggerInstance.Msg($"Avatar fit: {_avatar.CalibrationFitSummary(camera, stableLeft, stableRight)}.");
                        if (_avatar.IkError != null) LoggerInstance.Warning(_avatar.IkError);
                    }
                    else
                    {
                        var reason = roleError ?? (!tPose.Ready
                            ? tPose.Status : !stable ? stabilityReason : _avatar?.IkError ?? "check devices/avatar");
                        _avatar?.HideTPosePreview();
                        _panel.FinishCalibration($"Calibration failed: {reason}", false, tPose.Warning);
                        LoggerInstance.Warning($"Full body calibration failed: {reason}.");
                        LogCalibrationDiagnostics(camera, frame, "failed");
                    }
                    _calibrationSampler.Reset();
                }
                else if (hasPoses && tPose.Ready)
                    _calibrationSampler.Add(Time.unscaledTime, camera.transform.position,
                        waist, left, right, _tracking.RolePoses);
                else _calibrationSampler.Reset();
            }

            var driveAvatar = !_calibrationRequired &&
                !_panel.ShowCalibrationPose &&
                _config.Enabled && frame.HasWaist && _avatar != null &&
                _avatar.Animator != null && _avatar.Animator.gameObject.activeInHierarchy;
            _avatar?.UpdateTrackingState(driveAvatar, frame.HasLeft, frame.HasRight);
            if (!driveAvatar && _avatar != null && _avatar.DriveMode != _lastDriveMode)
            {
                _lastDriveMode = _avatar.DriveMode;
                LoggerInstance.Msg($"Avatar drive mode: {_lastDriveMode}.");
            }
            if (!driveAvatar || _avatar == null) return;
            LogPoseDiagnostics(camera);
            if (frame.HasWaist)
            {
                if (_config.SmoothingEnabled)
                    _poseFilter.Filter(waist, left, right, frame.HasLeft, frame.HasRight,
                        Time.unscaledTime, _config,
                        out waist, out left, out right);
                else
                    _poseFilter.Reset();
                _avatar.Apply(waist, left, right, camera, frame.HasLeft, frame.HasRight, _tracking.RolePoses);
                if (_avatar.DriveMode != _lastDriveMode)
                {
                    _lastDriveMode = _avatar.DriveMode;
                    LoggerInstance.Msg($"Avatar drive mode: {_lastDriveMode}.");
                    if (_avatar.IkError != null) LoggerInstance.Warning(_avatar.IkError);
                }
            }
        }
        catch (Exception exception)
        {
            var error = $"{exception.GetType().Name}: {exception.Message}";
            ResetExpressions(false);
            if (error != _lastTrackingError || Time.unscaledTime >= _nextTrackingErrorLog)
            {
                LoggerInstance.Error($"Tracking update failed; retrying: {exception}");
                _lastTrackingError = error;
                _nextTrackingErrorLog = Time.unscaledTime + 30f;
            }
            _retryTrackingAt = Time.unscaledTime + 2f;
            _nextScan = 0f;
            _poseFilter.Reset();
            _calibrationSampler.Reset();
            _calibrationEnd = 0f;
            _lastDriveMode = "";
            _captureCameraGuide.Hide();
            _menuBustCamera.Update(null, null, false, false);
            _avatarRendering.Restore();
            try { _avatar?.Dispose(); }
            catch (Exception cleanupError)
            {
                LoggerInstance.Warning($"Avatar cleanup after tracking error failed: " +
                    $"{cleanupError.GetType().Name}: {cleanupError.Message}");
            }
            _avatar = null;
            _calibrationRequired = true;
            _recoveryPending = _sessionCalibration != null;
            _recoveryGate.Reset();
            _panel.ClearCalibrationResult();
            try { _panel.ResetUi(); }
            catch { /* Retry will recreate the panel if possible. */ }
            try { _menuButton.ResetUi(); }
            catch { /* Retry will recreate the menu button if possible. */ }
        }
    }

    /// <summary>
    /// UIが返した操作を設定・割り当て・校正状態へ反映します。
    /// </summary>
    /// <param name="command">UIが確定した操作要求。</param>
    /// <param name="camera">ゲーム内のHMD視点として使用するカメラ。</param>
    private void HandlePanelCommand(PanelCommand command, Camera camera)
    {
        if (_tracking == null || _config == null || command == PanelCommand.None) return;
        switch (command)
        {
            case PanelCommand.RepositionUi:
                _panel.Reposition(_menuButton.PanelReference(camera), camera);
                return;
            case PanelCommand.UiPreferencesChanged:
            case PanelCommand.HandRotationChanged:
                TrySaveConfig();
                return;
            case PanelCommand.ExpressionsChanged:
                ResetExpressions(false);
                TrySaveConfig();
                return;
            case PanelCommand.AssignmentChanged: break;
            case PanelCommand.Recalibrate:
                OpenCalibration(camera);
                return;
            case PanelCommand.PrepareCalibration:
                InvalidateCalibration();
                _panel.SelectCalibrationPose();
                return;
            case PanelCommand.StartCalibration:
                if (!_panel.BeginCalibrationCountdown()) return;
                InvalidateCalibration(false);
                if (!_tracking.CaptureWorldAnchor(camera))
                {
                    _panel.FinishCalibration("Calibration failed: HMD pose unavailable", false);
                    return;
                }
                _avatar?.RefitCalibrationSize();
                _calibrationEnd = Time.unscaledTime + (_panel.UseTPose ? 5f : 3f);
                LoggerInstance.Msg($"Full body {(_panel.UseTPose ? "T-pose" : "standing")} calibration countdown started.");
                LogCalibrationDiagnostics(camera, null, "started");
                return;
            case PanelCommand.TogglePoseMode:
                _panel.TogglePoseMode();
                InvalidateCalibration(false);
                LoggerInstance.Msg($"Calibration pose: {(_panel.UseTPose ? "T-pose" : "standing")}.");
                return;
            case PanelCommand.ToggleTracking: _config.Enabled = !_config.Enabled; break;
            case PanelCommand.ToggleUltimateRotation:
                _config.UltimateTrackerRotate180 = !_config.UltimateTrackerRotate180;
                InvalidateCalibration();
                LoggerInstance.Msg($"Ultimate Tracker 180 correction: {(_config.UltimateTrackerRotate180 ? "ON" : "OFF")}; recalibration required.");
                break;
            case PanelCommand.ToggleCameraGuide:
                _config.ShowCaptureCameraGuide = !_config.ShowCaptureCameraGuide;
                _captureCameraGuide.RequestRescan();
                break;
            case PanelCommand.ToggleMenuBustShot:
                _config.MenuBustShot = !_config.MenuBustShot;
                _menuBustCamera.Update(camera, _avatar?.Animator, _config.MenuBustShot, _panel.ShowCalibrationPose || _recoveryPending);
                break;
            case PanelCommand.ToggleAvatarShadows:
                _config.DisableAvatarShadows = !_config.DisableAvatarShadows; break;
            case PanelCommand.ToggleAvatarLights:
                _config.DisableAvatarLights = !_config.DisableAvatarLights; break;
            case PanelCommand.ToggleAvatarInHeadset:
                if (_avatarHmdVisibility.TryToggle(_config))
                {
                    TrySaveConfig();
                    LoggerInstance.Msg($"VRM headset visibility applied. {_avatarHmdVisibility.LastApplication}");
                }
                else LoggerInstance.Warning("VRM headset visibility could not be changed; " +
                    _avatarHmdVisibility.Error);
                return;
            case PanelCommand.CancelCalibration:
            case PanelCommand.Close:
                _calibrationEnd = 0f;
                _calibrationSampler.Reset();
                _avatar?.HideTPosePreview();
                // The permanent entry buttons have their own visibility lifetime.
                return;
        }
        if (command == PanelCommand.AssignmentChanged)
        {
            InvalidateCalibration();
            LoggerInstance.Msg($"Tracker assignment changed: {_tracking.AssignmentStatus}.");
            foreach (var role in TrackerRoles.All.Where(_tracking.HasAssignment))
                LoggerInstance.Msg($"Assigned {TrackerRoles.Label(role)}: {_tracking.AssignmentLabel(role)}.");
        }
        TrySaveConfig();
    }

    /// <summary>表情APIと同じ個体の入力を準備し、失敗を全身追跡から隔離します。</summary>
    private void PrepareExpressions()
    {
        if (_tracking == null || Time.unscaledTime < _retryExpressionsAt) return;
        try
        {
            _expressions.Refresh(_avatarHmdVisibility.GetActiveAvatarRoot() ?? _avatar?.Animator?.gameObject);
            _expressionInput = _tracking.ReadExpressionButtons();
            var diagnostic = $"sources={_expressionInput.Source}; available(left/right)=" +
                $"{_expressionInput.Available & 7}/{(_expressionInput.Available >> 3) & 7}";
            if (diagnostic != _expressionDiagnostic && Time.unscaledTime >= _nextExpressionDiagnostic)
            {
                LoggerInstance.Msg("Expression input: " + diagnostic + "; bits=primary:1 secondary:2 grip:4.");
                _expressionDiagnostic = diagnostic;
                _nextExpressionDiagnostic = Time.unscaledTime + 5f;
            }
        }
        catch (Exception exception) { ExpressionError(exception); }
    }

    /// <summary>校正や他の設定画面を除き、ボタンによる表情を更新します。フルトラ校正は不要です。</summary>
    private void UpdateExpressions()
    {
        if (_config == null || Time.unscaledTime < _retryExpressionsAt) return;
        try
        {
            _expressions.Update(_expressionInput, _config,
                _calibrationEnd <= 0 && (!_panel.IsOpen || _panel.IsExpressionScreen));
        }
        catch (Exception exception) { ExpressionError(exception); }
    }

    /// <summary>表情処理の失敗を記録し、参照を解放して一定時間後に再検出します。</summary>
    private void ExpressionError(Exception exception)
    {
        LoggerInstance.Warning($"Expression control unavailable; retrying: {exception.GetType().Name}: {exception.Message}");
        ResetExpressions(true);
        _retryExpressionsAt = Time.unscaledTime + 5f;
    }

    /// <summary>表情を解除します。破棄済みアバターの例外を他の機能へ伝えません。</summary>
    private void ResetExpressions(bool releaseAvatar)
    {
        try
        {
            if (releaseAvatar) _expressions.Dispose();
            else _expressions.Reset();
        }
        catch { /* Avatar teardown can destroy the native expression component first. */ }
        _expressionInput = default;
    }

    /// <summary>
    /// 校正の開始・結果時に座標と割り当てを記録し、診断処理の失敗は校正へ波及させません。
    /// </summary>
    /// <param name="camera">校正に使用したHMDカメラ。</param>
    /// <param name="sampledFrame">姿勢判定と同じ更新で読み取った身体入力。nullなら診断処理内で取得します。</param>
    /// <param name="stage">started、failed、completedなどの記録時点。</param>
    private void LogCalibrationDiagnostics(Camera camera, TrackerFrame? sampledFrame, string stage)
    {
        try
        {
            var frame = sampledFrame ?? _tracking?.ReadWorldPoses(camera) ?? default;
            LoggerInstance.Msg($"Calibration check [{stage}]: mode={(_panel.UseTPose ? "T-pose" : "standing")}; " +
                $"front source={_panel.CalibrationDirectionSource}; " +
                $"tracked waist/left/right={frame.HasWaist}/{frame.HasLeft}/{frame.HasRight}; " +
                TPoseCalibration.Diagnostics(camera, _panel.CalibrationForward, frame));
            LoggerInstance.Msg($"Calibration tracking [{stage}]:\n{_tracking?.CalibrationDiagnostics(camera)}");
        }
        catch (Exception exception)
        {
            LoggerInstance.Warning($"Calibration diagnostics unavailable: {exception.Message}");
        }
    }

    /// <summary>UIを閉じた曲選択・プレイ中も姿勢を定期記録し、計測失敗で追跡を停止させません。</summary>
    /// <param name="camera">現在のHMDカメラ。描画姿勢とMOD更新時の姿勢はログで区別します。</param>
    private void LogPoseDiagnostics(Camera camera)
    {
        var now = Time.unscaledTime;
        var panelDue = _panel.IsOpen && now >= _nextPoseDiagnostics;
        if (_avatar == null || (!panelDue && now < _nextAutomaticPoseDiagnostics)) return;
        _nextPoseDiagnostics = now + 2.5f;
        _nextAutomaticPoseDiagnostics = now + 10f;
        try
        {
            // The current animation pose is separate from the captured post-solve pose.
            var diagnostics = _avatar.PoseDiagnostics(camera);
            if (diagnostics == null) return;
            LoggerInstance.Msg($"Avatar pose diagnostics: capture={(panelDue ? "panel" : "automatic")}; {diagnostics}.");
            LoggerInstance.Msg($"Foot pose window: {_avatar.TakeFootPoseWindow()}.");
        }
        catch (Exception exception)
        {
            if (now < _nextPoseDiagnosticsError) return;
            _nextPoseDiagnosticsError = now + 30f;
            LoggerInstance.Warning($"Pose diagnostics unavailable; tracking continues: {exception.Message}");
        }
    }

    /// <summary>
    /// 保存した校正と追跡原点を破棄し、明示的な再校正が必要な状態へ戻します。
    /// </summary>
    /// <param name="resetPanel">外部変更時は準備画面へ戻します。明示的な計測開始・姿勢選択時は段階を保持します。</param>
    private void InvalidateCalibration(bool resetPanel = true)
    {
        _sessionCalibration = null;
        _recoveryPending = _rebaseOnRecovery = false;
        _recoveryGate.Reset();
        _tracking?.ClearWorldAnchor();
        _calibrationRequired = true;
        _calibrationEnd = 0f;
        _calibrationSampler.Reset();
        _poseFilter.Reset();
        if (resetPanel) _panel.ClearCalibrationResult();
        _avatar?.ResetCalibration(true);
    }

    /// <summary>
    /// 元の校正を保持したままIKを停止し、シーンや接続変更からの復帰を予約します。
    /// </summary>
    /// <param name="reason">復帰を予約するきっかけとなった状態変化の説明。</param>
    /// <param name="rebase">復帰時にHMDから追跡原点を取り直す必要があるか。</param>
    private void QueueCalibrationRecovery(string reason, bool rebase)
    {
        if (_sessionCalibration == null) { InvalidateCalibration(); return; }
        _recoveryPending = true;
        _rebaseOnRecovery |= rebase;
        _calibrationRequired = true;
        _calibrationEnd = 0f;
        _calibrationSampler.Reset();
        _poseFilter.Reset();
        _recoveryGate.Reset();
        _nextRecoveryAttempt = Time.unscaledTime + 0.75f;
        _panel.ClearCalibrationResult();
        try { _avatar?.HideTPosePreview(); _avatar?.ResetCalibration(true); }
        catch { /* New rig will be found after scene teardown. */ }
        LoggerInstance.Msg($"Full body recovery queued: {reason}; keeping original calibration.");
    }

    /// <summary>
    /// 骨格とサイズの安定を待ち、必要に応じて追跡原点を取り直して保存校正を復帰します。
    /// </summary>
    /// <param name="camera">ゲーム内のHMD視点として使用するカメラ。</param>
    private void TryRecoverCalibration(Camera camera)
    {
        if (!_recoveryPending || _sessionCalibration == null || _tracking == null ||
            Time.unscaledTime < _nextRecoveryAttempt) return;
        if (_avatar == null || !_avatar.ReadyForRecovery) { _recoveryGate.Reset(); return; }
        var scale = _avatar.Animator.transform.lossyScale;
        var identity = $"{_avatar.Animator.GetInstanceID()}:{_avatar.IkComponent!.solver.Pointer}";
        if (!_recoveryGate.Ready(identity, new(scale.x, scale.y, scale.z), Time.unscaledTime)) return;
        if (!_avatar.CanRestore(_sessionCalibration, out var reason))
        {
            LoggerInstance.Warning($"Full body recovery requires manual calibration: {reason}.");
            InvalidateCalibration();
            return;
        }
        if (_rebaseOnRecovery)
        {
            if (!_tracking.RebaseWorldAnchors(camera)) return;
        }
        if (!_tracking.TryGetSpace(_sessionCalibration.SpaceSource, out var space) ||
            !_tracking.ReadWorldPoses(camera).HasWaist) return;
        _nextRecoveryAttempt = Time.unscaledTime + 2f;
        if (!_avatar.RestoreSession(_sessionCalibration, space, camera))
        {
            LoggerInstance.Warning($"Full body recovery waiting: {_avatar.IkError ?? "rig not ready"}.");
            return;
        }
        _recoveryPending = _rebaseOnRecovery = false;
        _calibrationRequired = false;
        _poseFilter.Reset();
        _nextPoseDiagnostics = _nextAutomaticPoseDiagnostics = Time.unscaledTime + 2f;
        LoggerInstance.Msg($"Full body calibration restored: original scale={_sessionCalibration.Scale}, " +
            $"current scale={_avatar.Animator.transform.lossyScale}; original pose and tracker offsets reused.");
    }

    /// <summary>
    /// 固定した正面・パネル位置で準備画面を開きます。姿勢選択へ進むまでは既存の校正を維持します。
    /// </summary>
    /// <param name="camera">ゲーム内のHMD視点として使用するカメラ。</param>
    private void OpenCalibration(Camera camera)
    {
        // Merely checking readiness must not discard a working calibration.
        _avatar?.HideTPosePreview();
        _calibrationEnd = 0f;
        _calibrationSampler.Reset();
        _panel.OpenCalibration(camera, _menuButton.PanelReference(camera));
        LoggerInstance.Msg($"Calibration front: {_panel.CalibrationDirectionSource}; " +
            $"direction={_panel.CalibrationForward}.");
    }

    /// <summary>
    /// 設定の保存を試み、失敗時は現在のメモリー上の設定を維持して間隔を空けて警告します。
    /// </summary>
    private void TrySaveConfig()
    {
        if (_configPath == null || _config == null) return;
        try { _config.Save(_configPath); }
        catch (Exception exception)
        {
            if (Time.unscaledTime < _nextConfigErrorLog) return;
            _nextConfigErrorLog = Time.unscaledTime + 30f;
            LoggerInstance.Warning($"Configuration could not be saved to {_configPath}: " +
                $"{exception.GetType().Name}: {exception.Message}. Settings remain active " +
                "until the game exits.");
        }
    }

    /// <summary>
    /// AvatarLoaderのHMD、稼働中のステレオカメラの順で視点を探します。未検出時に配信カメラをHMDとして使いません。
    /// </summary>
    /// <returns>利用できるカメラ。見つからない場合はnull。</returns>
    private static Camera? FindHeadCamera()
    {
        var loader = AvatarLoader.s_instance;
        var headset = loader != null && loader.gameObject.activeInHierarchy
            ? loader.m_HMDCamera : null;
        if (headset != null && headset.gameObject.activeInHierarchy) return headset;
        foreach (var camera in Camera.allCameras)
            if (camera != null && camera.stereoEnabled && camera.gameObject.activeInHierarchy)
                return camera;
        return null;
    }
}
