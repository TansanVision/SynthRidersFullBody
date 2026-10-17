using System.Globalization;
using System.Numerics;
using SynthRidersFullBody;

/// <summary>実行時と同じ足目標計算で、持ち上げ・屈曲・ターンの入力を拡大しないことを検証します。</summary>
internal static class TrackedFootPoseTests
{
    /// <summary>校正から目標・到達率の計測までを、骨軸・体格・足先の有無を変えて確認します。</summary>
    public static void Run(Action<bool, string> check)
    {
        foreach (var scale in new[] { .2f, 1f, 2.5f })
        foreach (var yaw in new[] { 0f, 90f, 180f, 270f })
        foreach (var hasToes in new[] { false, true })
        {
            var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw * MathF.PI / 180f);
            var boneAxis = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, .7f);
            var origin = new Vector3(2, -.1f, 3);
            Vector3 World(Vector3 p) => origin + Vector3.Transform(p * scale, turn);
            var tracker = World(new(0, .15f, -.06f));
            var ankle = World(new(0, .1f, 0));
            var toe = hasToes ? World(new(0, .05f, .15f)) : ankle;
            var footRotation = turn * boneAxis;
            var toeRotation = hasToes ? footRotation * Quaternion.CreateFromAxisAngle(Vector3.UnitX, .5f) : footRotation;
            var mount = RigidPoseOffset.Between(tracker, turn, ankle, footRotation);
            var endpoint = RigidPoseOffset.Between(ankle, footRotation, toe, toeRotation);
            foreach (var movement in new[] { Vector3.Zero, new Vector3(0, .2f, .2f),
                new Vector3(0, .65f, .1f), new Vector3(.55f, .6f, 0), new Vector3(0, .1f, -.35f) })
            {
                var delta = Vector3.Transform(movement * scale, turn);
                var result = TrackedFootPose.FromTracker(tracker + delta, turn, mount, endpoint);
                check(Vector3.Distance(result.AnklePosition, ankle + delta) < .00001f,
                    "Lifting the foot added an unmeasured reach or changed its movement scale.");
                check(Vector3.Distance(result.EndPosition, toe + delta) < .00001f,
                    "The endpoint lost its calibrated translation when the foot was lifted.");
                check(MathF.Abs(Quaternion.Dot(result.EndRotation, toeRotation)) > .99999f,
                    "A turn changed the endpoint's bone axes.");
            }
            // Separate toe input controls direction, never the ankle location or foot length.
            var dual = TrackedFootPose.FromTracker(tracker, turn, mount, endpoint, World(new(.3f, .8f, .6f)));
            check(Vector3.Distance(dual.AnklePosition, ankle) < .00001f &&
                MathF.Abs(Vector3.Distance(dual.EndPosition, dual.AnklePosition) - Vector3.Distance(toe, ankle)) < .00001f,
                "Independent toe tracking pulled the ankle or stretched the foot.");
        }

        // The old 0.6 extension floor pushed deeply folded feet away from the hip.
        var identity = new RigidPoseOffset(Vector3.Zero, Quaternion.Identity);
        var tucked = TrackedFootPose.FromTracker(new(0, .7f, .1f), Quaternion.Identity, identity, identity);
        check(AvatarPoseMath.SolveTwoBone(Vector3.UnitY, tucked.AnklePosition, -Vector3.UnitY,
            Vector3.UnitZ, .5f, .5f, 170, out var knee, out var foot), "Folded leg solve failed.");
        var foldedKnee = KneePoseSample.Capture(Vector3.UnitY, knee, foot, Vector3.UnitZ);
        check(foldedKnee.FlexDegrees > 140 && Vector3.Distance(foot, tucked.AnklePosition) < .00001f,
            "Measured knee lift was replaced with a straighter inferred kick.");

        // A matching toe position alone must not conceal a rotated/displaced ankle.
        var desired = new TrackedFootPose(Vector3.Zero, Quaternion.Identity, new(0, 0, .2f), Quaternion.Identity);
        var actualRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 3);
        var actualAnkle = desired.EndPosition - Vector3.Transform(new Vector3(0, 0, .2f), actualRotation);
        var hip = Vector3.UnitY;
        var calf = new Vector3(0, .5f, 0);
        var sample = FootPoseSample.Capture(hip, calf, actualAnkle, actualRotation,
            desired.EndPosition, actualRotation, desired);
        check(MathF.Abs(sample.AnkleError!.Value - .2f) < .00001f && MathF.Abs(sample.AnkleAngle!.Value - 60) < .001f,
            "Matching endpoint hid the ankle error.");
        check(MathF.Abs(sample.EndAngle!.Value - 60) < .001f && sample.TargetSlope == 0 &&
            MathF.Abs(sample.SolvedSlope!.Value + 60) < .001f, "Tilted foot was indistinguishable from its target.");
        var length = Vector3.Distance(hip, calf) + Vector3.Distance(calf, actualAnkle);
        check(MathF.Abs(sample.Reach!.Value - 1 / length) < .00001f, "Reach used the toe chain instead of ankle leg length.");
        var reached = FootPoseSample.Capture(hip, calf, Vector3.Zero, Quaternion.Identity, desired.EndPosition, Quaternion.Identity, desired);
        check(reached.AnkleError == 0 && reached.AnkleAngle == 0 && reached.EndAngle == 0 && reached.Reach == 1,
            "Correct foot pose did not report zero errors.");
        check(sample.AnkleError > .19f, "Later pose changed the earlier sample.");
        var noToe = FootPoseSample.Capture(hip, calf, Vector3.Zero, Quaternion.Identity, Vector3.Zero, Quaternion.Identity,
            new(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Quaternion.Identity));
        check(noToe.TargetSlope == null && noToe.SolvedSlope == null, "Ankle-only endpoint reported a foot slope.");
        var invalid = FootPoseSample.Capture(hip, calf, new(float.NaN, 0, 0), Quaternion.Identity,
            desired.EndPosition, Quaternion.Identity, desired);
        check(invalid.AnkleError == null && invalid.Reach == null, "Invalid skeleton looked like zero error.");
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            check(sample.Summary.Contains("ankle error=0.200m/60deg") &&
                default(FootPoseSample).Summary.Contains("reach=n/a"), "Diagnostic formatting lost missing data or decimal separator.");
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }
}
