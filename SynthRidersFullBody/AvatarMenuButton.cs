using Il2CppSynth.Avatars;
using Il2CppUtil.Controller;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SynthRidersFullBody;

/// <summary>
/// アバター画面の常設ボタンから要求される操作です。
/// </summary>
internal enum AvatarMenuAction { 
    /// <summary>
    /// 操作要求なし。
    /// </summary>
    None, 
    /// <summary>
    /// 全身設定画面を開く要求。
    /// </summary>
    Settings, 
    /// <summary>
    /// 校正画面を開く要求。
    /// </summary>
    Calibrate }

/// <summary>
/// アバター選択画面のスコア表示位置を基準に、設定・校正用の独立した入口ボタンを管理します。
/// </summary>
internal sealed class AvatarMenuButton
{
    private GameObject? _root;
    private Image? _settingsBackground;
    private Image? _calibrationBackground;
    private bool _previousTrigger = true;
    private bool _previousLeftTrigger = true;
    private bool _previousRightTrigger = true;
    private AvatarLoader? _loader;
    private Transform? _anchor;
    private LeaderboardManagementController? _leaderboard;
    private bool _wasVisible;
    private float _nextScan;
    private float _nextScoreScan;
    /// <summary>
    /// 入口ボタンの配置に使用した基準を示す診断文です。
    /// </summary>
    public string Placement { get; private set; } = "waiting for avatar menu";
    /// <summary>
    /// 入口の方向とHMD位置から、詳細パネルを置くワールド姿勢を求めます。
    /// </summary>
    /// <param name="camera">ゲーム内のHMD視点として使用するカメラ。</param>
    /// <returns>入口を基準にした配置。入口がなければnull。</returns>
    public Pose? PanelPose(Camera camera)
    {
        if (_root == null || !_root.activeSelf) return null;
        var eye = camera.transform.position;
        var entry = _root.transform.position;
        var near = VrPanelLayout.DetailPosition(new(eye.x, eye.y, eye.z), new(entry.x, entry.y, entry.z),
            _root.transform.localScale.x);
        var position = new Vector3(near.X, near.Y, near.Z);
        var forward = Vector3.ProjectOnPlane(position - eye, Vector3.up).normalized;
        return new Pose(position, Quaternion.LookRotation(forward, Vector3.up));
    }

    /// <summary>
    /// アバター選択画面の有効なアンカーが存在するかを確認します。
    /// </summary>
    /// <returns>選択画面が開いていればtrue。</returns>
    public bool IsAvatarMenuOpen() => FindAnchor() != null;

    /// <summary>
    /// 常設ボタンを隠し、押し続けた入力による再操作を防ぐ状態へ戻します。
    /// </summary>
    public void Hide()
    {
        if (_root != null) _root.SetActive(false);
        _wasVisible = false;
        _anchor = null;
        _previousTrigger = _previousLeftTrigger = _previousRightTrigger = true;
    }

    /// <summary>
    /// 入口UIを破棄し、参照と入力・配置のキャッシュを初期化します。
    /// </summary>
    public void ResetUi()
    {
        if (_root != null)
        {
            try
            {
                _root.SetActive(false);
                UnityEngine.Object.Destroy(_root);
            }
            catch { /* The Unity object may already have been destroyed. */ }
        }
        _root = null;
        _settingsBackground = null;
        _calibrationBackground = null;
        _loader = null;
        _leaderboard = null;
        _anchor = null;
        _wasVisible = false;
        _nextScan = 0f;
        _nextScoreScan = 0f;
        _previousTrigger = true;
        _previousLeftTrigger = true;
        _previousRightTrigger = true;
    }

    /// <summary>
    /// 入口の表示・固定位置・ポインター選択を更新し、新たなボタン操作を返します。
    /// </summary>
    /// <param name="camera">ゲーム内のHMD視点として使用するカメラ。</param>
    /// <param name="trigger">いずれかの手のトリガーが押されているか。</param>
    /// <param name="leftPointer">左手のポインター姿勢と同じ個体のトリガー状態。</param>
    /// <param name="rightPointer">右手のポインター姿勢と同じ個体のトリガー状態。</param>
    /// <param name="panel">入口ボタンとの遮蔽・入力重複を判定する詳細パネル。</param>
    /// <param name="calibrationRequired">ユーザーによる校正を必要としているか。</param>
    /// <returns>このフレームで確定した操作。操作がなければNone。</returns>
    public AvatarMenuAction Tick(Camera camera, bool trigger, ControllerPointer leftPointer,
        ControllerPointer rightPointer, SettingsPanel panel, bool calibrationRequired)
    {
        EnsureCreated();
        if (_root == null) return AvatarMenuAction.None;
        var useGaze = !leftPointer.HasPose && !rightPointer.HasPose;
        var gazeClicked = useGaze && trigger && !_previousTrigger;
        var leftClicked = leftPointer.Trigger && !_previousLeftTrigger;
        var rightClicked = rightPointer.Trigger && !_previousRightTrigger;
        _previousTrigger = trigger;
        _previousLeftTrigger = leftPointer.Trigger;
        _previousRightTrigger = rightPointer.Trigger;

        var menuAnchor = FindAnchor();
        var scorePanel = menuAnchor == null ? null : FindScoreboard();
        var anchor = scorePanel != null ? scorePanel.transform : menuAnchor;
        // These are independent entry buttons. Only read the native panel's
        // location; never parent to, hide, or change its display or input.
        var visible = menuAnchor != null;
        _root.SetActive(visible);
        if (!visible)
        {
            _wasVisible = false;
            _anchor = null;
            return AvatarMenuAction.None;
        }
        if (!_wasVisible || _anchor != anchor)
        {
            // A press that opens the native avatar menu must not also activate
            // a newly visible entry button under the same controller ray.
            gazeClicked = leftClicked = rightClicked = false;
            var position = anchor!.position;
            var rect = anchor.gameObject.GetComponent<RectTransform>();
            if (scorePanel != null)
            {
                if (rect != null)
                    position = rect.TransformPoint(new Vector3(rect.rect.center.x, rect.rect.center.y, 0f));
                var forward = anchor.forward;
                var up = anchor.up;
                if (Vector3.Dot(forward, position - camera.transform.position) < 0f) forward = -forward;
                _root.transform.SetPositionAndRotation(position - forward * 0.02f,
                    Quaternion.LookRotation(forward, up));
                Placement = $"independent buttons at score location: {scorePanel.name}";
            }
            else
            {
                // Compatibility fallback for game versions without the native
                // scoreboard controller. Still fixed for this menu opening.
                position += anchor.right * 0.78f;
                if (rect != null)
                {
                    var bounds = rect.rect;
                    var rightCenter = rect.TransformPoint(new Vector3(bounds.xMax, bounds.center.y, 0f));
                    if (Vector3.Distance(rightCenter, camera.transform.position) is > 0.3f and < 4f)
                        position = rightCenter + anchor.right * 0.35f;
                }
                var horizontal = Vector3.ProjectOnPlane(anchor.forward, Vector3.up);
                if (horizontal.sqrMagnitude < 0.01f) horizontal = Vector3.forward;
                if (Vector3.Dot(horizontal, position - camera.transform.position) < 0f) horizontal = -horizontal;
                _root.transform.SetPositionAndRotation(position,
                    Quaternion.LookRotation(horizontal, Vector3.up));
                Placement = "avatar menu side (scoreboard unavailable)";
            }
            _anchor = anchor;
            _root.transform.localScale = Vector3.one * VrPanelLayout.EntryScale(
                Vector3.Distance(_root.transform.position, camera.transform.position));
            _wasVisible = true;
        }
        var gazeAction = useGaze ? HitAction(new Ray(camera.transform.position,
            camera.transform.forward), panel) : AvatarMenuAction.None;
        var leftAction = leftPointer.HasPose ? HitAction(new Ray(leftPointer.Pose.Position,
            leftPointer.Pose.Rotation * Vector3.forward), panel) : AvatarMenuAction.None;
        var rightAction = rightPointer.HasPose ? HitAction(new Ray(rightPointer.Pose.Position,
            rightPointer.Pose.Rotation * Vector3.forward), panel) : AvatarMenuAction.None;
        var settingsFocused = gazeAction == AvatarMenuAction.Settings ||
            leftAction == AvatarMenuAction.Settings || rightAction == AvatarMenuAction.Settings;
        var calibrationFocused = gazeAction == AvatarMenuAction.Calibrate ||
            leftAction == AvatarMenuAction.Calibrate || rightAction == AvatarMenuAction.Calibrate;
        if (_settingsBackground != null)
            _settingsBackground.color = settingsFocused
                ? new Color(0.12f, 0.70f, 0.81f, 0.98f)
                : new Color(0.23f, 0.13f, 0.48f, 0.98f);
        if (_calibrationBackground != null)
            _calibrationBackground.color = calibrationFocused
                ? new Color(1f, 0.23f, 0.84f, 0.98f)
                : calibrationRequired
                    ? new Color(0.70f, 0.06f, 0.58f, 0.98f)
                    : new Color(0.34f, 0.14f, 0.56f, 0.98f);
        if (rightClicked && rightAction != AvatarMenuAction.None) return rightAction;
        if (leftClicked && leftAction != AvatarMenuAction.None) return leftAction;
        return gazeClicked ? gazeAction : AvatarMenuAction.None;
    }

    /// <summary>
    /// 有効なAvatarLoaderのメニューまたはアバター一覧を、入口の表示判定用に取得します。
    /// </summary>
    /// <returns>開いているメニューのアンカー。なければnull。</returns>
    private Transform? FindAnchor()
    {
        if (_loader == null || Time.unscaledTime >= _nextScan)
        {
            _nextScan = Time.unscaledTime + 1f;
            _loader = null;
            var activeLoader = AvatarLoader.s_instance;
            if (activeLoader != null && activeLoader.gameObject.activeInHierarchy)
                _loader = activeLoader;
            if (_loader == null)
            {
                foreach (var loader in Resources.FindObjectsOfTypeAll<AvatarLoader>())
                    if (loader != null && loader.gameObject.activeInHierarchy)
                    {
                        _loader = loader;
                        break;
                    }
            }
        }

        Transform? anchor = null;
        if (_loader != null && _loader.gameObject.activeInHierarchy)
        {
            var panel = _loader.m_menuWrapper;
            if (panel != null && panel.activeInHierarchy)
            {
                var grid = _loader.m_GridContainer;
                anchor = grid != null && grid.activeInHierarchy ? grid.transform : panel.transform;
            }
        }

        return anchor;
    }

    /// <summary>
    /// 独立したボタンの配置基準に使えるゲームのスコア領域を探します。
    /// </summary>
    /// <returns>スコア領域のGameObject。共有ラッパーしかない場合などはnull。</returns>
    private GameObject? FindScoreboard()
    {
        if (_leaderboard == null && Time.unscaledTime >= _nextScoreScan)
        {
            _nextScoreScan = Time.unscaledTime + 2f;
            _leaderboard = LeaderboardManagementController.s_instance;
            if (_leaderboard == null)
                foreach (var candidate in Resources.FindObjectsOfTypeAll<LeaderboardManagementController>())
                    if (candidate != null && candidate.gameObject.scene.IsValid())
                    {
                        _leaderboard = candidate;
                        break;
                    }
        }
        if (_leaderboard == null) return null;
        var panel = _leaderboard.globalLeaderbaordContainer;
        // Reject a shared wrapper: its center would not identify the score area.
        if (panel != null && _loader?.m_menuWrapper != null &&
            _loader.m_menuWrapper.transform.IsChildOf(panel.transform)) return null;
        return panel;
    }

    /// <summary>
    /// 入口のCanvasと2つのボタンを、未作成の場合に生成します。
    /// </summary>
    private void EnsureCreated()
    {
        if (_root != null) return;
        _root = VrUi.CreateCanvas("SynthRidersFullBody.AvatarMenuButton",
            new Vector2(500f, 260f), 0.001f);
        _settingsBackground = VrUi.Image(_root, "SettingsButton",
            new Vector2(470f, 100f), new Vector2(0f, 65f),
            new Color(0.23f, 0.13f, 0.48f, 0.98f));
        VrUi.HexOutline(_root, "SettingsFrame", new Vector2(475f, 105f),
            new Vector2(0f, 65f), new Color(0.12f, 0.82f, 0.95f, 0.95f), 3f, 42f);
        VrUi.Text(_root, "SettingsLabel", new Vector2(450f, 86f),
            new Vector2(0f, 65f), 32f, Color.white,
            TextAlignmentOptions.Center).text = "FULL BODY SETTINGS";
        _calibrationBackground = VrUi.Image(_root, "CalibrationButton",
            new Vector2(470f, 100f), new Vector2(0f, -65f),
            new Color(0.70f, 0.06f, 0.58f, 0.98f));
        VrUi.HexOutline(_root, "CalibrationFrame", new Vector2(475f, 105f),
            new Vector2(0f, -65f), new Color(1f, 0.40f, 0.89f, 0.95f), 3f, 42f);
        VrUi.Text(_root, "CalibrationLabel", new Vector2(450f, 86f),
            new Vector2(0f, -65f), 32f, Color.white,
            TextAlignmentOptions.Center).text = "CALIBRATE FULL BODY";
        _root.SetActive(false);
    }

    /// <summary>
    /// 詳細パネルによる遮蔽を考慮して、レイが指す入口ボタンを判定します。
    /// </summary>
    /// <param name="ray">ワールド空間の入力レイ。</param>
    /// <param name="panel">入口ボタンとの遮蔽・入力重複を判定する詳細パネル。</param>
    /// <returns>レイが有効なボタンに当たった場合の操作。それ以外はNone。</returns>
    private AvatarMenuAction HitAction(Ray ray, SettingsPanel panel)
    {
        if (_root == null) return AvatarMenuAction.None;
        var plane = new Plane(_root.transform.forward, _root.transform.position);
        if (!plane.Raycast(ray, out var distance) || distance > VrUi.InteractionDistance || panel.OccludesRay(ray, distance))
            return AvatarMenuAction.None;
        var point = _root.transform.InverseTransformPoint(ray.GetPoint(distance));
        if (Mathf.Abs(point.x) > 235f) return AvatarMenuAction.None;
        if (Mathf.Abs(point.y - 65f) <= 50f) return AvatarMenuAction.Settings;
        if (Mathf.Abs(point.y + 65f) <= 50f) return AvatarMenuAction.Calibrate;
        return AvatarMenuAction.None;
    }
}
