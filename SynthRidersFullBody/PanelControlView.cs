using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SynthRidersFullBody;

/// <summary>操作種別に合わせた背景、スイッチ、選択マークを描画する共通部品です。</summary>
internal sealed class PanelControlView
{
    private readonly GameObject _root;
    private readonly Image _background;
    private readonly Image _switchTrack;
    private readonly Image _switchThumb;
    private readonly Image _selection;
    private readonly TextMeshProUGUI _arrow;
    private readonly PanelRect _bounds;
    private PanelControlState? _renderedState;
    private bool _renderedFocus;
    private readonly UiFocusFrame _focusFrame;
    /// <summary>呼び出し元が設定するローカライズ済みラベルです。</summary>
    public TextMeshProUGUI Label { get; }

    /// <summary>共通矩形の内側にラベルと補助表示を作成します。</summary>
    public PanelControlView(GameObject parent, int index, float fontSize = 30f)
    {
        _bounds = PanelControls.Bounds(index);
        _background = VrUi.Image(parent, $"Control{index}", new(_bounds.Width, _bounds.Height),
            new(_bounds.X, _bounds.Y), Color.white);
        _root = _background.gameObject;
        Label = VrUi.Text(_root, "Label", new(_bounds.Width - 36, _bounds.Height - 8), Vector2.zero,
            fontSize, Color.white, TextAlignmentOptions.MidlineLeft);
        Label.enableWordWrapping = true;
        _arrow = VrUi.Text(_root, "Arrow", new(30, 40), new(_bounds.Width / 2 - 24, 0),
            28, Color.white, TextAlignmentOptions.Center);
        _arrow.text = index is PanelControls.Back or PanelControls.PagePrevious or PanelControls.DashboardPrevious or PanelControls.Nearer
            ? "<" : index == PanelControls.Close ? "X" : ">";
        _switchTrack = VrUi.Image(_root, "Switch", new(72, 30), new(_bounds.Width / 2 - 52, 0), Color.white);
        _switchThumb = VrUi.Image(_root, "Thumb", new(24, 24), Vector2.zero, Color.white);
        _selection = VrUi.Image(_root, "Selected", new(_bounds.Width - 8, 4),
            new(0, -_bounds.Height / 2 + 3), Color.white);
        _focusFrame = new UiFocusFrame(_root, new(_bounds.Width, _bounds.Height));
    }

    /// <summary>受け付けた押下を短い白い枠で示します。保存や校正の成功を意味する表示ではありません。</summary>
    public void Pulse() => _focusFrame.Pulse();

    /// <summary>入力判定と共通の状態で操作可否と選択を表し、情報行には背景を描きません。</summary>
    public void Render(PanelControlState state, bool focused)
    {
        focused &= state.Interactive;
        // Keep the accepted click visible briefly even if that action selects
        // or disables this control. Disabled controls still cannot receive hover/clicks.
        _focusFrame.Show(focused, state.Kind is not (PanelControlKind.Hidden or PanelControlKind.Information));
        if (_renderedState == state && _renderedFocus == focused) return;
        _renderedState = state;
        _renderedFocus = focused;
        _root.SetActive(state.Kind != PanelControlKind.Hidden);
        if (state.Kind == PanelControlKind.Hidden) return;
        var information = state.Kind == PanelControlKind.Information;
        var toggle = state.Kind == PanelControlKind.Toggle;
        var navigation = state.Kind == PanelControlKind.Navigation;
        _background.enabled = !information;
        _background.color = !state.Enabled ? new Color(.14f, .12f, .22f, 1f) :
            state.Kind == PanelControlKind.Primary ? new Color(focused ? .96f : .73f, .03f, .60f, 1f) :
            focused ? new Color(.06f, .43f, .55f, 1f) :
            state.Kind == PanelControlKind.Choice && state.Selected ? new Color(.35f, .12f, .52f, 1f) :
            new Color(.085f, .12f, .26f, 1f);
        Label.color = !state.Enabled ? new Color(.65f, .65f, .72f, 1f) : Color.white;
        var reserved = toggle ? 110 : navigation ? 70 : 36;
        Label.rectTransform.sizeDelta = new(_bounds.Width - reserved, _bounds.Height - 8);
        Label.rectTransform.anchoredPosition = new(-(reserved - 36) / 2f, 0);
        _arrow.gameObject.SetActive(navigation);
        _switchTrack.gameObject.SetActive(toggle);
        _switchThumb.gameObject.SetActive(toggle);
        _switchTrack.color = state.Selected ? new Color(.06f, .87f, .94f, 1f) : new Color(.35f, .35f, .42f, 1f);
        _switchThumb.rectTransform.anchoredPosition = new(_bounds.Width / 2 - 52 + (state.Selected ? 20 : -20), 0);
        _selection.gameObject.SetActive(state.Kind == PanelControlKind.Choice && state.Selected);
        _selection.color = new Color(.96f, .03f, .78f, 1f);
    }
}
