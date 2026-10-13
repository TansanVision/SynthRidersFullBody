using System.Numerics;

namespace SynthRidersFullBody;

// Calibrated ankle -> VRIK end bone (toe, if present). Keep positional and
// rotational offsets together so different VRM bone axes survive a full turn.
/// <summary>
/// 基準姿勢から目標姿勢までの、基準回転に対するローカル位置・回転オフセットです。
/// </summary>
/// <param name="Position">基準回転で表したローカル位置オフセット。</param>
/// <param name="Rotation">基準回転からの相対回転。</param>
internal readonly record struct RigidPoseOffset(Vector3 Position, Quaternion Rotation)
{
    /// <summary>
    /// 同じ空間の基準姿勢と目標姿勢から、再適用可能な剛体オフセットを求めます。
    /// </summary>
    /// <param name="origin">基準姿勢の位置。目標と同じ空間を使用します。</param>
    /// <param name="rotation">位置と同じ座標系で表した回転。</param>
    /// <param name="target">基準と同じ空間での目標位置。</param>
    /// <param name="targetRotation">基準と同じ空間で表した目標回転。</param>
    /// <returns>基準座標系で表した位置・回転オフセット。</returns>
    public static RigidPoseOffset Between(Vector3 origin, Quaternion rotation,
        Vector3 target, Quaternion targetRotation) =>
        new(Vector3.Transform(target - origin, Quaternion.Inverse(rotation)),
            Quaternion.Inverse(rotation) * targetRotation);

    /// <summary>
    /// 保存したローカルオフセットを、新しい基準姿勢へ適用します。
    /// </summary>
    /// <param name="origin">基準姿勢の位置。目標と同じ空間を使用します。</param>
    /// <param name="rotation">位置と同じ座標系で表した回転。</param>
    /// <returns>基準と同じ空間での目標位置と回転。</returns>
    public (Vector3 Position, Quaternion Rotation) Apply(Vector3 origin, Quaternion rotation) =>
        (origin + Vector3.Transform(Position, rotation), rotation * Rotation);
}
