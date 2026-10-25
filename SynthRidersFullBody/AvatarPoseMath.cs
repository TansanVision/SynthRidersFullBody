using System.Numerics;

namespace SynthRidersFullBody;

// Pure pose geometry, shared by the runtime and tests that do not load Unity/IL2CPP.
/// <summary>
/// Unityに依存しない、腰の旋回抽出と膝の曲げ方向・2ボーンIKの計算を提供します。
/// </summary>
internal static class AvatarPoseMath
{
    /// <summary>
    /// 直立基準の膝ガイドを求めます。ほぼ直線の脚や横・後方の張り出しを曲げ方向として増幅しません。
    /// </summary>
    /// <param name="hip">基準姿勢の股関節位置。</param>
    /// <param name="knee">基準姿勢の膝位置。</param>
    /// <param name="foot">基準姿勢の足首位置。</param>
    /// <param name="forward">校正時のアバター正面方向。</param>
    /// <returns>脚軸に直交し、正面から最大35度の基準方向。実際の膝トラッカー入力には使用しません。</returns>
    public static Vector3 CalibrationKneeBend(Vector3 hip, Vector3 knee, Vector3 foot, Vector3 forward)
    {
        var axis = Direction(foot - hip, -Vector3.UnitY);
        var front = Perpendicular(forward, axis);
        var upper = knee - hip;
        var projected = upper - axis * Vector3.Dot(upper, axis);
        var upperLength = upper.Length();
        if (!Finite(projected) || !float.IsFinite(upperLength) || upperLength < 0.000001f) return front;
        // Use a length ratio, not the old fixed 2 cm threshold. Otherwise the
        // same rest pose chooses a different knee direction after size fitting.
        var ratio = projected.Length() / upperLength;
        if (ratio <= 0.025f) return front;
        var measured = Vector3.Normalize(projected);
        var dot = Math.Clamp(Vector3.Dot(front, measured), -1f, 1f);
        if (dot < 0.5f) return front; // Lateral/reversed rest geometry is not a flexion hint.
        var side = measured - front * dot;
        var angle = MathF.Min(MathF.Acos(dot), 35f * MathF.PI / 180f);
        var limited = side.LengthSquared() < 0.000001f ? front :
            front * MathF.Cos(angle) + Vector3.Normalize(side) * MathF.Sin(angle);
        var trust = Math.Clamp((ratio - 0.025f) / 0.1f, 0f, 1f);
        trust *= trust * (3f - 2f * trust);
        return Direction(Vector3.Lerp(front, limited, trust), front);
    }

    /// <summary>
    /// 回転差からワールドY軸まわりの旋回を抽出し、前屈による向きの反転を避けます。
    /// </summary>
    /// <param name="delta">校正時から現在までの回転差。</param>
    /// <param name="previousYaw">旋回成分の計算が縮退した場合に維持する前回の回転。</param>
    /// <returns>正規化した水平旋回。計算が縮退する場合は前回値。</returns>
    public static Quaternion PelvisYaw(Quaternion delta, Quaternion previousYaw)
    {
        // Extract the twist about world up. Projecting the forward vector onto
        // the floor instead reverses the heading when a forward bend passes 90°.
        var lengthSquared = delta.Y * delta.Y + delta.W * delta.W;
        if (!float.IsFinite(lengthSquared) || lengthSquared < 0.000001f)
            return previousYaw;
        var inverseLength = 1f / MathF.Sqrt(lengthSquared);
        return new Quaternion(0f, delta.Y * inverseLength, 0f, delta.W * inverseLength);
    }

    /// <summary>
    /// 基準時の膝の曲げ方向を現在の脚軸へ運び、脚軸に直交する方向へ補正します。
    /// </summary>
    /// <param name="referenceAxis">校正時の股関節から足先側へ向かう脚軸。</param>
    /// <param name="referenceBend">校正時の膝の曲げ方向。</param>
    /// <param name="currentAxis">現在の股関節から足先側へ向かう脚軸。</param>
    /// <returns>現在の脚軸に直交する正規化された曲げ方向。</returns>
    public static Vector3 LegBend(Vector3 referenceAxis, Vector3 referenceBend,
        Vector3 currentAxis)
    {
        var from = Direction(referenceAxis, -Vector3.UnitY);
        var to = Direction(currentAxis, from);
        var bend = Perpendicular(referenceBend, from);
        var dot = Math.Clamp(Vector3.Dot(from, to), -1f, 1f);
        var rotation = dot < -0.9999f
            ? Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(from, bend)), MathF.PI)
            : Quaternion.Normalize(new Quaternion(Vector3.Cross(from, to), 1f + dot));
        // Transport the calibrated bend plane with the leg. A horizontal kick
        // then bends upward instead of switching to the avatar's sideways axis.
        return Perpendicular(Vector3.Transform(bend, rotation), to);
    }

    /// <summary>
    /// 骨長と膝の最大屈曲角から到達距離を制限し、膝と足の位置を計算します。
    /// </summary>
    /// <param name="hip">股関節の位置。足と膝の位置と同じ座標系です。</param>
    /// <param name="requestedFoot">要求する足首位置。股関節と同じ空間です。</param>
    /// <param name="fallbackAxis">足目標と股関節が重なる場合に使用する脚の方向。</param>
    /// <param name="bend">脚の曲がる側を指定する方向ベクトル。</param>
    /// <param name="upperLength">大腿の長さ。位置入力と同じ単位です。</param>
    /// <param name="lowerLength">下腿の長さ。位置入力と同じ単位です。</param>
    /// <param name="maxFlexionDegrees">膝の最大屈曲角。単位は度です。</param>
    /// <param name="knee">成功時に、股関節と同じ空間で計算した膝位置を受け取ります。</param>
    /// <param name="foot">成功時に、到達可能距離と屈曲角の制限を適用した足位置を受け取ります。</param>
    /// <returns>入力と計算結果が有効な場合はtrue。</returns>
    /// <remarks>すべての位置と長さは同じ空間・単位を使用します。要求目標が到達範囲外なら、骨長を伸ばさずに足目標を到達範囲へ制限します。</remarks>
    public static bool SolveTwoBone(Vector3 hip, Vector3 requestedFoot,
        Vector3 fallbackAxis, Vector3 bend, float upperLength, float lowerLength,
        float maxFlexionDegrees, out Vector3 knee, out Vector3 foot)
    {
        knee = foot = default;
        if (!Finite(hip) || !Finite(requestedFoot) || !Finite(fallbackAxis) || !Finite(bend) ||
            !float.IsFinite(upperLength) || !float.IsFinite(lowerLength) ||
            !float.IsFinite(maxFlexionDegrees) || upperLength < 0.01f || lowerLength < 0.01f)
            return false;
        var offset = requestedFoot - hip;
        var axis = Direction(offset, Direction(fallbackAxis, -Vector3.UnitY));
        var angle = Math.Clamp(maxFlexionDegrees, 60f, 170f) * (MathF.PI / 180f);
        var minReach = MathF.Sqrt(MathF.Max(0f, upperLength * upperLength +
            lowerLength * lowerLength + 2f * upperLength * lowerLength * MathF.Cos(angle)));
        var totalLength = upperLength + lowerLength;
        var maxReach = totalLength;
        minReach = MathF.Min(minReach, maxReach);
        var distance = Math.Clamp(offset.Length(), minReach, maxReach);
        // This analytic solve does not need a bent-knee epsilon to choose its
        // bend plane. Shortening reach by 0.1% imposed ~5.1 degrees of flexion,
        // including on a straight forward kick. Handle full extension directly
        // to avoid cancellation in the square root below.
        if (distance >= totalLength)
        {
            knee = hip + axis * upperLength;
            foot = hip + axis * totalLength;
            return Finite(knee) && Finite(foot);
        }
        var along = (upperLength * upperLength + distance * distance - lowerLength * lowerLength) /
            (2f * distance);
        var outwards = MathF.Sqrt(MathF.Max(0f, upperLength * upperLength - along * along));
        knee = hip + axis * along + Perpendicular(bend, axis) * outwards;
        foot = hip + axis * distance;
        return Finite(knee) && Finite(foot);
    }

    /// <summary>
    /// 指定軸に垂直な方向をヒントから作り、平行・無効なヒントには別の軸を代用します。
    /// </summary>
    /// <param name="hint">優先的に使う曲げ方向の候補。</param>
    /// <param name="axis">射影・方向計算の基準となる単位軸。</param>
    /// <returns>軸に直交する単位ベクトル。</returns>
    private static Vector3 Perpendicular(Vector3 hint, Vector3 axis)
    {
        var projected = hint - axis * Vector3.Dot(hint, axis);
        if (Finite(projected) && projected.LengthSquared() > 0.000001f)
            return Vector3.Normalize(projected);
        var fallback = MathF.Abs(axis.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitY;
        return Vector3.Normalize(fallback - axis * Vector3.Dot(fallback, axis));
    }

    /// <summary>
    /// 有効で十分な長さを持つベクトルを正規化し、それ以外では代替方向を返します。
    /// </summary>
    /// <param name="value">変換または検証する値。</param>
    /// <param name="fallback">入力が無効または短すぎる場合に使用する代替方向。</param>
    /// <returns>正規化した方向、または代替方向。</returns>
    private static Vector3 Direction(Vector3 value, Vector3 fallback) =>
        Finite(value) && value.LengthSquared() > 0.000001f ? Vector3.Normalize(value) : fallback;

    /// <summary>
    /// ベクトルの全成分が有限値かどうかを確認します。
    /// </summary>
    /// <param name="value">変換または検証する値。</param>
    /// <returns>NaNと無限大を含まなければtrue。</returns>
    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
