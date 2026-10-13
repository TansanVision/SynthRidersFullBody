using System.Numerics;

namespace SynthRidersFullBody;

/// <summary>
/// 頭付近の個体除外と候補数に関する、追跡部位の自動割り当て条件を提供します。
/// </summary>
internal static class TrackerAssignmentPolicy
{
    /// <summary>
    /// 未設定部位が存在し、候補数がその部位数と一致するかを確認します。
    /// </summary>
    /// <param name="candidates">自動割り当て候補の個数。</param>
    /// <param name="unassignedRoles">まだデバイスを割り当てていない部位数。</param>
    /// <returns>候補を余りなく未設定部位へ対応付けられる数ならtrue。</returns>
    public static bool CanFillAutomatically(int candidates, int unassignedRoles) =>
        unassignedRoles > 0 && candidates == unassignedRoles;

    // A fourth tracker on the headset is commonly used to align mixed spaces.
    // Automatic body assignment must not mistake it for the highest body tracker.
    // Evaluate only for NEW assignments; crouching/turning must not reassign roles.
    /// <summary>
    /// HMDに装着した位置合わせ用トラッカーを、頭からの距離と高さの差で除外します。
    /// </summary>
    /// <param name="head">ほかの位置入力と同じ座標系で表した頭の位置。</param>
    /// <param name="tracker">候補トラッカーの位置。頭と同じ追跡空間です。</param>
    /// <returns>有限な位置で、頭より0.25m以上低く0.35m以上離れていればtrue。</returns>
    public static bool IsBodyCandidate(Vector3 head, Vector3 tracker) =>
        IsFinite(head) && IsFinite(tracker) &&
        head.Y - tracker.Y >= 0.25f && Vector3.Distance(head, tracker) >= 0.35f;

    /// <summary>
    /// ベクトルの全成分が有限値かを確認します。
    /// </summary>
    /// <param name="value">変換または検証する値。</param>
    /// <returns>NaNと無限大を含まなければtrue。</returns>
    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
