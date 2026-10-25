using System.Numerics;
using SynthRidersFullBody;

/// <summary>前へ伸ばした脚を人為的に曲げず、骨長と本来の屈曲を保つことを確認します。</summary>
internal static class StraightLegTests
{
    /// <summary>サイズ・大腿下腿の比率・旋回・足上げの角度を変えて、実行時の解法を検証します。</summary>
    public static void Run(Action<bool, string> check)
    {
        foreach (var size in new[] { .2f, 1f, 2.5f })
        foreach (var lengths in new[] { (.4f, .4f), (.6f, .35f) })
        foreach (var yaw in new[] { 0f, 90f, 180f })
        foreach (var lift in new[] { 0f, 30f, 60f, 90f, 120f })
        {
            var upper = lengths.Item1 * size;
            var lower = lengths.Item2 * size;
            var length = upper + lower;
            var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw * MathF.PI / 180);
            var swing = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -lift * MathF.PI / 180);
            var axis = Vector3.Transform(-Vector3.UnitY, turn * swing);
            var bend = Vector3.Transform(Vector3.UnitZ, turn * swing);
            var hip = new Vector3(2, 1, -3);
            // Test both an exactly reachable straight foot and a target beyond
            // the chain. The latter exercises the production ankle correction.
            foreach (var reach in new[] { 1f, 1.15f })
            {
                var target = hip + axis * (length * reach);
                check(AvatarPoseMath.SolveTwoBone(hip, target, axis, bend, upper, lower, 145,
                    out var knee, out var ankle), "Straight leg solve failed.");
                var measured = KneePoseSample.Capture(hip, knee, ankle, bend);
                check(measured.FlexDegrees < .2f, $"A straight forward leg retained {measured.FlexDegrees} degrees of flexion.");
                check(Math.Abs(Vector3.Distance(hip, knee) - upper) < .00001f &&
                    Math.Abs(Vector3.Distance(knee, ankle) - lower) < .00001f,
                    "Full extension stretched a bone.");
                check(Vector3.Distance(ankle, hip + axis * length) < .00001f,
                    "Full extension shortened the ankle reach.");
            }
            var relaxedTarget = hip + axis * (length * .98f);
            check(AvatarPoseMath.SolveTwoBone(hip, relaxedTarget, axis, bend, upper, lower, 145,
                out var bentKnee, out var bentAnkle), "Genuinely bent leg solve failed.");
            var bent = KneePoseSample.Capture(hip, bentKnee, bentAnkle, bend);
            check(bent.FlexDegrees > 20 && Vector3.Distance(bentAnkle, relaxedTarget) < .00001f,
                "Straight-leg correction forced a measured bent leg straight.");
            var sample = FootPoseSample.Capture(hip, bentKnee, bentAnkle, Quaternion.Identity,
                bentAnkle, Quaternion.Identity, new(relaxedTarget, Quaternion.Identity, relaxedTarget, Quaternion.Identity));
            check(Math.Abs(sample.TargetKneeFlex!.Value - bent.FlexDegrees!.Value) < .01f,
                "Target-distance diagnostic does not match geometric knee flexion.");

            var restKnee = hip - Vector3.UnitY * upper;
            var restAnkle = restKnee - Vector3.UnitY * lower;
            var farAnkle = hip + axis * (length * 1.15f);
            var desiredFootRotation = turn * swing;
            check(TrackedAnkleConstraint.TrySolve(hip, restKnee, restAnkle,
                Quaternion.Identity, Quaternion.Identity, Quaternion.Identity,
                new(farAnkle, desiredFootRotation, farAnkle, desiredFootRotation), bend, true, 1, 145, out var result),
                "Forward raised ankle correction was not applied.");
            var solvedKnee = hip + Vector3.Transform(restKnee - hip, result.Upper);
            var solvedAnkle = solvedKnee + Vector3.Transform(restAnkle - restKnee, result.Lower);
            check(KneePoseSample.Capture(hip, solvedKnee, solvedAnkle, bend).FlexDegrees < .2f,
                "Post-VRIK ankle correction reintroduced a bent knee.");
            check(Math.Abs(Quaternion.Dot(result.Ankle, desiredFootRotation)) > .99999f,
                "Straightening discarded the measured foot orientation.");
        }
        // Reproduce the log's former 0.999 reach clamp: it imposed ~5.1 degrees.
        check(AvatarPoseMath.SolveTwoBone(Vector3.Zero, Vector3.UnitZ * .8f,
            -Vector3.UnitY, Vector3.UnitY, .4f, .4f, 145, out var fullKnee, out var fullAnkle) &&
            KneePoseSample.Capture(Vector3.Zero, fullKnee, fullAnkle, Vector3.UnitY).FlexDegrees == 0,
            "The confirmed 5.1 degree minimum flexion remains.");
        // The two ends of this narrow interval must remain spatially continuous.
        AvatarPoseMath.SolveTwoBone(Vector3.Zero, Vector3.UnitZ * .799999f,
            -Vector3.UnitY, Vector3.UnitY, .4f, .4f, 145, out var beforeKnee, out var beforeAnkle);
        check(Vector3.Distance(fullKnee, beforeKnee) < .001f && Vector3.Distance(fullAnkle, beforeAnkle) < .00001f,
            "Reaching a straight knee introduced a positional jump.");
        var unbounded = FootPoseSample.Capture(Vector3.Zero, fullKnee, fullAnkle, Quaternion.Identity,
            fullAnkle, Quaternion.Identity, new(Vector3.UnitZ * 1.2f, Quaternion.Identity, Vector3.UnitZ * 1.2f, Quaternion.Identity));
        check(unbounded.TargetKneeFlex == 0 && Math.Abs(unbounded.TargetDistance!.Value - 1.2f) < .00001f &&
            Math.Abs(unbounded.LegLength!.Value - .8f) < .00001f,
            "An unreachable target lost its actual distance in diagnostics.");
    }
}
