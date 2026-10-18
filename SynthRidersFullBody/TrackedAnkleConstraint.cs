using System.Numerics;

namespace SynthRidersFullBody;

/// <summary>足先終端のIKが到達不足を足首の傾きで補った場合の、骨長を保つ回転補正です。</summary>
/// <param name="Upper">補正後の大腿のワールド回転。</param>
/// <param name="Lower">補正後の下腿のワールド回転。</param>
/// <param name="Ankle">補正後の足首のワールド回転。</param>
/// <param name="Weight">到達限界とIK重みに応じた実際の補正率。</param>
/// <param name="Excess">脚長を超えた足首目標の距離。単位はm。</param>
internal readonly record struct AnkleConstraintResult(Quaternion Upper, Quaternion Lower,
    Quaternion Ankle, float Weight, float Excess);

/// <summary>届かない足首目標を脚長内へ制限し、計測した足の向きを維持します。</summary>
internal static class TrackedAnkleConstraint
{
    /// <summary>到達範囲外の足先終端だけを2ボーンで解き直し、境界から滑らかに適用します。</summary>
    /// <param name="hip">VRIK計算直後の股関節位置。</param>
    /// <param name="knee">同じ時点の膝位置。</param>
    /// <param name="ankle">同じ時点の足首位置。</param>
    /// <param name="upperRotation">大腿のワールド回転。</param>
    /// <param name="lowerRotation">下腿のワールド回転。</param>
    /// <param name="ankleRotation">足首のワールド回転。</param>
    /// <param name="target">装着オフセットと独立足先入力を反映済みの目標。</param>
    /// <param name="bend">膝の曲がる方向。膝トラッカーがあればそのガイドを使用します。</param>
    /// <param name="hasToeEndpoint">足首より先にVRIKの終端があるか。</param>
    /// <param name="weight">この足の追跡、IK全体、脚の位置・回転重みを合わせた適用率。</param>
    /// <param name="maxFlexion">膝の最大屈曲角。</param>
    /// <param name="result">成功時の補正回転と到達不足量。</param>
    /// <returns>有効な到達範囲外の足に補正を計算した場合はtrue。</returns>
    /// <remarks>到達可能な通常の膝解法は維持します。床へ向きを固定せず、実際の足上げ・つま先立ちも入力どおりに扱います。</remarks>
    public static bool TrySolve(Vector3 hip, Vector3 knee, Vector3 ankle,
        Quaternion upperRotation, Quaternion lowerRotation, Quaternion ankleRotation,
        TrackedFootPose target, Vector3 bend, bool hasToeEndpoint, float weight, float maxFlexion,
        out AnkleConstraintResult result)
    {
        result = default;
        if (!hasToeEndpoint || !float.IsFinite(weight) || weight <= 0f ||
            !Valid(upperRotation) || !Valid(lowerRotation) || !Valid(ankleRotation) || !Valid(target.AnkleRotation)) return false;
        var upperLength = Vector3.Distance(hip, knee);
        var lowerLength = Vector3.Distance(knee, ankle);
        var length = upperLength + lowerLength;
        var excess = Vector3.Distance(hip, target.AnklePosition) - length;
        if (!float.IsFinite(excess) || excess <= 0f) return false;
        if (!AvatarPoseMath.SolveTwoBone(hip, target.AnklePosition, ankle - hip, bend,
                upperLength, lowerLength, maxFlexion, out var solvedKnee, out var solvedAnkle)) return false;
        // A hard switch at reach=1 would introduce a knee/ankle pop. Fade across
        // the first 2% of leg length; this also scales with the fitted avatar.
        var blend = Math.Clamp(excess / (length * .02f), 0f, 1f);
        blend = blend * blend * (3f - 2f * blend) * Math.Clamp(weight, 0f, 1f);
        var upperDelta = FromTo(knee - hip, solvedKnee - hip);
        var lowerDelta = FromTo(Vector3.Transform(ankle - knee, upperDelta), solvedAnkle - solvedKnee);
        result = new(Quaternion.Slerp(Quaternion.Normalize(upperRotation),
                Quaternion.Normalize(upperDelta * upperRotation), blend),
            Quaternion.Slerp(Quaternion.Normalize(lowerRotation),
                Quaternion.Normalize(lowerDelta * upperDelta * lowerRotation), blend),
            Quaternion.Slerp(Quaternion.Normalize(ankleRotation), Quaternion.Normalize(target.AnkleRotation), blend),
            blend, excess);
        return true;
    }

    /// <summary>有効な骨ベクトルを最短回転で重ねます。180度の場合も有限な回転を返します。</summary>
    private static Quaternion FromTo(Vector3 from, Vector3 to)
    {
        from = Vector3.Normalize(from);
        to = Vector3.Normalize(to);
        var dot = Math.Clamp(Vector3.Dot(from, to), -1f, 1f);
        if (dot < -.999999f)
        {
            var basis = MathF.Abs(from.X) < .9f ? Vector3.UnitX : Vector3.UnitY;
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(from, basis)), MathF.PI);
        }
        return Quaternion.Normalize(new Quaternion(Vector3.Cross(from, to), 1f + dot));
    }

    /// <summary>有限で正規化可能なボーン回転だけを受け付けます。</summary>
    private static bool Valid(Quaternion value) => float.IsFinite(value.LengthSquared()) && value.LengthSquared() > 1e-12f;
}
