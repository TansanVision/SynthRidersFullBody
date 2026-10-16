using UnityEngine;

namespace SynthRidersFullBody;

/// <summary>読みやすさと操作フィードバックの共通設定を管理します。</summary>
internal sealed partial class SettingsPanel
{
    private ModConfig _uiConfig = new();
    private bool _appearanceScreen;
    private PanelPlacement? _placement;
    private readonly UiFeedback _feedback = new();

    /// <summary>保存済みのUI設定を使用します。毎フレームの位置追従は行いません。</summary>
    public void SetUiConfiguration(ModConfig config) => _uiConfig = config;

    /// <summary>入口の操作にも同じ音・振動設定を適用します。</summary>
    public void PlayFeedback(Tracking tracking, UiHand hand) => _feedback.Play(_uiConfig, tracking, hand);

    /// <summary>明示的な位置リセット時に、現在の頭と入口から配置を取り直します。</summary>
    public void Reposition(PanelPlacement? placement, Camera camera)
    {
        if (_calibrationScreen || ShowCalibrationPose || IsCalibrationCountingDown) return;
        _placement = placement ?? DefaultPlacement(camera);
        ApplyAppearance();
    }

    /// <summary>入口の取得失敗時も、目線正面を塞がず下側へ固定配置します。</summary>
    private static PanelPlacement DefaultPlacement(Camera camera)
    {
        var eye = camera.transform.position;
        var forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
        var entry = eye + forward * VrPanelLayout.DefaultDistance;
        return new(new(eye.x, eye.y, eye.z), new(entry.x, entry.y, entry.z), VrPanelLayout.PanelScale);
    }

    /// <summary>開いた時点の基準を保持して距離・サイズを反映します。レイは同じTransformで逆変換します。</summary>
    private void ApplyAppearance()
    {
        if (_root == null || _placement is not { } placement) return;
        var position = placement.Position(_uiConfig.UiPanelDistance, _uiConfig.UiLargeText);
        var direction = placement.Entry - placement.Eye;
        var forward = new Vector3(direction.X, 0, direction.Z).normalized;
        if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
        _root.transform.localScale = Vector3.one * VrPanelLayout.Scale(_uiConfig.UiLargeText);
        _root.transform.SetPositionAndRotation(new(position.X, position.Y, position.Z), Quaternion.LookRotation(forward, Vector3.up));
        _nextTextUpdate = 0;
    }

    /// <summary>UI設定の明示操作だけを反映します。キャリブレーションは無効化しません。</summary>
    private PanelCommand HandleAppearance(int control)
    {
        switch (control)
        {
            case PanelControls.TextStandard: _uiConfig.UiLargeText = false; break;
            case PanelControls.TextLarge: _uiConfig.UiLargeText = true; break;
            case PanelControls.Nearer: _uiConfig.UiPanelDistance = MathF.Round(_uiConfig.UiPanelDistance - .1f, 1); break;
            case PanelControls.Farther: _uiConfig.UiPanelDistance = MathF.Round(_uiConfig.UiPanelDistance + .1f, 1); break;
            case 2: return PanelCommand.RepositionUi;
            case 3: _uiConfig.UiHaptics = !_uiConfig.UiHaptics; return PanelCommand.UiPreferencesChanged;
            case 4: _uiConfig.UiSound = !_uiConfig.UiSound; return PanelCommand.UiPreferencesChanged;
            case 5: _uiConfig.UiLargeText = false; _uiConfig.UiPanelDistance = VrPanelLayout.DefaultDistance; break;
            default: return PanelCommand.None;
        }
        ApplyAppearance();
        return PanelCommand.UiPreferencesChanged;
    }

    /// <summary>標準・大、距離、位置リセット、振動・操作音を表示します。</summary>
    private void UpdateAppearanceText()
    {
        _title!.text = UiText.Get("Panel & feedback");
        _status!.text = UiText.Format("Distance: {0} m | Text and buttons: {1}",
            _uiConfig.UiPanelDistance.ToString("0.0"), UiText.Get(_uiConfig.UiLargeText ? "Large" : "Standard"));
        _views[PanelControls.TextStandard]!.Label.text = UiText.Get("Standard");
        _views[PanelControls.TextLarge]!.Label.text = UiText.Get("Large (+15%)");
        _views[PanelControls.Nearer]!.Label.text = UiText.Get("Move closer");
        _views[PanelControls.Farther]!.Label.text = UiText.Get("Move farther");
        _rows[2]!.text = UiText.Get("Reset position here");
        _rows[3]!.text = UiText.Format("Controller vibration: {0}", UiText.OnOff(_uiConfig.UiHaptics));
        _rows[4]!.text = UiText.Format("UI sound: {0}", UiText.OnOff(_uiConfig.UiSound));
        _rows[5]!.text = UiText.Get("Restore default size / distance");
        _footer!.text = UiText.Get("Shared across avatars. Position stays fixed until you move it. Calibration keeps its front direction.");
    }

    /// <summary>距離の端と選択済みサイズは操作不能とし、表示と当たり判定を揃えます。</summary>
    private void UpdateAppearanceControls()
        => PanelControls.Appearance(_controls, _uiConfig.UiPanelDistance, _uiConfig.UiLargeText, _uiConfig.UiHaptics, _uiConfig.UiSound);
}
