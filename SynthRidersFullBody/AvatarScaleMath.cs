using System.Numerics;

namespace SynthRidersFullBody;

// Fit a neutral rig to the standing player's vertical head/foot span. Only
// uniform root scale changes; neither bone translations nor proportions change.
/// <summary>
/// 頭と左右足の高さを基準に、モデル全体を均等に拡縮する計算を提供します。
/// </summary>
internal static class AvatarScaleMath
{
    /// <summary>
    /// 有効な直立時の高さの差から、骨の比率を保つルート位置とローカルスケールを計算します。
    /// </summary>
    /// <param name="root">モデルルートの現在のワールド位置。</param>
    /// <param name="localScale">モデルルートの現在のローカルスケール。</param>
    /// <param name="modelHead">モデルの頭または目の基準点のワールド位置。</param>
    /// <param name="modelLeftFoot">モデルの左足首のワールド位置。</param>
    /// <param name="modelRightFoot">モデルの右足首のワールド位置。</param>
    /// <param name="userHead">HMDまたは頭目標のワールド位置。</param>
    /// <param name="userLeftFoot">左足入力のワールド位置。</param>
    /// <param name="userRightFoot">右足入力のワールド位置。</param>
    /// <param name="position">成功時のモデルルートのワールド位置を受け取ります。</param>
    /// <param name="scale">成功時のモデルルートのローカルスケールを受け取ります。</param>
    /// <param name="factor">成功時に適用する均等な拡縮率を受け取ります。</param>
    /// <returns>入力と計算結果が有効で、許容される身長・足の高低差に収まる場合はtrue。</returns>
    /// <remarks>位置はm、Y軸は上として扱います。ユーザーの頭と平均足位置の高さの差を0.5〜2.5m、左右足の高低差を0.2m以下に制限します。falseの場合は出力を適用しません。</remarks>
    public static bool TryFit(Vector3 root, Vector3 localScale, Vector3 modelHead,
        Vector3 modelLeftFoot, Vector3 modelRightFoot, Vector3 userHead,
        Vector3 userLeftFoot, Vector3 userRightFoot, out Vector3 position,
        out Vector3 scale, out float factor)
    {
        position = root;
        scale = localScale;
        factor = 1f;
        if (!Finite(root) || !Finite(localScale) || !Finite(modelHead) ||
            !Finite(modelLeftFoot) || !Finite(modelRightFoot) || !Finite(userHead) ||
            !Finite(userLeftFoot) || !Finite(userRightFoot) ||
            localScale.X <= 0 || localScale.Y <= 0 || localScale.Z <= 0) return false;
        var modelSpan = modelHead.Y - (modelLeftFoot.Y + modelRightFoot.Y) * 0.5f;
        var userSpan = userHead.Y - (userLeftFoot.Y + userRightFoot.Y) * 0.5f;
        if (modelSpan < 0.001f || userSpan < 0.5f || userSpan > 2.5f ||
            MathF.Abs(userLeftFoot.Y - userRightFoot.Y) > 0.2f) return false;
        factor = userSpan / modelSpan;
        position = userHead + (root - modelHead) * factor;
        scale = localScale * factor;
        return Finite(position) && Finite(scale);
    }

    /// <summary>
    /// ベクトルの全成分が有限値かを確認します。
    /// </summary>
    /// <param name="v">変換または検証するベクトル。</param>
    /// <returns>NaNと無限大を含まなければtrue。</returns>
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
