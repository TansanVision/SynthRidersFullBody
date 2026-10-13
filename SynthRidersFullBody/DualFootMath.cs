using System.Numerics;

namespace SynthRidersFullBody;

/// <summary>
/// 足首と独立した足先の計測を使い、足首位置と足の長さを変えずに向きを補正します。
/// </summary>
internal static class DualFootMath
{
    // Swing the foot towards a separately measured toe while retaining ankle
    // roll. Never translate the ankle or stretch the foot to chase measurement noise.
    /// <summary>
    /// 足首のロールを保ちつつ、足の軸を計測した足先方向へ向けます。
    /// </summary>
    /// <param name="ankle">足首の位置。足先の計測位置と同じ座標系です。</param>
    /// <param name="rotation">位置と同じ座標系で表した回転。</param>
    /// <param name="localToe">足首に対する足先のローカル位置オフセット。</param>
    /// <param name="measuredToe">独立した足先入力から求めた、足首と同じ空間の目標位置。</param>
    /// <returns>補正後の回転。入力が縮退・反平行に近い場合は元の回転。</returns>
    public static Quaternion Rotation(Vector3 ankle, Quaternion rotation, Vector3 localToe, Vector3 measuredToe)
    {
        var from = Vector3.Transform(localToe, rotation);
        var to = measuredToe - ankle;
        if (!Finite(from) || !Finite(to) || from.LengthSquared() < 0.0001f || to.LengthSquared() < 0.0001f)
            return rotation;
        from = Vector3.Normalize(from); to = Vector3.Normalize(to);
        var dot = Math.Clamp(Vector3.Dot(from, to), -1f, 1f);
        // An antiparallel/near-zero pair indicates a bad sample or swapped roles.
        // Retain the ankle orientation rather than picking an arbitrary flip axis.
        if (dot < -0.95f) return rotation;
        var swing = Quaternion.Normalize(new Quaternion(Vector3.Cross(from, to), 1f + dot));
        return Quaternion.Normalize(swing * rotation);
    }
    /// <summary>
    /// ベクトルの各成分が有限値かを確認します。
    /// </summary>
    /// <param name="v">変換または検証するベクトル。</param>
    /// <returns>NaNと無限大を含まなければtrue。</returns>
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
