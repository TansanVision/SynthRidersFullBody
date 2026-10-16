using System.Numerics;

namespace SynthRidersFullBody;

/// <summary>
/// 入口と詳細パネルの読みやすい距離・拡大率・重なり回避を計算します。
/// </summary>
internal static class VrPanelLayout
{
    /// <summary>詳細UIの保存距離の既定値・下限・上限です。単位はmです。</summary>
    public const float DefaultDistance = 1.4f, MinDistance = 1.1f, MaxDistance = 1.8f;
    /// <summary>破損した設定や範囲外の距離を操作可能な値へ戻します。</summary>
    public static float ClampDistance(float distance) => float.IsFinite(distance)
        ? Math.Clamp(distance, MinDistance, MaxDistance) : DefaultDistance;
    /// <summary>文字と操作領域を一緒に拡大し、当たり判定との不一致を防ぎます。</summary>
    public static float Scale(bool large) => PanelScale * (large ? 1.15f : 1f);
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
        => DetailPosition(eye, entry, entryScale,
            Math.Clamp(new Vector2(entry.X - eye.X, entry.Z - eye.Z).Length(), 1.15f, 1.6f), PanelScale);

    /// <summary>保存した距離・実際の拡大率を用い、入口とパネルの投影範囲を離します。</summary>
    public static Vector3 DetailPosition(Vector3 eye, Vector3 entry, float entryScale, float panelDistance, float panelScale)
    {
        var horizontal = new Vector3(entry.X - eye.X, 0, entry.Z - eye.Z);
        var distance = horizontal.Length();
        var direction = distance > 0.1f ? horizontal / distance : Vector3.UnitZ;
        panelDistance = ClampDistance(panelDistance);
        var ratio = panelDistance / Math.Max(0.1f, distance);
        var projectedY = eye.Y + (entry.Y - eye.Y) * ratio;
        // Separate the projected bounds, even when the score anchor is much
        // farther away than the details. Both controls remain reachable.
        var separation = 400f * panelScale + 130f * entryScale * ratio + 0.12f;
        var y = projectedY + (projectedY < eye.Y - 0.3f ? separation : -separation);
        // Avoid projecting a low, nearby entry far below the user's feet when
        // increasing distance. Never move back into the entry's projected bounds.
        if (y < eye.Y - .85f) y = Math.Max(eye.Y - .85f, projectedY + separation);
        var position = eye + direction * panelDistance;
        position.Y = y;
        return position;
    }
}

/// <summary>開いた時点の頭と入口の位置です。頭を動かしてもパネル位置は更新しません。</summary>
internal readonly record struct PanelPlacement(Vector3 Eye, Vector3 Entry, float EntryScale)
{
    /// <summary>明示操作でだけ呼び出す、指定距離・サイズの固定位置計算です。</summary>
    public Vector3 Position(float distance, bool large) =>
        VrPanelLayout.DetailPosition(Eye, Entry, EntryScale, distance, VrPanelLayout.Scale(large));
}
