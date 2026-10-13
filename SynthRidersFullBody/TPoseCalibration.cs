using UnityEngine;

namespace SynthRidersFullBody;

/// <summary>
/// Unityのワールド姿勢を校正の共通判定へ渡します。
/// </summary>
internal static class TPoseCalibration
{
    /// <summary>
    /// 腰・足・視線・手を検証し、StandingまたはTポーズの校正条件を確認します。
    /// </summary>
    /// <param name="camera">ゲーム内のHMD視点として使用するカメラ。</param>
    /// <param name="calibrationForward">校正開始時に固定したワールド空間の正面方向。</param>
    /// <param name="hasHands">左右の手の姿勢を取得できているか。</param>
    /// <param name="leftHand">左手のワールド姿勢。</param>
    /// <param name="rightHand">右手のワールド姿勢。</param>
    /// <param name="hasTrackers">腰と左右足の主入力をすべて追跡できているか。</param>
    /// <param name="waist">腰のワールド姿勢。</param>
    /// <param name="leftFoot">左足の主入力のワールド姿勢。</param>
    /// <param name="rightFoot">右足の主入力のワールド姿勢。</param>
    /// <param name="requireTPose">腕の条件も検証するか。falseならStandingです。</param>
    /// <returns>校正可否と、最初に満たさない条件の説明。</returns>
    public static TPoseAssessment Check(Camera camera, Vector3 calibrationForward,
        bool hasHands, Pose leftHand, Pose rightHand,
        bool hasTrackers, Pose waist, Pose leftFoot, Pose rightFoot, bool requireTPose = true) =>
        CalibrationPoseCheck.Check(ToNumerics(camera.transform.position), ToNumerics(camera.transform.forward),
            ToNumerics(calibrationForward), hasHands, ToNumerics(leftHand.Position), ToNumerics(rightHand.Position),
            hasTrackers, ToNumerics(waist.Position), ToNumerics(leftFoot.Position), ToNumerics(rightFoot.Position), requireTPose);

    /// <summary>
    /// 最後に判定したフレームの座標・正面・足幅を診断文にします。欠測は呼び出し側で明記します。
    /// </summary>
    /// <param name="camera">ゲーム内のHMD視点。</param>
    /// <param name="front">固定した校正の正面。</param>
    /// <param name="frame">このフレームで判定に使った腰と左右足の姿勢。</param>
    /// <returns>姿勢チェックの数値と入力座標。</returns>
    public static string Diagnostics(Camera camera, Vector3 front, TrackerFrame frame) =>
        CalibrationPoseCheck.Diagnostics(ToNumerics(camera.transform.position), ToNumerics(camera.transform.forward),
            ToNumerics(front), ToNumerics(frame.Waist.Position), ToNumerics(frame.Left.Position), ToNumerics(frame.Right.Position));

    /// <summary>
    /// 軸と単位を変更せず、Unityのベクトルを計算用ベクトルへ写します。
    /// </summary>
    /// <param name="value">変換するベクトル。</param>
    /// <returns>同じXYZ成分の計算用ベクトル。</returns>
    internal static System.Numerics.Vector3 ToNumerics(Vector3 value) => new(value.x, value.y, value.z);
}
