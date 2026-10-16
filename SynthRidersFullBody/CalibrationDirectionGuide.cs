using Il2CppSynth.Avatars;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SynthRidersFullBody;

// A fixed direction, independent of the head direction used to point at UI.
/// <summary>
/// 校正開始時の正面を固定し、鏡・床矢印・視線ターゲット・カウントダウンを管理します。
/// </summary>
internal sealed class CalibrationDirectionGuide
{
    private GameObject? _floor;
    private GameObject? _sign;
    private TextMeshProUGUI? _label;
    private TextMeshProUGUI? _countdown;
    private TextMeshProUGUI? _hint;
    private Image? _focus;
    private Image? _progress;
    private GameObject? _openedMirror;
    private GameObject? _mirror;
    private AvatarLoader? _mirrorLoader;
    private bool _changedNativeMirror;
    private Vector3 _origin;
    private Vector3 _forward;
    private bool _hasFloor;
    /// <summary>
    /// 固定した正面方向をゲームの鏡から求めたかを示します。
    /// </summary>
    public bool UsesMirror { get; private set; }
    /// <summary>
    /// 正面方向の取得元をUI表示用の短い文字列で示します。
    /// </summary>
    public string Source => UsesMirror ? "MIRROR" : "ARROW";

    /// <summary>
    /// 鏡または開始時の水平視線から正面を決め、ユーザーの位置を基準にガイドを固定します。
    /// </summary>
    /// <param name="camera">ゲーム内のHMD視点として使用するカメラ。</param>
    /// <returns>ワールド空間の固定された水平正面方向。</returns>
    public Vector3 Begin(Camera camera)
    {
        Hide();
        EnsureCreated();
        _origin = camera.transform.position;
        _hasFloor = false;
        UsesMirror = false;
        _mirror = null;
        _mirrorLoader = null;
        _forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
        try
        {
            // The reference is available even when Toggle mirror hides it.
            var loader = AvatarLoader.s_instance;
            var mirror = loader?.avatarMirror;
            if (mirror != null)
            {
                var direction = Vector3.ProjectOnPlane(mirror.transform.position - _origin, Vector3.up);
                if (direction.sqrMagnitude > 0.04f)
                {
                    _forward = direction.normalized;
                    UsesMirror = true;
                    _mirror = mirror.gameObject;
                    _mirrorLoader = loader;
                    ShowMirror();
                }
            }
        }
        catch { /* Show an explicit fixed arrow if this game version has no usable mirror. */ }
        if (_forward.sqrMagnitude < 0.01f) _forward = Vector3.forward;
        _sign!.transform.SetPositionAndRotation(_origin + _forward * 1.2f,
            Quaternion.LookRotation(_forward, Vector3.up));
        UiLocalization.Bind(_label!, UsesMirror ? "FRONT = MIRROR" : "FRONT = THIS ARROW");
        return _forward;
    }

    /// <summary>
    /// 姿勢判定と残り時間を目線の高さに表示し、足元ガイドと鏡の表示を更新します。
    /// </summary>
    /// <param name="visible">対象表示を有効にするか。</param>
    /// <param name="hasTrackers">校正に必要な腰・左右足の追跡姿勢が有効か。</param>
    /// <param name="left">左足のワールド姿勢。</param>
    /// <param name="right">右足のワールド姿勢。</param>
    /// <param name="countdown">残り秒数。負数はカウントダウンしていない状態です。</param>
    /// <param name="tPose">腕を水平に広げるTポーズを使用するか。falseはStandingです。</param>
    /// <param name="assessment">現在の校正姿勢の判定結果。</param>
    /// <param name="keepMirror">校正用にゲームの鏡を開いた状態を維持するか。</param>
    public void Update(bool visible, bool hasTrackers, Pose left, Pose right, float countdown,
        bool tPose, TPoseAssessment assessment, bool keepMirror)
    {
        if (_sign == null || _floor == null) return;
        _sign.SetActive(visible);
        if (keepMirror) ShowMirror(); else RestoreMirror();
        if (!visible) { _floor.SetActive(false); return; }
        if (visible && !_hasFloor && hasTrackers)
        {
            var position = _origin + _forward * 0.55f;
            position.y = Mathf.Min(left.Position.y, right.Position.y) + 0.015f;
            _floor.transform.SetPositionAndRotation(position,
                Quaternion.LookRotation(Vector3.down, _forward));
            _hasFloor = true;
        }
        _floor.SetActive(visible && _hasFloor);
        _countdown!.text = countdown >= 0f
            ? $"{Mathf.CeilToInt(countdown)}   {UiText.Get(tPose ? "HOLD T-POSE" : "HOLD STILL")}" : UiText.Get("FACE THIS WAY");
        _hint!.text = countdown >= 0f && !assessment.Ready ? UiText.Status(assessment.Status) :
            countdown >= 0f && assessment.Warning != null ? UiText.Format("WARNING: {0}", UiText.Status(assessment.Warning)) : UiText.Get("LOOK AT THE DOT");
        var color = countdown >= 0f && (!assessment.Ready || assessment.Warning != null) ? new Color(1f, 0.65f, 0.2f) :
            new Color(0.06f, 0.87f, 0.94f);
        _focus!.color = color;
        _focus.rectTransform.localScale = Vector3.one *
            (countdown >= 0f ? 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 5f) : 1f);
        _progress!.rectTransform.sizeDelta = new Vector2(
            640f * (countdown < 0f ? 0f : Mathf.Clamp01(1f - countdown / (tPose ? 5f : 3f))), 5f);
    }

    /// <summary>
    /// 正面ガイドを隠し、MODが開いた鏡の表示状態を元へ戻します。
    /// </summary>
    public void Hide()
    {
        if (_floor != null) _floor.SetActive(false);
        if (_sign != null) _sign.SetActive(false);
        RestoreMirror();
    }

    /// <summary>
    /// ゲームの鏡を必要な場合だけ開き、後で元へ戻せるよう変更を記録します。
    /// </summary>
    private void ShowMirror()
    {
        if (_mirrorLoader != null && OptionalMember.Read(_mirrorLoader, "showMirror") is false &&
            OptionalMember.TrySetBooleanState(_mirrorLoader, "ChangeAvatarMirrorState", true))
            _changedNativeMirror = true;
        if (_mirror == null || _mirror.activeSelf) return;
        _openedMirror = _mirror;
        _mirror.SetActive(true);
    }

    /// <summary>
    /// MODが変更した鏡の状態だけを元に戻します。
    /// </summary>
    private void RestoreMirror()
    {
        if (_changedNativeMirror && _mirrorLoader != null)
            OptionalMember.TrySetBooleanState(_mirrorLoader, "ChangeAvatarMirrorState", false);
        _changedNativeMirror = false;
        if (_openedMirror != null)
        {
            try { _openedMirror.SetActive(false); } catch { /* Scene teardown. */ }
            _openedMirror = null;
        }
    }

    /// <summary>
    /// ガイドと鏡の変更を解除し、作成した表示オブジェクトを破棄します。
    /// </summary>
    public void Dispose()
    {
        Hide();
        if (_floor != null) UnityEngine.Object.Destroy(_floor);
        if (_sign != null) UnityEngine.Object.Destroy(_sign);
        _floor = _sign = null;
        _mirror = null;
        _mirrorLoader = null;
        _label = _countdown = _hint = null;
        _focus = _progress = null;
    }

    /// <summary>
    /// 床矢印と目線の高さに表示する校正案内を、必要な場合に作成します。
    /// </summary>
    private void EnsureCreated()
    {
        if (_floor != null && _sign != null) return;
        Dispose();
        var cyan = new Color(0.06f, 0.87f, 0.94f, 0.95f);
        _floor = VrUi.CreateCanvas("SynthRidersFullBody.CalibrationFrontArrow", new Vector2(400f, 550f), 0.001f);
        VrUi.Image(_floor, "Shaft", new Vector2(22f, 260f), new Vector2(0f, 10f), cyan);
        var left = VrUi.Image(_floor, "ArrowLeft", new Vector2(22f, 140f), new Vector2(-45f, 90f), cyan);
        var right = VrUi.Image(_floor, "ArrowRight", new Vector2(22f, 140f), new Vector2(45f, 90f), cyan);
        left.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f);
        right.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        UiLocalization.Bind(VrUi.Text(_floor, "Front", new Vector2(380f, 60f), new Vector2(0f, -175f),
            44f, Color.white, TextAlignmentOptions.Center), "FRONT");
        _sign = VrUi.CreateCanvas("SynthRidersFullBody.CalibrationFrontSign", new Vector2(760f, 300f), 0.001f);
        VrUi.Image(_sign, "Background", new Vector2(760f, 300f), Vector2.zero,
            new Color(0.10f, 0.065f, 0.22f, 0.85f));
        _label = VrUi.Text(_sign, "Direction", new Vector2(730f, 55f), new Vector2(0f, 100f),
            34f, cyan, TextAlignmentOptions.Center);
        _focus = VrUi.Image(_sign, "Focus", new Vector2(18f, 18f), Vector2.zero, cyan);
        _focus.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        _countdown = VrUi.Text(_sign, "Countdown", new Vector2(730f, 60f), new Vector2(0f, -50f),
            32f, Color.white, TextAlignmentOptions.Center);
        _hint = VrUi.Text(_sign, "Posture", new Vector2(730f, 55f), new Vector2(0f, -105f),
            23f, Color.white, TextAlignmentOptions.Center);
        _hint.enableWordWrapping = true;
        _progress = VrUi.Image(_sign, "Progress", new Vector2(0f, 5f), new Vector2(0f, -145f), cyan);
        Hide();
    }
}
