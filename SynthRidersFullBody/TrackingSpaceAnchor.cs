using UnityEngine;

namespace SynthRidersFullBody;

/// <summary>立位追跡空間とゲーム空間の固定原点。シーン間でもAPI同士の登録を保持します。</summary>
internal readonly record struct TrackingSpaceAnchor(Vector3 Translation, Quaternion Rotation)
{
    /// <summary>
    /// 生HMDとゲーム内カメラの位置・水平方向を合わせ、頭の傾きの差を床に混ぜません。
    /// </summary>
    /// <param name="head">取得元APIの追跡空間で計測したHMDの生姿勢。</param>
    /// <param name="camera">ゲーム内のHMD視点として使用するカメラ。</param>
    /// <returns>追跡空間をゲーム空間へ合わせる平行移動と回転。</returns>
    public static TrackingSpaceAnchor FromHead(Pose head, Camera camera)
    {
        // Both APIs expose a standing space. A stale/predicted head rotation
        // must not become a permanent pitch/roll of the entire tracker floor.
        var level = LevelTrackingSpace.Rotation(new System.Numerics.Quaternion(head.Rotation.x, head.Rotation.y, head.Rotation.z, head.Rotation.w),
            new System.Numerics.Quaternion(camera.transform.rotation.x, camera.transform.rotation.y, camera.transform.rotation.z, camera.transform.rotation.w));
        var rotation = new Quaternion(level.X, level.Y, level.Z, level.W);
        return new TrackingSpaceAnchor(camera.transform.position - rotation * head.Position,
            rotation);
    }

    /// <summary>
    /// 追跡空間の姿勢へ固定した原点変換を適用します。
    /// </summary>
    /// <param name="raw">取得元APIの追跡空間で表した生姿勢。</param>
    /// <returns>ゲームのワールド空間の姿勢。</returns>
    public Pose ToWorld(Pose raw) =>
        new(Translation + Rotation * raw.Position, Rotation * raw.Rotation);

    /// <summary>ゲーム側原点の移動をこのAPIにも適用し、API間の登録変換を変えません。</summary>
    public TrackingSpaceAnchor Rebase(TrackingSpaceAnchor previous, TrackingSpaceAnchor current)
    {
        var delta = current.Rotation * Quaternion.Inverse(previous.Rotation);
        return new(current.Translation + delta * (Translation - previous.Translation), delta * Rotation);
    }
}

