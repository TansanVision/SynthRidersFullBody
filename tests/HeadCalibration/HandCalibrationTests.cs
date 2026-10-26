using SynthRidersFullBody;
using UnityEngine;
using Il2CppRootMotion.FinalIK;
using N = System.Numerics.Quaternion;

internal static class HandCalibrationTests
{
    /// <summary>入力と手の回転登録、取得元喪失、手動補正との併用、シーン復帰を検証します。</summary>
    public static void Run(Action<bool, string> check)
    {
        using var targets = new HandTargetAdjustment();
        var solver = new IKSolverVR();
        var nativeLeft = new GameObject("native left").transform;
        var nativeRight = new GameObject("native right").transform;
        nativeLeft.position = new(-.1f, 1.2f, .5f); nativeRight.position = new(.1f, 1.2f, .5f);
        solver.leftArm.target = nativeLeft; solver.rightArm.target = nativeRight;
        foreach (var standing in new[] { false, true })
        {
            var leftReference = new Pose(new(-.5f, 1.4f, 0), Quaternion.AngleAxis(standing ? 90 : 0, new(0, 0, 1)));
            var rightReference = new Pose(new(.5f, 1.4f, 0), Quaternion.AngleAxis(standing ? -90 : 0, new(0, 0, 1)));
            var source = new HandCalibrationInput(new("XR:left", new(default, Quaternion.AngleAxis(-80, new(1, 0, 0)))),
                new("XR:right", new(default, Quaternion.AngleAxis(110, new(1, 0, 0)))));
            targets.Capture(source, leftReference, rightReference);
            targets.UpdateInput(source);
            targets.Apply(solver, 0, 0);
            check(Near(solver.leftArm.target!.rotation, leftReference.Rotation) && Near(solver.rightArm.target!.rotation, rightReference.Rotation),
                "Controller orientation was not registered to standard palms.");
            check(solver.leftArm.target.position == nativeLeft.position && solver.rightArm.target.position == nativeRight.position,
                "Hand rotation calibration changed native hand positions.");
            targets.Restore();
            var motion = Quaternion.AngleAxis(45, new(0, 1, 0));
            var moved = source.Remap(p => p with { Rotation = motion * p.Rotation });
            nativeLeft.rotation = Quaternion.AngleAxis(-140, new(0, 0, 1)); // native rotation is not the input
            targets.UpdateInput(moved);
            targets.Apply(solver, 0, 0);
            check(Near(solver.leftArm.target!.rotation, motion * leftReference.Rotation), "Native hand rotation contaminated direct controller motion.");
            targets.Restore();
            targets.UpdateInput(moved with { Left = null });
            targets.Apply(solver, 0, 0);
            check(solver.leftArm.target == nativeLeft && solver.rightArm.target != nativeRight, "One missing hand disabled both hands.");
            targets.Restore();
            targets.UpdateInput(moved with { Left = moved.Left!.Value with { Source = "SteamVR:left" } });
            targets.Apply(solver, 0, 0);
            check(solver.leftArm.target == nativeLeft, "Applied XR rotation calibration to a different API.");
            targets.Restore();
            targets.UpdateInput(source);
            targets.Apply(solver, 15, 0);
            check(Near(solver.leftArm.target!.rotation, leftReference.Rotation * Quaternion.AngleAxis(15, new(0, 0, 1))),
                "Fine adjustment is not relative to the calibrated hand.");
            targets.Restore();
            // Recreate the reference from original saved inputs after moving the world.
            var world = Quaternion.AngleAxis(170, new(0, 1, 0));
            Pose Remap(Pose p) => new(new Vector3(3, 0, 2) + world * p.Position, world * p.Rotation);
            var restored = source.Remap(Remap);
            targets.Capture(restored, Remap(leftReference), Remap(rightReference));
            targets.UpdateInput(restored);
            targets.Apply(solver, 0, 0);
            check(Near(solver.leftArm.target!.rotation, world * leftReference.Rotation), "Recovery lost standard hand alignment.");
            targets.Restore();
            targets.Capture(source, null, rightReference); // left has an explicitly assigned wrist tracker
            targets.UpdateInput(source);
            targets.Apply(solver, 0, 0);
            check(solver.leftArm.target == nativeLeft, "An explicit wrist target was calibrated twice.");
            targets.Restore();
        }
    }

    /// <summary>同じ回転の正負Quaternionを同一とみなします。</summary>
    private static bool Near(Quaternion a, Quaternion b) => Math.Abs(N.Dot(a.N, b.N)) > .99999f;
}
