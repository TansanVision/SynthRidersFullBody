using System.Numerics;

namespace SynthRidersFullBody;

/// <summary>
/// 入口と詳細パネルの読みやすい距離・拡大率・重なり回避を計算します。
/// </summary>
internal static class VrPanelLayout
{
    /// <summary>
    /// 詳細パネルの1ピクセルあたりのワールド長です。単位はmです。
    /// </summary>
    public const float PanelScale = 0.00125f;
    /// <summary>
    /// 入口までの距離に応じて、最大3倍までCanvasのスケールを増やします。
    /// </summary>
    /// <param name="distance">ユーザーから入口までの距離。単位はmです。</param>
    /// <returns>入口の1ピクセルあたりのワールド長。</returns>
    public static float EntryScale(float distance) => 0.00125f * Math.Clamp(distance / 1.6f, 1f, 3f);

    /// <summary>
    /// 詳細パネルを入口方向の近距離へ置き、投影上の入口領域と上下に離します。
    /// </summary>
    /// <param name="eye">HMDのワールド位置。</param>
    /// <param name="entry">入口ボタンのワールド位置。</param>
    /// <param name="entryScale">入口Canvasの1ピクセルあたりのワールド長。</param>
    /// <returns>HMDから水平距離1.15〜1.6mにある詳細パネルのワールド位置。</returns>
    public static Vector3 DetailPosition(Vector3 eye, Vector3 entry, float entryScale)
    {
        var horizontal = new Vector3(entry.X - eye.X, 0, entry.Z - eye.Z);
        var distance = horizontal.Length();
        var direction = distance > 0.1f ? horizontal / distance : Vector3.UnitZ;
        var panelDistance = Math.Clamp(distance, 1.15f, 1.6f);
        var ratio = panelDistance / Math.Max(0.1f, distance);
        var projectedY = eye.Y + (entry.Y - eye.Y) * ratio;
        // Separate the projected bounds, even when the score anchor is much
        // farther away than the details. Both controls remain reachable.
        var separation = 400f * PanelScale + 130f * entryScale * ratio + 0.12f;
        var y = projectedY + (projectedY < eye.Y - 0.3f ? separation : -separation);
        var position = eye + direction * panelDistance;
        position.Y = y;
        return position;
    }
}
