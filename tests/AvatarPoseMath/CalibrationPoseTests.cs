using System.Numerics;
using SynthRidersFullBody;

internal static class CalibrationPoseTests
{
    public static void Run(Action<bool, string> check)
    {
        var head = new Vector3(0, 1.7f, 0);
        var waist = new Vector3(0, 1f, 0);
        var left = new Vector3(-0.2f, 0.1f, 0);
        var right = new Vector3(0.2f, 0.1f, 0);
        var leftHand = new Vector3(-0.7f, 1.4f, 0);
        var rightHand = new Vector3(0.7f, 1.4f, 0);
        // Rotate the entire aligned play space, not just the avatar. Body checks
        // must be invariant to origin, heading and the tracker's local mounting axes.
        foreach (var degrees in new[] { 0f, 45f, 90f, 180f, 270f })
        foreach (var offset in new[] { Vector3.Zero, new Vector3(10, -2, 4) })
        foreach (var tPose in new[] { false, true })
        {
            var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, degrees * MathF.PI / 180f);
            Vector3 Point(Vector3 v) => Vector3.Transform(v, turn) + offset;
            var forward = Vector3.Transform(Vector3.UnitZ, turn);
            var correct = CalibrationPoseCheck.Check(Point(head), forward, forward, true,
                Point(leftHand), Point(rightHand), true, Point(waist), Point(left), Point(right), tPose);
            check(correct.Ready && correct.Warning == null,
                $"Aligned calibration rejected or warned at {degrees} deg, mode={tPose}: {correct.Status}");
            var swapped = CalibrationPoseCheck.Check(Point(head), forward, forward, true,
                Point(leftHand), Point(rightHand), true, Point(waist), Point(right), Point(left), tPose);
            check(swapped.Ready && swapped.Warning?.Contains("Feet reversed") == true &&
                !swapped.Warning.Contains("too close"), "Reversed feet must warn without blocking or reporting a narrow stance.");
            // A user facing away has correct body-relative assignments but appears
            // reversed against the fixed front. Tell them to face front FIRST.
            var away = CalibrationPoseCheck.Check(Point(head), -forward, forward, true,
                Point(rightHand), Point(leftHand), true, Point(waist), Point(right), Point(left), tPose);
            check(!away.Ready && away.Status.StartsWith("Face the FRONT"),
                "Facing backwards was incorrectly diagnosed as swapped trackers.");
        }
        TPoseAssessment Assess(bool hands = true, bool trackers = true, bool tPose = false,
            Vector3? look = null, Vector3? pelvis = null, Vector3? leftInput = null) =>
            CalibrationPoseCheck.Check(head, look ?? Vector3.UnitZ, Vector3.UnitZ, hands,
                leftHand, rightHand, trackers, pelvis ?? waist, leftInput ?? left, right, tPose);
        check(Assess(hands: false).Ready, "Standing incorrectly required both controllers.");
        check(!Assess(hands: false, tPose: true).Ready, "T-pose ignored missing controllers.");
        check(!Assess(trackers: false).Ready, "Missing body inputs accepted.");
        var low = Assess(pelvis: new Vector3(0, 0.3f, 0));
        check(!low.Ready && low.Status.StartsWith("Waist too low") && !low.Status.Contains("T-pose"),
            "Standing failure incorrectly referred to T-pose or accepted a low waist.");
        var down = Assess(look: Vector3.Normalize(new Vector3(0, -1, 1)));
        check(!down.Ready && down.Status.Contains("eye-level"), "Downward gaze accepted.");
        check(!Assess(leftInput: new Vector3(float.NaN, 0, 0)).Ready, "NaN foot position accepted.");
        check(!Assess(look: new Vector3(float.PositiveInfinity, 0, 0)).Ready, "Infinite HMD direction accepted.");
        check(!Assess(look: Vector3.Zero).Ready, "Zero HMD direction accepted.");
        check(!Assess(pelvis: head).Ready, "Head-mounted tracker accepted as waist.");
        foreach (var tPose in new[] { false, true })
        foreach (var separation in new[] { 0f, 0.04f, -0.04f, -0.06f })
        {
            var closeLeft = new Vector3(-separation * 0.5f, 0.1f, 0);
            var closeRight = new Vector3(separation * 0.5f, 0.1f, 0);
            var close = CalibrationPoseCheck.Check(head, Vector3.UnitZ, Vector3.UnitZ, true,
                leftHand, rightHand, true, waist, closeLeft, closeRight, tPose);
            check(close.Ready && close.Warning?.Contains("too close") == true,
                $"Narrow stance must warn without blocking: separation={separation}, mode={tPose}.");
            check((close.Warning?.Contains("reversed") == true) == (separation < -0.05f),
                "Combined warning lost the signed left/right check.");
        }
        foreach (var footPair in new[] { (right, left), (Vector3.UnitY * 0.1f, Vector3.UnitY * 0.1f) })
        {
            var noHands = CalibrationPoseCheck.Check(head, Vector3.UnitZ, Vector3.UnitZ, false,
                leftHand, rightHand, true, waist, footPair.Item1, footPair.Item2, true);
            check(!noHands.Ready && noHands.Status.Contains("controllers not tracked") && noHands.Warning != null,
                "Foot warning must not bypass missing T-pose controllers.");
            var armsDown = CalibrationPoseCheck.Check(head, Vector3.UnitZ, Vector3.UnitZ, true,
                leftHand - Vector3.UnitY, rightHand - Vector3.UnitY, true, waist, footPair.Item1, footPair.Item2, true);
            check(!armsDown.Ready && armsDown.Status.Contains("raise hands") && armsDown.Warning != null,
                "Foot warning must not bypass T-pose arm height.");
            check(!CalibrationPoseCheck.Check(head, Vector3.UnitZ, Vector3.UnitZ, true,
                leftHand, rightHand, false, waist, footPair.Item1, footPair.Item2, false).Ready,
                "Warning-only foot checks bypassed missing body tracking.");
            check(!CalibrationPoseCheck.Check(head, -Vector3.UnitZ, Vector3.UnitZ, true,
                leftHand, rightHand, true, waist, footPair.Item1, footPair.Item2, false).Ready,
                "Warning-only foot checks bypassed front direction.");
        }
        // Body geometry from the v0.46 Ultimate Tracker failure log; hand poses
        // are valid test inputs because the log does not include controller poses.
        foreach (var tPose in new[] { false, true })
        {
            var reported = CalibrationPoseCheck.Check(new(-0.039f, 1.618f, -0.015f), new(0.008f, 0.018f, 1f),
                new(0.011f, 0, 1f), true, leftHand, rightHand, true, new(0.561f, 0.921f, -0.117f),
                new(0.626f, 0.059f, -0.023f), new(0.426f, 0.047f, -0.006f), tPose);
            check(reported.Ready && reported.Warning?.Contains("Feet reversed") == true,
                $"Reported Ultimate Tracker body geometry still blocked calibration, mode={tPose}.");
        }
        var diagnostics = CalibrationPoseCheck.Diagnostics(head, Vector3.UnitZ, Vector3.UnitZ, waist, right, left);
        check(diagnostics.Contains("foot signed separation=-0.400 m") &&
            diagnostics.Contains("waist above feet=0.900 m") && diagnostics.Contains("facing dot=1.000"),
            "Calibration failure log lost signed geometry needed to diagnose the Ultimate Tracker report.");
    }
}
