using System.Numerics;

namespace SynthRidersFullBody;

/// <summary>頭と胸・肩の寸法から、固定バストショットの位置と注視点を求めます。</summary>
internal static class MenuBustFraming
{
    /// <summary>固定する画角。ゲーム側の保存済みカメラ設定は変更しません。</summary>
    public const float FieldOfView = 35f;

    /// <summary>頭上の余白と胸・肩幅を収める透視投影の構図を計算します。</summary>
    /// <param name="head">頭ボーンの位置。</param>
    /// <param name="chest">胸の位置。</param>
    /// <param name="hips">骨盤の位置。</param>
    /// <param name="forward">アバターの正面方向。上下成分は除去します。</param>
    /// <param name="shoulderWidth">左右上腕の付け根の距離。</param>
    /// <param name="aspect">出力カメラの縦横比。</param>
    /// <param name="nearClip">出力カメラの近クリップ距離。</param>
    /// <param name="position">成功時のカメラ位置。</param>
    /// <param name="focus">成功時の注視点。</param>
    /// <returns>骨格と出力条件が有効で構図を計算できた場合はtrue。</returns>
    public static bool TryCreate(Vector3 head, Vector3 chest, Vector3 hips, Vector3 forward,
        float shoulderWidth, float aspect, float nearClip, out Vector3 position, out Vector3 focus)
    {
        position = focus = default;
        if (!Finite(head) || !Finite(chest) || !Finite(hips) || !Finite(forward) ||
            !float.IsFinite(shoulderWidth) || shoulderWidth < 0f ||
            !float.IsFinite(aspect) || aspect < .1f || !float.IsFinite(nearClip) || nearClip < 0f) return false;
        var torso = head.Y - hips.Y;
        forward.Y = 0f;
        if (torso < .02f || forward.LengthSquared() < .000001f || chest.Y >= head.Y || chest.Y < hips.Y) return false;
        var top = head.Y + torso * .38f;
        var bottom = chest.Y - torso * .18f;
        var halfHeight = (top - bottom) * .5f;
        var halfWidth = MathF.Max(shoulderWidth * .65f, torso * .48f);
        var tangent = MathF.Tan(FieldOfView * MathF.PI / 360f);
        // Include body depth so the closest part of the head/shoulders remains inside the frustum.
        var depth = torso * .35f;
        var distance = MathF.Max(MathF.Max(halfHeight / tangent, halfWidth / (tangent * aspect)) + depth,
            nearClip + depth * 2f);
        focus = (head + chest) * .5f;
        focus.Y = (top + bottom) * .5f;
        position = focus + Vector3.Normalize(forward) * distance;
        return Finite(position) && Finite(focus);
    }

    /// <summary>非有限の骨格座標を除外します。</summary>
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
