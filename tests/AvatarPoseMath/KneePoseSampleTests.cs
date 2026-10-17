using System.Globalization;
using System.Numerics;
using SynthRidersFullBody;

/// <summary>膝の実測角・ガイド一致度と、保存した数値が後続の姿勢から独立することを確認します。</summary>
internal static class KneePoseSampleTests
{
    /// <summary>直立・屈曲・反転を、サイズと向きを変えた骨格で検証します。</summary>
    /// <param name="check">検証結果と失敗理由を受け取る関数。</param>
    public static void Run(Action<bool, string> check)
    {
        foreach (var scale in new[] { .02f, .3f, 1f, 2.376f, 100f })
        foreach (var yaw in new[] { 0f, 90f, 180f, 270f })
        foreach (var angle in new[] { 0f, 5.1f, 5.3f, 45f, 90f, 150f })
        {
            var halfAngle = angle * MathF.PI / 360f;
            var hip = new Vector3(0, MathF.Cos(halfAngle), 0);
            var knee = new Vector3(0, MathF.Cos(halfAngle) * .5f, MathF.Sin(halfAngle) * .5f);
            var ankle = Vector3.Zero;
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw * MathF.PI / 180f);
            var origin = new Vector3(3, -1, 2);
            Vector3 Map(Vector3 p) => origin + Vector3.Transform(p * scale, rotation);
            var guide = Vector3.Transform(Vector3.UnitZ, rotation);
            var sample = KneePoseSample.Capture(Map(hip), Map(knee), Map(ankle), guide);
            check(sample.FlexDegrees.HasValue && Math.Abs(sample.FlexDegrees.Value - angle) < .005f,
                "Measured knee flexion changed with avatar scale or yaw.");
            check(angle == 0f ? sample.GuideDot == null : sample.GuideDot > .999f,
                "Forward knee bend did not match its guide, or straight knee reported a direction.");
            var reversed = KneePoseSample.Capture(Map(hip), Map(knee), Map(ankle), -guide);
            check(angle == 0f ? reversed.GuideDot == null : reversed.GuideDot < -.999f,
                "Reverse guide was not distinguished from forward guide.");
        }

        var solvedKnee = new Vector3(0, .5f, .5f);
        var saved = KneePoseSample.Capture(Vector3.UnitY, solvedKnee, Vector3.Zero, Vector3.UnitZ);
        solvedKnee = Vector3.UnitY * .5f; // A later animation update restores the straight pose.
        var later = KneePoseSample.Capture(Vector3.UnitY, solvedKnee, Vector3.Zero, Vector3.UnitZ);
        check(saved.FlexDegrees == 90f && saved.GuideDot == 1f && later.FlexDegrees == 0f,
            "Post-solve knee sample was lost when the next animated pose was read.");
        var noGuide = KneePoseSample.Capture(Vector3.UnitY, new(0, .5f, .5f), Vector3.Zero, null);
        check(noGuide.FlexDegrees == 90f && noGuide.GuideDot == null, "Missing guide hid a valid knee angle.");
        var axialGuide = KneePoseSample.Capture(Vector3.UnitY, new(0, .5f, .5f), Vector3.Zero, Vector3.UnitY);
        check(axialGuide.FlexDegrees == 90f && axialGuide.GuideDot == null, "Axial guide reported a bend direction.");
        foreach (var bad in new[] { Vector3.UnitY, new Vector3(float.NaN, 0, 0), new Vector3(float.PositiveInfinity, 0, 0) })
        {
            var invalid = KneePoseSample.Capture(Vector3.UnitY, bad, Vector3.Zero, Vector3.UnitZ);
            check(invalid.FlexDegrees == null && invalid.GuideDot == null, "Invalid bone geometry looked like a straight leg.");
        }
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            check(new KneePoseSample(5.3f, -.5f).Summary == "flex=5.3deg guideDot=-0.50",
                "Knee log decimal separators depended on game language.");
            check(default(KneePoseSample).Summary == "flex=n/a guideDot=n/a", "Missing sample looked like measured zero.");
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }
}
