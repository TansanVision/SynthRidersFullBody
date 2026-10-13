using System.Numerics;

namespace SynthRidersFullBody;

/// <summary>
/// 自動割り当てに用いるデバイス識別キーと追跡空間内の位置です。
/// </summary>
/// <param name="Key">取得元と個体を区別する識別キー。</param>
/// <param name="Position">候補個体の追跡空間内の位置。</param>
internal readonly record struct BodyCandidate(string Key, Vector3 Position);

/// <summary>
/// 直立したユーザーの位置関係から腰・足首・足先の候補を推定します。
/// </summary>
internal static class AutoBodyAssignment
{
    // Suggestions for an upright user with feet apart and arms down. Ambiguous
    // elbows/knees/head references remain manual; this is never run in the solve loop.
    /// <summary>
    /// 頭付近の個体や曖昧な候補を除外し、左右と高さから部位の候補を返します。
    /// </summary>
    /// <param name="devices">同じ追跡空間に属する個体の識別キーと位置。</param>
    /// <param name="head">候補デバイスと同じ追跡空間でのHMD位置。</param>
    /// <param name="forward">入力位置と同じ座標系におけるユーザーまたはアバターの正面方向。</param>
    /// <returns>位置条件から候補を絞れた部位とデバイスキーの対応。候補がない場合は空。</returns>
    public static Dictionary<TrackerRole, string> Suggest(IEnumerable<BodyCandidate> devices,
        Vector3 head, Vector3 forward)
    {
        var result = new Dictionary<TrackerRole, string>();
        forward.Y = 0f;
        if (!float.IsFinite(forward.LengthSquared()) || forward.LengthSquared() < 0.001f) return result;
        var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        var candidates = devices.Where(d => TrackerAssignmentPolicy.IsBodyCandidate(head, d.Position) &&
            new Vector2(d.Position.X - head.X, d.Position.Z - head.Z).Length() < 0.75f).ToArray();
        if (candidates.Length == 0) return result;
        var floor = candidates.Min(d => d.Position.Y);
        var height = head.Y - floor;
        if (height < 0.8f) return result;
        var feet = candidates.Where(d => d.Position.Y <= floor + Math.Min(0.20f, height * 0.16f)).ToArray();
        var left = feet.Where(d => Vector3.Dot(d.Position - head, right) < -0.04f).ToArray();
        var rightFoot = feet.Where(d => Vector3.Dot(d.Position - head, right) > 0.04f).ToArray();
        // A partial startup sample must not promote a knee or waist to a foot.
        if (left.Length == 0 || rightFoot.Length == 0) return result;
        AddFoot(left, TrackerRole.LeftFoot, TrackerRole.LeftToes);
        AddFoot(rightFoot, TrackerRole.RightFoot, TrackerRole.RightToes);
        var waist = candidates.Where(d => d.Position.Y > floor + height * 0.45f &&
            d.Position.Y < floor + height * 0.80f &&
            Math.Abs(Vector3.Dot(d.Position - head, right)) < 0.22f).ToArray();
        if (waist.Length == 1) result[TrackerRole.Waist] = waist[0].Key;
        return result;

        void AddFoot(BodyCandidate[] side, TrackerRole ankleRole, TrackerRole toeRole)
        {
            if (side.Length == 1) { result[ankleRole] = side[0].Key; return; }
            if (side.Length != 2) return;
            var sorted = side.OrderBy(d => d.Position.Y).ToArray();
            if (sorted[1].Position.Y - sorted[0].Position.Y < 0.035f ||
                Vector3.Distance(sorted[0].Position, sorted[1].Position) > 0.35f) return;
            result[ankleRole] = sorted[1].Key;
            result[toeRole] = sorted[0].Key;
        }
    }
}
