using System.Numerics;

namespace SynthRidersFullBody;

/// <summary>校正した装着位置から求めた足首と、VRIKへ渡す終端のワールド姿勢です。</summary>
/// <param name="AnklePosition">計測位置に装着オフセットだけを加えた足首位置。</param>
/// <param name="AnkleRotation">装着回転と、利用可能なら独立した足先位置から求めた足首回転。</param>
/// <param name="EndPosition">足首または足先の終端位置。</param>
/// <param name="EndRotation">終端ボーンの軸に合わせた回転。</param>
internal readonly record struct TrackedFootPose(Vector3 AnklePosition, Quaternion AnkleRotation,
    Vector3 EndPosition, Quaternion EndRotation)
{
    /// <summary>入力の足位置を体格や持ち上げ量で引き伸ばさず、校正した剛体オフセットを適用します。</summary>
    /// <param name="position">主入力トラッカーの位置。</param>
    /// <param name="rotation">主入力トラッカーの回転。</param>
    /// <param name="mount">トラッカーから足首への校正オフセット。</param>
    /// <param name="endpoint">足首からソルバー終端への校正オフセット。</param>
    /// <param name="independentToe">別の足先入力で求めた足先ボーン位置。片足1台ならnull。</param>
    /// <returns>足首位置を維持した、足首と終端の目標姿勢。</returns>
    public static TrackedFootPose FromTracker(Vector3 position, Quaternion rotation,
        RigidPoseOffset mount, RigidPoseOffset endpoint, Vector3? independentToe = null)
    {
        var ankle = mount.Apply(position, rotation);
        var ankleRotation = independentToe.HasValue
            ? DualFootMath.Rotation(ankle.Position, ankle.Rotation, endpoint.Position, independentToe.Value)
            : ankle.Rotation;
        var end = endpoint.Apply(ankle.Position, ankleRotation);
        return new(ankle.Position, ankleRotation, end.Position, end.Rotation);
    }
}
