using System.Numerics;

namespace SynthRidersFullBody;

/// <summary>ソルバー直後の足首・終端誤差と脚の到達率を、後続の骨格更新に依存せず保存します。</summary>
/// <param name="AnkleError">足首の実位置から目標までの距離。単位はm。</param>
/// <param name="AnkleAngle">足首の実回転と目標の角度差。</param>
/// <param name="EndAngle">足先補正後の終端回転とVRIK目標の角度差。</param>
/// <param name="Reach">股関節から足首目標までの距離／現在の大腿・下腿の合計長。</param>
/// <param name="TargetSlope">足首から終端への目標方向の仰角。足首終端ではnull。</param>
/// <param name="SolvedSlope">実際の足首から終端への仰角。足首終端ではnull。</param>
internal readonly record struct FootPoseSample(float? AnkleError, float? AnkleAngle, float? EndAngle,
    float? Reach, float? TargetSlope, float? SolvedSlope)
{
    /// <summary>その時点の大腿・下腿の合計長。ユーザーの実脚長ではありません。</summary>
    public float? LegLength { get; init; } = null;
    /// <summary>同じソルバー結果の股関節から、入力された足首目標までの距離です。</summary>
    public float? TargetDistance { get; init; } = null;
    /// <summary>現在の骨長と目標距離だけから要求される膝の屈曲角。ユーザーの実姿勢の推定値ではありません。</summary>
    public float? TargetKneeFlex { get; init; } = null;

    /// <summary>同じ計算フレームの関節・目標だけを使用して、足首と足先を区別して計測します。</summary>
    /// <param name="hip">股関節位置。</param>
    /// <param name="knee">膝位置。</param>
    /// <param name="ankle">足首位置。</param>
    /// <param name="ankleRotation">足首回転。</param>
    /// <param name="end">終端位置。</param>
    /// <param name="endRotation">補正後の終端回転。</param>
    /// <param name="target">この計算へ渡した足の目標。</param>
    /// <returns>骨格が無効なら未計測を表す値。</returns>
    public static FootPoseSample Capture(Vector3 hip, Vector3 knee, Vector3 ankle, Quaternion ankleRotation,
        Vector3 end, Quaternion endRotation, TrackedFootPose target)
    {
        var upperLength = Vector3.Distance(hip, knee);
        var lowerLength = Vector3.Distance(knee, ankle);
        var length = upperLength + lowerLength;
        if (!Finite(hip) || !Finite(knee) || !Finite(ankle) || !Finite(end) ||
            !Finite(target.AnklePosition) || !Finite(target.EndPosition) || !float.IsFinite(length) || length < .000001f)
            return default;
        var distance = Vector3.Distance(hip, target.AnklePosition);
        float? targetFlex = upperLength > .000001f && lowerLength > .000001f
            ? (float)(Math.Acos(Math.Clamp(((double)distance * distance -
                (double)upperLength * upperLength - (double)lowerLength * lowerLength) /
                (2.0 * upperLength * lowerLength), -1.0, 1.0)) * 180.0 / Math.PI)
            : null;
        return new(Vector3.Distance(ankle, target.AnklePosition), Angle(ankleRotation, target.AnkleRotation),
            Angle(endRotation, target.EndRotation), distance / length,
            Slope(target.EndPosition - target.AnklePosition), Slope(end - ankle))
        {
            LegLength = length,
            TargetDistance = distance,
            TargetKneeFlex = targetFlex
        };
    }

    /// <summary>ゼロの誤差と未計測を区別し、ゲーム言語に依存しない書式で表します。</summary>
    public string Summary => $"ankle error={Number(AnkleError, "0.000")}m/{Number(AnkleAngle, "0.#")}deg; " +
        $"end rotation error={Number(EndAngle, "0.#")}deg; reach={Number(Reach, "0.000")}; " +
        $"foot slope target/solved={Number(TargetSlope, "0.#")}/{Number(SolvedSlope, "0.#")}deg; " +
        $"target knee flex={Number(TargetKneeFlex, "0.#")}deg; " +
        $"leg length={Number(LegLength, "0.000")}m; hip-to-ankle target={Number(TargetDistance, "0.000")}m";

    /// <summary>足首から足先へ向かうボーン方向の仰角。足裏や床面の角度とは区別します。</summary>
    private static float? Slope(Vector3 axis) => axis.LengthSquared() < 1e-12f ? null :
        MathF.Atan2(axis.Y, MathF.Sqrt(axis.X * axis.X + axis.Z * axis.Z)) * 180f / MathF.PI;

    /// <summary>符号とQuaternionの長さに依存しない最短角度差を求めます。</summary>
    private static float? Angle(Quaternion a, Quaternion b)
    {
        var lengthA = a.LengthSquared();
        var lengthB = b.LengthSquared();
        if (!float.IsFinite(lengthA) || !float.IsFinite(lengthB) || lengthA < 1e-12f || lengthB < 1e-12f) return null;
        return 2f * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b))), 0f, 1f)) * 180f / MathF.PI;
    }

    /// <summary>未計測をn/aとし、小数点に常にピリオドを使用します。</summary>
    private static string Number(float? value, string format) => value?.ToString(format, System.Globalization.CultureInfo.InvariantCulture) ?? "n/a";
    /// <summary>位置の全成分が有限かを調べます。</summary>
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
