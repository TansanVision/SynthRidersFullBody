namespace SynthRidersFullBody;

// Resolve collisions from the original samples simultaneously. Sequentially
// clearing flags can accidentally leave one role valid in a three-way collision.
/// <summary>
/// 同じ個体の重複割り当てを除外した、腰と左右足の追跡可否です。
/// </summary>
/// <param name="Waist">腰の追跡が有効か。</param>
/// <param name="Left">左足の追跡が有効か。</param>
/// <param name="Right">右足の追跡が有効か。</param>
internal readonly record struct TrackerAvailability(bool Waist, bool Left, bool Right)
{
    /// <summary>
    /// 元の追跡状態を同時に比較し、同じデバイス番号が衝突する部位を無効化します。
    /// </summary>
    /// <param name="waistId">腰に割り当てたデバイス番号。</param>
    /// <param name="leftId">左足に割り当てたデバイス番号。</param>
    /// <param name="rightId">右足に割り当てたデバイス番号。</param>
    /// <param name="waist">重複チェック前の腰の追跡可否。</param>
    /// <param name="left">重複チェック前の左足の追跡可否。</param>
    /// <param name="right">重複チェック前の右足の追跡可否。</param>
    /// <returns>衝突した双方を無効にした追跡可否。</returns>
    public static TrackerAvailability Distinct(ulong waistId, ulong leftId, ulong rightId,
        bool waist, bool left, bool right)
    {
        var waistLeft = waist && left && waistId == leftId;
        var waistRight = waist && right && waistId == rightId;
        var leftRight = left && right && leftId == rightId;
        return new(waist && !waistLeft && !waistRight,
            left && !waistLeft && !leftRight, right && !waistRight && !leftRight);
    }
}
