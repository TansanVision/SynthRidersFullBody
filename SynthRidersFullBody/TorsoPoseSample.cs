using System.Numerics;

namespace SynthRidersFullBody;

/// <summary>同一計算時点で採取した体幹の相対位置。各成分は右・上・正面の順、単位はmです。</summary>
internal readonly record struct TorsoPoseSample(Vector3? HeadFromHips, Vector3? HeadFromChest,
    Vector3? NeckFromChest, Vector3? HmdFromEyes)
{
    /// <summary>基準姿勢と計算後で比較できるよう、骨の位置差を体の水平軸へ変換します。</summary>
    public static TorsoPoseSample Capture(Vector3 hips, Vector3? chest, Vector3? neck, Vector3? head,
        Vector3? eyes, Vector3? hmd, Vector3 forward)
    {
        forward.Y = 0;
        forward = forward.LengthSquared() > .000001f ? Vector3.Normalize(forward) : Vector3.UnitZ;
        var right = Vector3.Cross(Vector3.UnitY, forward);
        /// <summary>両端が存在する位置差だけを水平な体の座標へ射影します。</summary>
        Vector3? Delta(Vector3? from, Vector3? to)
        {
            if (from is not { } a || to is not { } b) return null;
            var d = b - a;
            return new Vector3(Vector3.Dot(d, right), d.Y, Vector3.Dot(d, forward));
        }
        return new(Delta(hips, head), Delta(chest, head), Delta(chest, neck), Delta(eyes, hmd));
    }

    /// <summary>欠測とゼロを区別し、解剖学的な異常の判定は行わず測定値を表示します。</summary>
    public string Summary => $"head-hips={Format(HeadFromHips)}, head-chest={Format(HeadFromChest)}, " +
        $"neck-chest={Format(NeckFromChest)}, HMD-eyes={Format(HmdFromEyes)}; axes=right/up/front m";

    /// <summary>ログの小数点・桁数を固定し、存在しない骨はn/aとします。</summary>
    private static string Format(Vector3? value) => value is { } v
        ? FormattableString.Invariant($"({v.X:0.000},{v.Y:0.000},{v.Z:0.000})") : "n/a";
}
