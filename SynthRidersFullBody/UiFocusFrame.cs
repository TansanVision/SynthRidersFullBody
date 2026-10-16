using UnityEngine;
using UnityEngine.UI;

namespace SynthRidersFullBody;

/// <summary>入口と詳細UIで共用する、ホバーのシアン枠と押下時の短い白枠です。</summary>
internal sealed class UiFocusFrame
{
    private readonly GameObject _root;
    private readonly Image[] _edges;
    private float _pulseUntil;
    private bool _focused, _enabled, _shown, _white;

    /// <summary>文字や当たり判定を変更しない、ボタンの内側の細い枠を作成します。</summary>
    public UiFocusFrame(GameObject parent, Vector2 size)
    {
        _root = new GameObject("FocusOutline");
        _root.transform.SetParent(parent.transform, false);
        var rect = _root.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new(.5f, .5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;
        _edges = new[]
        {
            VrUi.Image(_root, "Top", new(size.x - 4, 3), new(0, size.y / 2 - 2), Color.white),
            VrUi.Image(_root, "Bottom", new(size.x - 4, 3), new(0, -size.y / 2 + 2), Color.white),
            VrUi.Image(_root, "Left", new(3, size.y - 4), new(-size.x / 2 + 2, 0), Color.white),
            VrUi.Image(_root, "Right", new(3, size.y - 4), new(size.x / 2 - 2, 0), Color.white)
        };
        _root.SetActive(false);
    }

    /// <summary>情報行・無効操作には枠を出さず、有効なホバーと押下だけを強調します。</summary>
    public void Show(bool focused, bool enabled = true)
    {
        _focused = focused;
        _enabled = enabled;
        var white = Time.unscaledTime < _pulseUntil;
        var shown = enabled && (focused || white);
        if (_shown == shown && _white == white) return;
        _shown = shown;
        _white = white;
        _root.SetActive(shown);
        if (shown) foreach (var edge in _edges) edge.color = white ? Color.white : new Color(.06f, .87f, .94f);
    }

    /// <summary>押下を受け付けたことだけを0.18秒表示します。処理の成功通知ではありません。</summary>
    public void Pulse()
    {
        _pulseUntil = Time.unscaledTime + .18f;
        Show(_focused, _enabled);
    }
}
