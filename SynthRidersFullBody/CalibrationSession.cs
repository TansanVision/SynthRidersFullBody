using UnityEngine;

namespace SynthRidersFullBody;

/// <summary>
/// 校正時のボーン相対パスとローカル位置・回転を、Unity参照なしで保存する値です。
/// </summary>
/// <param name="Path">Animatorルートからボーンまでの相対パス。</param>
/// <param name="Position">ボーンのローカル位置。</param>
/// <param name="Rotation">ボーンのローカル回転。</param>
internal readonly record struct CalibrationBone(string Path, Vector3 Position, Quaternion Rotation);

// Values only: no reference to the destroyed avatar, camera, or solver. Retain
// the ORIGINAL calibration; scene recovery must never learn the player's live pose.
/// <summary>
/// 同じゲーム起動中のシーン移動に備えて、元の校正姿勢と入力・追跡空間を保存します。
/// </summary>
/// <param name="AvatarKey">同じアバターを識別する保存キー。</param>
/// <param name="RigSignature">保存時の骨格構成を識別する文字列。</param>
/// <param name="Bones">校正時の各ボーンの相対パスとローカル姿勢。</param>
/// <param name="Root">校正時のモデルルートのワールド姿勢。</param>
/// <param name="Scale">校正時に確定したモデルルートのワールドスケール。</param>
/// <param name="Waist">腰のワールド姿勢。</param>
/// <param name="Left">左足のワールド姿勢。</param>
/// <param name="Right">右足のワールド姿勢。</param>
/// <param name="Additional">部位ごとの校正時のワールド姿勢。</param>
/// <param name="SpaceSource">校正の腰入力が使用した取得元APIの識別名。</param>
/// <param name="Space">校正時の追跡空間からワールド空間への固定変換。</param>
/// <param name="Headset">校正時のHMD姿勢。復帰時の現在姿勢やゲームの中間目標から取り直しません。</param>
internal sealed record CalibrationSession(string AvatarKey, string RigSignature,
    CalibrationBone[] Bones, Pose Root, Vector3 Scale, Pose Waist, Pose Left, Pose Right,
    Dictionary<TrackerRole, Pose> Additional, string SpaceSource, TrackingSpaceAnchor Space, Pose? Headset = null)
{
    /// <summary>
    /// 保存した姿勢を、校正時の追跡原点から現在の追跡原点へ移します。
    /// </summary>
    /// <param name="pose">処理対象の位置・回転、または取得に成功したときの姿勢。</param>
    /// <param name="current">復帰先の追跡空間からワールド空間への固定変換。</param>
    /// <returns>現在のゲーム空間へ変換した姿勢。</returns>
    public Pose Remap(Pose pose, TrackingSpaceAnchor current)
    {
        var result = CalibrationRecoveryMath.Remap(N(pose.Position), N(pose.Rotation),
            N(Space.Translation), N(Space.Rotation), N(current.Translation), N(current.Rotation));
        return new Pose(new Vector3(result.Position.X, result.Position.Y, result.Position.Z),
            new Quaternion(result.Rotation.X, result.Rotation.Y, result.Rotation.Z, result.Rotation.W));
    }

    /// <summary>
    /// 現在のモデルサイズが保存時からの均等な拡縮として扱えるかを調べます。
    /// </summary>
    /// <param name="current">現在のモデルルートのワールドスケール。</param>
    /// <returns>有効な均等拡縮であればtrue。</returns>
    public bool MatchesScale(Vector3 current) => CalibrationRecoveryMath.CompatibleScale(N(Scale), N(current));
    /// <summary>
    /// Unityの数学型を、成分の値を保ってSystem.Numericsの型へ変換します。
    /// </summary>
    /// <param name="v">変換または検証するベクトル。</param>
    /// <returns>同じ成分を持つSystem.Numericsの値。</returns>
    private static System.Numerics.Vector3 N(Vector3 v) => new(v.x, v.y, v.z);
    /// <summary>
    /// Unityの数学型を、成分の値を保ってSystem.Numericsの型へ変換します。
    /// </summary>
    /// <param name="q">変換または検証するQuaternion。</param>
    /// <returns>同じ成分を持つSystem.Numericsの値。</returns>
    private static System.Numerics.Quaternion N(Quaternion q) => new(q.x, q.y, q.z, q.w);
}
