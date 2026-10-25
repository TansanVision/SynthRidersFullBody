using SynthRidersFullBody;
using UnityEngine;
using Il2CppRootMotion.FinalIK;

internal static class HandAdjustmentTests
{
    /// <summary>補正中の位置維持、ボーン軸の違い、計算中断と再接続を確認します。</summary>
    public static void Run(Action<bool, string> check)
    {
        using var correction = new HandTargetAdjustment();
        var solver = new IKSolverVR();
        var left = new GameObject("left native").transform;
        var right = new GameObject("right native").transform;
        left.position = new(-.1f, 1.2f, .5f); right.position = new(.1f, 1.2f, .5f);
        solver.leftArm.target = left; solver.rightArm.target = right;
        correction.Apply(solver, 0, 0);
        check(solver.leftArm.target == left && solver.rightArm.target == right, "Zero hand settings changed native bindings.");
        foreach (var axis in new[] { new Vector3(0, 0, 1), new Vector3(0, 1, 0), new Vector3(1, 0, 0) })
        {
            solver.leftArm.wristToPalmAxis = solver.rightArm.wristToPalmAxis = axis;
            for (var frame = 0; frame < 20; frame++)
            {
                correction.Apply(solver, 90, -90);
                check(solver.leftArm.target!.position == left.position && solver.rightArm.target!.position == right.position,
                    "Palm adjustment moved the tracked hands.");
                check(solver.leftArm.target!.rotation == Quaternion.AngleAxis(90, axis) &&
                    solver.rightArm.target!.rotation == Quaternion.AngleAxis(-90, axis), "Hand adjustment accumulated or used fixed bone axes.");
                check(left.rotation == Quaternion.identity && right.rotation == Quaternion.identity, "Native game target was modified.");
                correction.Restore();
                check(solver.leftArm.target == left && solver.rightArm.target == right, "Hand targets were not restored after solving.");
            }
        }
        correction.Apply(solver, 90, -90);
        var replacement = new GameObject("new scene target").transform;
        solver.rightArm.target = replacement;
        correction.Restore();
        check(solver.rightArm.target == replacement, "Cleanup overwrote a replacement game binding.");
        correction.Apply(solver, 90, 90);
        correction.Apply(solver, 90, 90); // aborted solve / missing post callback
        check(solver.leftArm.target!.rotation == Quaternion.AngleAxis(90, new Vector3(1, 0, 0)), "Aborted solve accumulated palm roll.");
        correction.Dispose();
        check(solver.leftArm.target == left && solver.rightArm.target == replacement, "Disable did not release hand targets.");
    }
}
