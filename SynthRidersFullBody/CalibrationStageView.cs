using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SynthRidersFullBody;

/// <summary>設定パネル右側に、手順・姿勢の説明図・結果の案内を表示します。</summary>
internal sealed class CalibrationStageView
{
    private static readonly string[] StageNames = { "Preparation", "Choose a pose", "Countdown", "Check movement" };
    private readonly GameObject _root;
    private readonly TextMeshProUGUI[] _steps = new TextMeshProUGUI[4];
    private readonly TextMeshProUGUI[] _poseLabels = new TextMeshProUGUI[2];
    private readonly Image[] _selection = new Image[2];
    private readonly GameObject _illustrations;
    private readonly TextMeshProUGUI _message;
    private readonly TextMeshProUGUI _countdown;
    private readonly TextMeshProUGUI _heading;
    private readonly TextMeshProUGUI _pageLabel;
    private string _lastMessage = "";
    private CalibrationStage? _lastStage;
    /// <summary>長い結果案内のページ数です。</summary>
    public int MessagePages { get; private set; } = 1;
    /// <summary>表示中の結果ページです。TMPのページ番号は1から始まります。</summary>
    public int MessagePage { get; private set; } = 1;

    /// <summary>追加カメラやモデルを使わず、軽量なUI線画で両方の姿勢を説明します。</summary>
    public CalibrationStageView(GameObject parent)
    {
        _root = Group(parent, "CalibrationSteps", new(445f, 0f));
        _heading = VrUi.Text(_root, "Heading", new(630, 56), new(0, 270), 30,
            Color.white, TextAlignmentOptions.Center);
        _heading.enableWordWrapping = true;
        for (var i = 0; i < _steps.Length; i++)
            _steps[i] = VrUi.Text(_root, $"Step{i}", new(630, 36), new(0, 215 - i * 40),
                26, Color.white, TextAlignmentOptions.MidlineLeft);
        _illustrations = Group(_root, "PoseIllustrations", Vector2.zero);
        for (var i = 0; i < 2; i++)
        {
            var x = i == 0 ? -165f : 165f;
            DrawPose(Group(_illustrations, $"Pose{i}", new(x, -145)), i == 0);
            _poseLabels[i] = VrUi.Text(_illustrations, $"PoseLabel{i}", new(300, 52), new(x, -310),
                25, Color.white, TextAlignmentOptions.Center);
            _poseLabels[i].enableWordWrapping = true;
            _selection[i] = VrUi.Image(_illustrations, $"Selected{i}", new(280, 4), new(x, -340),
                new Color(.96f, .03f, .78f));
        }
        _countdown = VrUi.Text(_root, "LargeCountdown", new(630, 130), new(0, 110),
            110, Color.white, TextAlignmentOptions.Center);
        _message = VrUi.Text(_root, "Guidance", new(620, 340), new(0, -150),
            27, Color.white, TextAlignmentOptions.Center);
        _message.enableWordWrapping = true;
        _pageLabel = VrUi.Text(_root, "ResultPage", new(620, 32), new(0, -277), 23,
            Color.white, TextAlignmentOptions.Center);
        _root.SetActive(false);
    }

    /// <summary>画面を隠します。親パネルの破棄時には一緒に破棄されます。</summary>
    public void Hide() => _root.SetActive(false);

    /// <summary>長い結果を省略せずに読むため、ページを切り替えます。</summary>
    public void ChangePage(int delta) => MessagePage = Math.Clamp(MessagePage + delta, 1, MessagePages);

    /// <summary>現在の段階に必要な説明だけを表示し、計測中は一覧と説明図を畳みます。</summary>
    public void Update(CalibrationFlow flow, bool tPose, float countdown, string? liveWarning)
    {
        if (_lastStage != flow.Stage) { MessagePage = 1; _lastStage = flow.Stage; }
        _root.SetActive(true);
        _heading.text = UiText.Format("Step {0} / 4: {1}", (int)flow.Stage + 1, UiText.Get(StageNames[(int)flow.Stage]));
        for (var i = 0; i < _steps.Length; i++)
        {
            _steps[i].gameObject.SetActive(flow.Stage is CalibrationStage.Preparation or CalibrationStage.Pose);
            _steps[i].text = $"{i + 1}. {UiText.Get(StageNames[i])}";
            _steps[i].color = i == (int)flow.Stage ? new Color(.96f, .3f, .85f) : new Color(.65f, .75f, .85f);
        }
        _illustrations.SetActive(flow.Stage is CalibrationStage.Preparation or CalibrationStage.Pose);
        for (var i = 0; i < 2; i++)
        {
            var selected = (i == 0) == tPose;
            _poseLabels[i].text = (selected ? UiText.Get("[Selected] ") : "") + UiText.Get(i == 0 ? "T-pose" : "standing");
            _selection[i].gameObject.SetActive(selected);
        }
        _countdown.gameObject.SetActive(flow.CountingDown);
        _countdown.text = Mathf.CeilToInt(Mathf.Max(0f, countdown)).ToString();
        _message.gameObject.SetActive(flow.Stage is CalibrationStage.Countdown or CalibrationStage.Review);
        _message.rectTransform.sizeDelta = new Vector2(620, flow.Stage == CalibrationStage.Review ? 460 : 340);
        _message.rectTransform.anchoredPosition = new Vector2(0, flow.Stage == CalibrationStage.Review ? 0 : -150);
        if (flow.CountingDown)
            _message.text = UiText.Get(tPose ? "Look at the front dot. Arms sideways, palms down. Hold still."
                : "Look at the front dot. Arms at sides, palms inward. Hold still.") +
                (liveWarning == null ? "" : "\n\n" + UiText.Format("WARNING: {0}", UiText.Status(liveWarning)));
        else if (flow.Stage == CalibrationStage.Review)
            _message.text = UiText.Status(flow.Result ?? "") + "\n\n" + UiText.Get(flow.Succeeded
                ? "Move your waist and each foot in the mirror. Choose Done if movement looks right, or Try again."
                : "Check the reason above. Return to preparation to retry, or check tracker assignments.") +
                (flow.Warning == null ? "" : "\n\n" + UiText.Format("WARNING: {0}", UiText.Status(flow.Warning)));
        _message.overflowMode = flow.Stage == CalibrationStage.Review ? TextOverflowModes.Page : TextOverflowModes.Ellipsis;
        if (_lastMessage != _message.text) { MessagePage = 1; _lastMessage = _message.text; }
        if (flow.Stage == CalibrationStage.Review)
        {
            _message.ForceMeshUpdate();
            MessagePages = Math.Max(1, _message.textInfo?.pageCount ?? 1);
            MessagePage = Math.Clamp(MessagePage, 1, MessagePages);
            _message.pageToDisplay = MessagePage;
        }
        else { MessagePage = MessagePages = 1; _message.pageToDisplay = 1; }
        _pageLabel.gameObject.SetActive(flow.Stage == CalibrationStage.Review && MessagePages > 1);
        _pageLabel.text = UiText.Format("Page {0}/{1}", MessagePage, MessagePages);
    }

    /// <summary>子要素をまとめる、中央基準のUIルートを作成します。</summary>
    private static GameObject Group(GameObject parent, string name, Vector2 position)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent.transform, false);
        var rect = root.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(690, 700);
        rect.anchoredPosition = position;
        return root;
    }

    /// <summary>頭・胴・腕・脚を線で描き、Tポーズと直立の腕の位置を区別します。</summary>
    private static void DrawPose(GameObject parent, bool tPose)
    {
        var color = new Color(.06f, .87f, .94f);
        VrUi.Image(parent, "Head", new(32, 32), new(0, 80), color);
        Line(parent, new(0, 60), new(0, -30), color);
        Line(parent, new(-25, 40), new(25, 40), color);
        foreach (var side in new[] { -1f, 1f })
        {
            var elbow = tPose ? new Vector2(side * 75, 40) : new Vector2(side * 40, 0);
            var hand = tPose ? new Vector2(side * 125, 40) : new Vector2(side * 50, -40);
            Line(parent, new(side * 25, 40), elbow, color);
            Line(parent, elbow, hand, color);
            Line(parent, new(0, -30), new(side * 35, -110), color);
            Line(parent, new(side * 35, -110), new(side * 55, -110), color);
        }
    }

    /// <summary>2点を結ぶ画像の線分を作成します。物理判定や影は持ちません。</summary>
    private static void Line(GameObject parent, Vector2 from, Vector2 to, Color color)
    {
        var delta = to - from;
        var line = VrUi.Image(parent, "Bone", new(delta.magnitude, 6), (from + to) * .5f, color);
        line.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
    }
}
