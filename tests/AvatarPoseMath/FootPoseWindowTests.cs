using System.Globalization;
using SynthRidersFullBody;

/// <summary>ログを出した瞬間だけ正常でも、それ以前の足首のずれが記録されることを検証します。</summary>
internal static class FootPoseWindowTests
{
    /// <summary>正常／傾きの切り替え、欠測・復帰中の除外、集計の初期化を確認します。</summary>
    public static void Run(Action<bool, string> check)
    {
        var window = new FootPoseWindow();
        check(window.Summary == "samples=0", "An empty window looked like a measured flat foot.");
        var tilted = new FootPoseSample(.08f, 50f, 50f, 1.1f, -5f, -55f);
        var normal = new FootPoseSample(0f, 0f, 0f, .9f, -5f, -5f);
        window.Add(tilted, 1, true);
        window.Add(tilted, 2, true);
        window.Add(normal, 3, true); // The instant at which the log is printed is normal.
        check(window.Summary.Contains("samples=3; span=2.00s") && window.Summary.Contains("ankle angle range=0..50deg") &&
            window.Summary.Contains("max ankle error=0.080m"), "A normal last frame concealed the earlier ankle tilt.");
        check(window.Summary.Contains("reach range=0.9..1.1") &&
            window.Summary.Contains("target range=-5..-5deg; solved range=-55..-5deg"),
            "Target/solved slope or reach changes were lost.");
        var before = window.Summary;
        window.Add(new FootPoseSample(1, 180, 180, 2, 80, 80), 4, false);
        window.Add(default, 5, true);
        window.Add(tilted with { AnkleAngle = float.NaN }, 6, true);
        window.Add(tilted, float.PositiveInfinity, true);
        check(window.Summary == before, "Lost, blending or invalid tracking polluted the measured range.");
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            check(window.Summary == before, "Window log format depended on game language.");
        }
        finally { CultureInfo.CurrentCulture = culture; }
        window.Reset();
        check(window.Summary == "samples=0", "Reset retained the previous calibration's observations.");
        window.Add(normal with { TargetSlope = null, SolvedSlope = null }, 10, true);
        check(window.Summary.Contains("ankle angle range=0..0deg") && window.Summary.Contains("target range=n/adeg; solved range=n/adeg"),
            "An ankle-only endpoint invented a measured slope.");
        window.Add(tilted, 0, true); // A fresh time origin must not have a negative span.
        check(window.Summary.Contains("samples=1; span=0.00s") && window.Summary.Contains("ankle angle range=50..50deg"),
            "Resetting time mixed samples from separate runs.");
    }
}
