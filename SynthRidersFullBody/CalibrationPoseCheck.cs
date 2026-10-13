using System.Globalization;
using System.Numerics;

namespace SynthRidersFullBody;

/// <summary>
/// 校正姿勢の判定結果と、ユーザーへ表示する理由です。
/// </summary>
/// <param name="Ready">現在の姿勢で校正を確定できるか。</param>
/// <param name="Status">ユーザーへ表示する姿勢判定の説明。</param>
/// <param name="Warning">校正を停止しない足の配置の警告。問題がなければnullです。</param>
internal readonly record struct TPoseAssessment(bool Ready, string Status, string? Warning = null);

/// <summary>
/// 機種に依存せず、同じワールド座標系の入力から校正の正面・身体・腕の条件を判定します。
/// </summary>
internal static class CalibrationPoseCheck
{
    /// <summary>
    /// 正面と視線を確認してから部位の位置関係を調べ、誤った向きを左右逆と断定しないようにします。
    /// </summary>
    /// <param name="head">HMDのワールド位置。</param>
    /// <param name="headForward">HMDのワールド前方ベクトル。</param>
    /// <param name="front">開始時に固定した校正の正面方向。</param>
    /// <param name="hasHands">左右の手を両方追跡できているか。</param>
    /// <param name="leftHand">左手のワールド位置。</param>
    /// <param name="rightHand">右手のワールド位置。</param>
    /// <param name="hasTrackers">腰と左右足をすべて追跡できているか。</param>
    /// <param name="waist">腰のワールド位置。</param>
    /// <param name="leftFoot">左足の主入力のワールド位置。</param>
    /// <param name="rightFoot">右足の主入力のワールド位置。</param>
    /// <param name="requireTPose">腕の広がりと高さも検証するか。falseはStandingです。</param>
    /// <returns>校正可否、停止理由または準備状態、および停止させない足の配置の警告。</returns>
    /// <remarks>左右逆と足幅不足は警告のみです。追跡・視線・腰の高さ・Tポーズの腕の条件は引き続き検証します。</remarks>
    public static TPoseAssessment Check(Vector3 head, Vector3 headForward, Vector3 front,
        bool hasHands, Vector3 leftHand, Vector3 rightHand, bool hasTrackers,
        Vector3 waist, Vector3 leftFoot, Vector3 rightFoot, bool requireTPose)
    {
        if (!hasTrackers) return new(false, "Waist / left foot / right foot not tracked");
        if (!Finite(head) || !Finite(headForward) || headForward.LengthSquared() < 0.01f ||
            !Finite(front) || !Finite(waist) || !Finite(leftFoot) || !Finite(rightFoot))
            return new(false, "Invalid tracking pose; check devices / space alignment");
        var forward = Horizontal(front);
        if (forward.LengthSquared() < 0.01f) forward = Horizontal(headForward);
        if (forward.LengthSquared() < 0.01f)
            return new(false, "Look at the eye-level target; keep your head upright");
        headForward = Vector3.Normalize(headForward);
        if (MathF.Abs(headForward.Y) > MathF.Sin(25f * MathF.PI / 180f))
            return new(false, "Look at the eye-level target; keep your head upright");
        if (Vector3.Dot(Horizontal(headForward), forward) < 0.5f)
            return new(false, "Face the FRONT arrow / mirror before checking feet");

        if (!TrackerAssignmentPolicy.IsBodyCandidate(head, waist))
            return new(false, "Check WAIST role / space alignment: tracker near or above head");
        var waistAboveFeet = waist.Y - (leftFoot.Y + rightFoot.Y) * 0.5f;
        if (waistAboveFeet < 0.40f)
            return new(false, "Waist too low vs feet; stand upright, check roles / space");
        var rightAxis = Vector3.Cross(Vector3.UnitY, forward);
        var footSeparation = Vector3.Dot(rightFoot - leftFoot, rightAxis);
        var reversed = footSeparation < -0.05f;
        var narrow = MathF.Abs(footSeparation) < 0.08f;
        var warning = reversed && narrow ? "Feet reversed / too close vs FRONT" :
            reversed ? "Feet reversed vs FRONT; check roles / space" :
            narrow ? "Feet too close; check stance after calibration" : null;
        if (!requireTPose) return new(true, "Ready: look forward and stand still", warning);
        if (!hasHands || !Finite(leftHand) || !Finite(rightHand))
            return new(false, "T-pose: controllers not tracked", warning);
        var right = rightHand - leftHand;
        right.Y = 0f;
        var spread = right.Length();
        if (spread < 0.65f)
            return new(false, $"T-pose: spread arms ({spread:0.00} m)", warning);
        if (Vector3.Dot(right / spread, rightAxis) < 0.5f)
            return new(false, "T-pose: face FRONT; arms sideways, hands uncrossed", warning);
        var heightDifference = MathF.Abs(leftHand.Y - rightHand.Y);
        if (heightDifference > 0.25f)
            return new(false, $"T-pose: keep hands level ({heightDifference:0.00} m)", warning);
        var handsBelowHead = head.Y - (leftHand.Y + rightHand.Y) * 0.5f;
        if (handsBelowHead > 0.70f)
            return new(false, "T-pose: raise hands to shoulder height", warning);
        if (handsBelowHead < -0.15f)
            return new(false, "T-pose: lower hands to shoulder height", warning);
        return new(true, "T-pose ready: hold arms out and stand still", warning);
    }

    /// <summary>
    /// 失敗時の左右・正面・高さ判定を再現できる数値を作成します。入力は全てワールド座標です。
    /// </summary>
    /// <param name="head">HMDの位置。</param>
    /// <param name="headForward">HMDの前方ベクトル。</param>
    /// <param name="front">校正の固定正面方向。</param>
    /// <param name="waist">腰の位置。</param>
    /// <param name="leftFoot">左足主入力の位置。</param>
    /// <param name="rightFoot">右足主入力の位置。</param>
    /// <returns>向きの内積、符号付き足幅、腰と足の高さの差、および入力座標。</returns>
    public static string Diagnostics(Vector3 head, Vector3 headForward, Vector3 front,
        Vector3 waist, Vector3 leftFoot, Vector3 rightFoot)
    {
        var forward = Horizontal(front);
        if (forward.LengthSquared() < 0.01f) forward = Horizontal(headForward);
        return FormattableString.Invariant($"facing dot={Vector3.Dot(Horizontal(headForward), forward):F3}; ") +
            FormattableString.Invariant($"foot signed separation={Vector3.Dot(rightFoot - leftFoot, Vector3.Cross(Vector3.UnitY, forward)):F3} m; ") +
            FormattableString.Invariant($"waist above feet={waist.Y - (leftFoot.Y + rightFoot.Y) * 0.5f:F3} m; ") +
            $"head={Format(head)}; head forward={Format(headForward)}; front={Format(front)}; " +
            $"waist={Format(waist)}; left={Format(leftFoot)}; right={Format(rightFoot)}";
    }

    /// <summary>
    /// ログの座標をロケールに依存しない小数点表記で整形します。
    /// </summary>
    /// <param name="value">整形するベクトル。</param>
    /// <returns>小数点以下3桁のXYZ文字列。</returns>
    internal static string Format(Vector3 value) => value.ToString("F3", CultureInfo.InvariantCulture);

    /// <summary>
    /// 水平方向の単位ベクトルを求めます。無効または長さがほぼゼロならゼロを返します。
    /// </summary>
    /// <param name="value">水平面へ射影する方向。</param>
    /// <returns>正規化したXZ方向、またはゼロ。</returns>
    private static Vector3 Horizontal(Vector3 value)
    {
        if (!Finite(value)) return Vector3.Zero;
        value.Y = 0f;
        return value.LengthSquared() > 0.0001f ? Vector3.Normalize(value) : Vector3.Zero;
    }

    /// <summary>
    /// 全成分が有限値かを確認します。
    /// </summary>
    /// <param name="value">検証するベクトル。</param>
    /// <returns>NaNと無限大を含まなければtrue。</returns>
    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
