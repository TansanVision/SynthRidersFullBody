using System.Numerics;

namespace SynthRidersFullBody;

/// <summary>
/// 保存した校正の拡縮互換性と、追跡原点変更時の座標変換を計算します。
/// </summary>
internal static class CalibrationRecoveryMath
{
    /// <summary>
    /// 各軸が正で有限、かつ基準からの拡縮率が各軸でほぼ等しいかを確認します。
    /// </summary>
    /// <param name="reference">比較基準となるモデルルートのワールドスケール。</param>
    /// <param name="current">現在のモデルルートのワールドスケール。</param>
    /// <returns>最大と最小の拡縮率の比が1.01未満ならtrue。</returns>
    public static bool CompatibleScale(Vector3 reference, Vector3 current)
    {
        if (!Positive(reference) || !Positive(current)) return false;
        var ratio = current / reference;
        var min = Math.Min(ratio.X, Math.Min(ratio.Y, ratio.Z));
        var max = Math.Max(ratio.X, Math.Max(ratio.Y, ratio.Z));
        return max / min < 1.01f;
    }

    /// <summary>
    /// 旧原点を基準にした相対姿勢を、新しい追跡原点へ移します。
    /// </summary>
    /// <param name="position">旧ワールド空間での変換対象位置。</param>
    /// <param name="rotation">位置と同じ座標系で表した回転。</param>
    /// <param name="oldOrigin">保存時の追跡原点のワールド位置。</param>
    /// <param name="oldRotation">保存時の追跡空間からワールド空間への回転。</param>
    /// <param name="newOrigin">移動先の追跡原点のワールド位置。</param>
    /// <param name="newRotation">移動先の追跡空間からワールド空間への回転。</param>
    /// <returns>新しいワールド空間に移した位置と正規化回転。</returns>
    public static (Vector3 Position, Quaternion Rotation) Remap(Vector3 position, Quaternion rotation,
        Vector3 oldOrigin, Quaternion oldRotation, Vector3 newOrigin, Quaternion newRotation)
    {
        var delta = Quaternion.Normalize(newRotation * Quaternion.Inverse(oldRotation));
        return (newOrigin + Vector3.Transform(position - oldOrigin, delta), Quaternion.Normalize(delta * rotation));
    }

    /// <summary>
    /// スケールの全成分が有限で、ゼロに近すぎない正数かを確認します。
    /// </summary>
    /// <param name="v">変換または検証するベクトル。</param>
    /// <returns>全成分が0.00001より大きい有限値ならtrue。</returns>
    private static bool Positive(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) &&
        float.IsFinite(v.Z) && v.X > 0.00001f && v.Y > 0.00001f && v.Z > 0.00001f;
}

// Wait for the same initialized rig and scale, without requiring the user to
// hold a calibration pose. Loading often changes both over several frames.
/// <summary>
/// ロード中に変化する骨格とサイズが安定するまで、校正復帰を待機させます。
/// </summary>
internal sealed class CalibrationRecoveryGate
{
    private string? _candidate;
    private Vector3 _scale;
    private float _since;
    /// <summary>
    /// 直前の候補を破棄し、次の候補から安定時間の計測をやり直します。
    /// </summary>
    public void Reset() => _candidate = null;
    /// <summary>
    /// 候補識別子とスケールの安定時間を更新し、復帰可能かを判定します。
    /// </summary>
    /// <param name="candidate">現在のアバターとソルバーを区別する識別子。</param>
    /// <param name="scale">安定性を確認するモデルルートのワールドスケール。</param>
    /// <param name="now">現在時刻。蓄積時刻と同じ基準の秒数を使用します。</param>
    /// <returns>同じ候補と許容差内のスケールが0.5秒以上続いていればtrue。</returns>
    public bool Ready(string candidate, Vector3 scale, float now)
    {
        if (!CalibrationRecoveryMath.CompatibleScale(scale, scale)) { Reset(); return false; }
        if (_candidate != candidate || Vector3.Distance(_scale, scale) > Math.Max(0.001f, _scale.Length() * 0.002f))
        { _candidate = candidate; _scale = scale; _since = now; return false; }
        return now - _since >= 0.5f;
    }
}
