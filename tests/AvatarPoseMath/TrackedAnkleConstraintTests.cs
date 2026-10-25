using System.Numerics;
using SynthRidersFullBody;

/// <summary>到達不足で足首が約50度傾いた例を再現し、骨長・入力方向と境界の連続性を確認します。</summary>
internal static class TrackedAnkleConstraintTests
{
    /// <summary>体格・旋回・装着軸・足首の向きと追跡重みを変えて実行時の補正を検証します。</summary>
    public static void Run(Action<bool, string> check)
    {
        foreach (var size in new[] { .2f, 1f, 2.5f })
        foreach (var heading in new[] { 0f, 90f, 180f, 270f })
        foreach (var pitch in new[] { -40f, 0f, 35f }) // Real toe raises/pointe must remain possible.
        {
            var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, heading * MathF.PI / 180);
            var origin = new Vector3(2, .1f, -3);
            Vector3 W(Vector3 p) => origin + Vector3.Transform(p * size, yaw);
            var hip = W(new(0, 1, 0));
            var knee = W(new(0, .6f, 0));
            var ankle = W(new(0, .2f, 0));
            var upperRotation = yaw * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, .8f);
            var lowerRotation = yaw * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -.5f);
            var desiredRotation = yaw * Quaternion.CreateFromYawPitchRoll(.2f, pitch * MathF.PI / 180, .3f);
            var nativeRotation = desiredRotation * Quaternion.CreateFromAxisAngle(Vector3.UnitX, 50f * MathF.PI / 180);
            var length = Vector3.Distance(hip, knee) + Vector3.Distance(knee, ankle);
            var bend = Vector3.Transform(Vector3.UnitZ, yaw);
            var oldBlend = -1f;
            foreach (var reach in new[] { 1.00001f, 1.001f, 1.005f, 1.01f, 1.03f, 1.15f, 2.452f, 8.65f })
            {
                var requested = hip + Vector3.Transform(-Vector3.UnitY * (length * reach), yaw);
                var target = new TrackedFootPose(requested, desiredRotation, requested, desiredRotation);
                check(TrackedAnkleConstraint.TrySolve(hip, knee, ankle, upperRotation, lowerRotation, nativeRotation,
                    target, bend, true, 1, 145, out var result), "Unreachable tracked ankle was not constrained.");
                // Apply WORLD rotations through a bone hierarchy, without translating joints.
                var solvedKnee = hip + Vector3.Transform(Vector3.Transform(knee - hip, Quaternion.Inverse(upperRotation)), result.Upper);
                var solvedAnkle = solvedKnee + Vector3.Transform(Vector3.Transform(ankle - knee, Quaternion.Inverse(lowerRotation)), result.Lower);
                check(Math.Abs(Vector3.Distance(hip, solvedKnee) - length / 2) < .00001f &&
                    Math.Abs(Vector3.Distance(solvedKnee, solvedAnkle) - length / 2) < .00001f,
                    "Ankle correction stretched bones.");
                check(result.Weight >= oldBlend && result.Weight is >= 0 and <= 1, "Reach correction was not continuous/monotonic.");
                oldBlend = result.Weight;
                if (reach >= 1.03f)
                {
                    check(Math.Abs(Quaternion.Dot(result.Ankle, desiredRotation)) > .99999f,
                        "Correction flattened a measured raised foot or retained the unintended 50 degree tilt.");
                    check(Vector3.Distance(solvedAnkle, hip + Vector3.Normalize(requested - hip) * length) < .00002f,
                        "The ankle did not reach the bone-length-limited goal.");
                    var measured = FootPoseSample.Capture(hip, solvedKnee, solvedAnkle, result.Ankle,
                        solvedAnkle, result.Ankle, target);
                    check(measured.AnkleError > 0 && measured.Reach > 1,
                        "Unreachable distance was concealed by changing the measured target.");
                }
                if (reach < 1.001f)
                    check(Math.Abs(Quaternion.Dot(result.Ankle, nativeRotation)) > .99999f,
                        "Crossing reach=1 caused an ankle pop.");
                check(TrackedAnkleConstraint.TrySolve(hip, knee, ankle, upperRotation, lowerRotation, nativeRotation,
                    target, bend, true, .25f, 145, out var partial) && Math.Abs(partial.Weight - result.Weight * .25f) < .00001f,
                    "The correction ignored recovery/IK weights.");
            }
            var reachable = new TrackedFootPose(W(new(0, .5f, .1f)), desiredRotation, ankle, desiredRotation);
            check(!TrackedAnkleConstraint.TrySolve(hip, knee, ankle, upperRotation, lowerRotation, nativeRotation,
                reachable, bend, true, 1, 145, out _), "Normal knee flexion was unnecessarily re-solved.");
            var far = reachable with { AnklePosition = W(new(0, -.3f, 0)) };
            check(!TrackedAnkleConstraint.TrySolve(hip, knee, ankle, upperRotation, lowerRotation, nativeRotation,
                far, bend, false, 1, 145, out _), "An ankle-only solver endpoint was altered.");
            foreach (var weight in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
                check(!TrackedAnkleConstraint.TrySolve(hip, knee, ankle, upperRotation, lowerRotation, nativeRotation,
                    far, bend, true, weight, 145, out _), "Lost/inactive tracking still corrected the ankle.");
            check(!TrackedAnkleConstraint.TrySolve(hip, knee, ankle, upperRotation, lowerRotation, nativeRotation,
                far with { AnkleRotation = default }, bend, true, 1, 145, out _), "Invalid target rotation was accepted.");
            check(!TrackedAnkleConstraint.TrySolve(hip, hip, ankle, upperRotation, lowerRotation, nativeRotation,
                far, bend, true, 1, 145, out _), "Zero-length thigh was accepted.");
            // Independent toe input feeds its computed ankle rotation into the same constraint.
            var mount = new RigidPoseOffset(Vector3.Zero, desiredRotation);
            var end = new RigidPoseOffset(new(0, -.04f * size, .18f * size), Quaternion.Identity);
            var dual = TrackedFootPose.FromTracker(far.AnklePosition, Quaternion.Identity, mount, end,
                far.AnklePosition + Vector3.Transform(new Vector3(.05f, .2f, .2f) * size, yaw));
            check(TrackedAnkleConstraint.TrySolve(hip, knee, ankle, upperRotation, lowerRotation, nativeRotation,
                dual, bend, true, 1, 145, out var dualResult) &&
                Math.Abs(Quaternion.Dot(dualResult.Ankle, dual.AnkleRotation)) > .99999f,
                "Independent ankle/toe direction was discarded.");
        }
        // Backward/upward targets and unequal segments exercise the two joint
        // rotation order, including an exact 180 degree upper-bone rotation.
        foreach (var axis in new[] { -Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ, Vector3.UnitX })
        foreach (var lengths in new[] { (.6f, .35f), (.35f, .6f) })
        {
            var hip = Vector3.Zero;
            var knee = -Vector3.UnitY * lengths.Item1;
            var ankle = knee - Vector3.UnitY * lengths.Item2;
            var goal = axis * (lengths.Item1 + lengths.Item2) * 1.15f;
            var target = new TrackedFootPose(goal, Quaternion.Identity, goal, Quaternion.Identity);
            check(TrackedAnkleConstraint.TrySolve(hip, knee, ankle, Quaternion.Identity, Quaternion.Identity,
                Quaternion.Identity, target, -Vector3.UnitZ, true, 1, 145, out var result),
                "A backward/upward ankle goal could not be constrained.");
            var solvedKnee = Vector3.Transform(knee, result.Upper);
            var solvedAnkle = solvedKnee + Vector3.Transform(ankle - knee, result.Lower);
            var expected = axis * (lengths.Item1 + lengths.Item2);
            check(Vector3.Distance(solvedAnkle, expected) < .00001f,
                "Joint rotation order failed for unequal lengths or an opposite goal.");
            check(Math.Abs(Vector3.Distance(solvedKnee, solvedAnkle) - lengths.Item2) < .00001f,
                "A turn changed the calf length.");
        }
    }
}
