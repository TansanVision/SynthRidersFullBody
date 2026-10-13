using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SynthRidersFullBody;

// Pixel-sized UI on a world-space canvas keeps text legible at a known distance.
/// <summary>
/// ピクセル単位のUI部品をワールド空間のCanvasへ組み立てる共通処理です。
/// </summary>
internal static class VrUi
{
    /// <summary>
    /// UIポインターの操作を受け付ける最大距離です。単位はmです。
    /// </summary>
    public const float InteractionDistance = 5f;
    /// <summary>
    /// 指定した物理スケールのワールドCanvasを作り、シーン移動後も保持します。
    /// </summary>
    /// <param name="name">作成するオブジェクトまたはUI部品の名前。</param>
    /// <param name="size">UI部品の幅と高さ。単位はピクセルです。</param>
    /// <param name="metresPerPixel">Canvasの1ピクセルあたりのワールド長。</param>
    /// <returns>RectTransformとCanvasを持つUIルート。</returns>
    public static GameObject CreateCanvas(string name, Vector2 size, float metresPerPixel)
    {
        var root = new GameObject(name);
        var rect = root.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 500;
        root.transform.localScale = Vector3.one * metresPerPixel;
        UnityEngine.Object.DontDestroyOnLoad(root);
        return root;
    }

    /// <summary>
    /// 親UIへ画像部品を追加し、サイズ・位置・色を設定します。
    /// </summary>
    /// <param name="parent">UI部品を追加する親GameObject。</param>
    /// <param name="name">作成するオブジェクトまたはUI部品の名前。</param>
    /// <param name="size">UI部品の幅と高さ。単位はピクセルです。</param>
    /// <param name="position">親UIの中央を基準にしたローカル位置。単位はピクセルです。</param>
    /// <param name="color">表示部品に設定する色とアルファ値。</param>
    /// <returns>作成したImage。Unity標準のレイキャスト対象にはしません。</returns>
    public static Image Image(GameObject parent, string name, Vector2 size, Vector2 position, Color color)
    {
        var gameObject = new GameObject(name);
        gameObject.transform.SetParent(parent.transform, false);
        var rect = gameObject.AddComponent<RectTransform>();
        Place(rect, size, position);
        var image = gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>
    /// 水平辺と斜辺の画像部品を組み合わせ、六角形風の枠を作成します。
    /// </summary>
    /// <param name="parent">UI部品を追加する親GameObject。</param>
    /// <param name="name">作成するオブジェクトまたはUI部品の名前。</param>
    /// <param name="size">UI部品の幅と高さ。単位はピクセルです。</param>
    /// <param name="position">親UIの中央を基準にしたローカル位置。単位はピクセルです。</param>
    /// <param name="color">表示部品に設定する色とアルファ値。</param>
    /// <param name="thickness">枠線の太さ。単位はUIピクセルです。</param>
    /// <param name="cornerCut">枠の角を切り欠く横幅。単位はUIピクセルです。</param>
    /// <returns>枠の各辺をまとめたUIルート。</returns>
    public static GameObject HexOutline(GameObject parent, string name, Vector2 size,
        Vector2 position, Color color, float thickness, float cornerCut)
    {
        var frame = new GameObject(name);
        frame.transform.SetParent(parent.transform, false);
        var rect = frame.AddComponent<RectTransform>();
        Place(rect, size, position);
        var halfWidth = size.x * 0.5f;
        var halfHeight = size.y * 0.5f;
        var cut = Mathf.Clamp(cornerCut, 1f, halfWidth - 1f);
        var diagonalLength = Mathf.Sqrt(cut * cut + halfHeight * halfHeight);
        var diagonalAngle = Mathf.Atan2(halfHeight, cut) * Mathf.Rad2Deg;
        Image(frame, "Top", new Vector2(size.x - cut * 2f, thickness),
            new Vector2(0f, halfHeight), color);
        Image(frame, "Bottom", new Vector2(size.x - cut * 2f, thickness),
            new Vector2(0f, -halfHeight), color);
        OutlineEdge(frame, "UpperLeft", diagonalLength, thickness,
            new Vector2(-halfWidth + cut * 0.5f, halfHeight * 0.5f),
            diagonalAngle, color);
        OutlineEdge(frame, "LowerLeft", diagonalLength, thickness,
            new Vector2(-halfWidth + cut * 0.5f, -halfHeight * 0.5f),
            -diagonalAngle, color);
        OutlineEdge(frame, "UpperRight", diagonalLength, thickness,
            new Vector2(halfWidth - cut * 0.5f, halfHeight * 0.5f),
            -diagonalAngle, color);
        OutlineEdge(frame, "LowerRight", diagonalLength, thickness,
            new Vector2(halfWidth - cut * 0.5f, -halfHeight * 0.5f),
            diagonalAngle, color);
        return frame;
    }

    /// <summary>
    /// 枠の1辺を細い画像として生成し、指定角度へ回転します。
    /// </summary>
    /// <param name="parent">UI部品を追加する親GameObject。</param>
    /// <param name="name">作成するオブジェクトまたはUI部品の名前。</param>
    /// <param name="length">UIの辺の長さ。単位はピクセルです。</param>
    /// <param name="thickness">枠線の太さ。単位はUIピクセルです。</param>
    /// <param name="position">親UIの中央を基準にしたローカル位置。単位はピクセルです。</param>
    /// <param name="angle">UI部品のZ軸まわりの回転角。単位は度です。</param>
    /// <param name="color">表示部品に設定する色とアルファ値。</param>
    private static void OutlineEdge(GameObject parent, string name, float length,
        float thickness, Vector2 position, float angle, Color color)
    {
        var edge = Image(parent, name, new Vector2(length, thickness), position, color);
        edge.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    /// <summary>
    /// 親UIへTextMeshPro文字部品を作り、書体・大きさ・色・配置を設定します。
    /// </summary>
    /// <param name="parent">UI部品を追加する親GameObject。</param>
    /// <param name="name">作成するオブジェクトまたはUI部品の名前。</param>
    /// <param name="size">UI部品の幅と高さ。単位はピクセルです。</param>
    /// <param name="position">親UIの中央を基準にしたローカル位置。単位はピクセルです。</param>
    /// <param name="fontSize">テキストの文字サイズ。</param>
    /// <param name="color">表示部品に設定する色とアルファ値。</param>
    /// <param name="alignment">テキスト領域内の配置方法。</param>
    /// <returns>作成したテキスト。折り返しを無効にし、領域を超える文字は省略します。</returns>
    public static TextMeshProUGUI Text(GameObject parent, string name, Vector2 size,
        Vector2 position, float fontSize, Color color, TextAlignmentOptions alignment)
    {
        var gameObject = new GameObject(name);
        gameObject.transform.SetParent(parent.transform, false);
        var rect = gameObject.AddComponent<RectTransform>();
        Place(rect, size, position);
        var text = gameObject.AddComponent<TextMeshProUGUI>();
        if (text.font == null)
            foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                if (font != null)
                {
                    text.font = font;
                    break;
                }
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    /// <summary>
    /// RectTransformのアンカーとピボットを中央に揃え、サイズとローカルUI位置を設定します。
    /// </summary>
    /// <param name="rect">配置するRectTransform。</param>
    /// <param name="size">UI部品の幅と高さ。単位はピクセルです。</param>
    /// <param name="position">親UIの中央を基準にしたローカル位置。単位はピクセルです。</param>
    private static void Place(RectTransform rect, Vector2 size, Vector2 position)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }
}
