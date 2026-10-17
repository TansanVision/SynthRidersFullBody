using System.Numerics;

namespace SynthRidersFullBody;

/// <summary>計測時の膝角度・ガイド一致度を値で保持し、後続の骨格更新から切り離します。</summary>
/// <param name="FlexDegrees">直線を0度とした屈曲角。無効な骨格ではnull。</param>
/// <param name="GuideDot">脚軸に直交する曲げ方向の一致度。直線・無効・ガイドなしではnull。</param>
internal readonly record struct KneePoseSample(float? FlexDegrees, float? GuideDot)
{
    /// <summary>関節位置から診断値を取得します。足先ではなく足首を使用します。</summary>
    /// <param name="hip">股関節のワールド位置。</param>
    /// <param name="knee">膝のワールド位置。</param>
    /// <param name="ankle">足首のワールド位置。</param>
    /// <param name="guideDirection">股関節から有効な膝ガイドへの方向。未使用時はnull。</param>
    /// <returns>Transformや入力への参照を持たない計測結果。</returns>
    public static KneePoseSample Capture(Vector3 hip, Vector3 knee, Vector3 ankle, Vector3? guideDirection)
    {
        var upper = knee - hip;
        var lower = ankle - knee;
        if (!TryNormalize(upper, out var upperDirection) || !TryNormalize(lower, out var lowerDirection))
            return default;
        var flex = MathF.Acos(Math.Clamp(Vector3.Dot(upperDirection, lowerDirection), -1f, 1f)) * 180f / MathF.PI;
        if (!TryNormalize(ankle - hip, out var axis) || !guideDirection.HasValue ||
            !TryNormalize(guideDirection.Value, out var guide))
            return new(flex, null);
        // Use normalized segments so that small and large avatars share the same straight-leg tolerance.
        var bend = upperDirection - axis * Vector3.Dot(upperDirection, axis);
        guide -= axis * Vector3.Dot(guide, axis);
        if (bend.LengthSquared() <= 0.000001f || guide.LengthSquared() <= 0.000001f)
            return new(flex, null);
        return new(flex, Math.Clamp(Vector3.Dot(Vector3.Normalize(bend), Vector3.Normalize(guide)), -1f, 1f));
    }

    /// <summary>計測不能な値をゼロと区別して、ロケールに依存しないログを作ります。</summary>
    public string Summary =>
        $"flex={(FlexDegrees.HasValue ? FlexDegrees.Value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "deg" : "n/a")} " +
        $"guideDot={(GuideDot.HasValue ? GuideDot.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "n/a")}";

    /// <summary>ゼロ長・非有限のベクトルを除外して単位方向を求めます。</summary>
    /// <param name="value">検査するベクトル。</param>
    /// <param name="direction">正常時の単位ベクトル。無効時はゼロ。</param>
    /// <returns>有限の単位方向が求まった場合はtrue。</returns>
    private static bool TryNormalize(Vector3 value, out Vector3 direction)
    {
        var lengthSquared = value.LengthSquared();
        direction = default;
        if (!float.IsFinite(lengthSquared) || lengthSquared < 1e-12f) return false;
        direction = value / MathF.Sqrt(lengthSquared);
        return true;
    }
}
