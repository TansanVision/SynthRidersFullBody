using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SynthRidersFullBody;

/// <summary>足を横から見た線画で、足首と足先の装着位置を区別する説明です。</summary>
internal sealed class TrackerMountGuide
{
    private readonly GameObject _root;
    private readonly Image _ankle, _toes;
    private readonly TextMeshProUGUI _heading;

    /// <summary>足とすねの輪郭、2つのセンサー位置、装着者基準の注意書きを作成します。</summary>
    public TrackerMountGuide(GameObject parent)
    {
        _root = new GameObject("TrackerMountGuide");
        _root.transform.SetParent(parent.transform, false);
        var rect = _root.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new(.5f, .5f);
        rect.sizeDelta = new(690, 700);
        rect.anchoredPosition = new(445, 0);
        _heading = Label("Heading", new(0, 270), new(620, 70), 32, "");
        var color = new Color(.06f, .87f, .94f);
        Line(new(-170, 170), new(-170, -100), color);
        Line(new(-170, -100), new(200, -100), color);
        Line(new(200, -100), new(210, -50), color);
        Line(new(210, -50), new(-60, 10), color);
        Line(new(-60, 10), new(-60, 170), color);
        _ankle = VrUi.Image(_root, "AnkleSensor", new(30, 30), new(-60, 50), color);
        _toes = VrUi.Image(_root, "ToeSensor", new(30, 30), new(140, -25), color);
        Line(new(-40, 50), new(40, 110), color);
        Line(new(140, -40), new(140, -165), color);
        Label("Ankle", new(135, 145), new(300, 72), 29, "Ankle / lower shin");
        Label("Toes", new(100, -200), new(400, 72), 29, "Toes / front of foot");
        Label("Direction", new(10, -290), new(600, 45), 25, "Side view: toes point forward");
        Label("Instructions", new(0, -355), new(620, 70), 23,
            "Left/right are your own. Two sensors per foot need separate assignments.");
        _root.SetActive(false);
    }

    /// <summary>足の個体選択時だけ表示し、選択中の装着位置をピンクで強調します。</summary>
    public void Show(bool visible, TrackerRole? role)
    {
        _root.SetActive(visible);
        if (!visible || role == null) return;
        _heading.text = UiText.Get(TrackerRoles.Label(role.Value));
        var ankle = role is TrackerRole.LeftFoot or TrackerRole.RightFoot;
        _ankle.color = ankle ? new(.96f, .3f, .85f) : new(.06f, .87f, .94f);
        _toes.color = !ankle ? new(.96f, .3f, .85f) : new(.06f, .87f, .94f);
    }

    /// <summary>言語変更に追従する折り返しラベルを作成します。</summary>
    private TextMeshProUGUI Label(string name, Vector2 position, Vector2 size, float font, string text)
    {
        var label = VrUi.Text(_root, name, size, position, font, Color.white, TextAlignmentOptions.Center);
        label.enableWordWrapping = true;
        if (text.Length > 0) UiLocalization.Bind(label, text);
        return label;
    }

    /// <summary>影や物理判定を持たない2点間の線を描きます。</summary>
    private void Line(Vector2 from, Vector2 to, Color color)
    {
        var delta = to - from;
        var line = VrUi.Image(_root, "Outline", new(delta.magnitude, 5), (from + to) * .5f, color);
        line.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
    }
}
