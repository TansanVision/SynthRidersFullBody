using System.Numerics;
using SynthRidersFullBody;

internal static class PalmPoseTests
{
    /// <summary>モデルの軸・手の大きさ・姿勢・向きが変わっても、指と掌面が標準姿勢に合うことを確認します。</summary>
    public static void Run(Action<bool, string> check)
    {
        foreach (var scale in new[] { .1f, 1f, 3f })
        foreach (var left in new[] { true, false })
        foreach (var tPose in new[] { true, false })
        foreach (var yaw in new[] { 0f, 90f, 180f, 270f })
        foreach (var axes in new[] { Quaternion.Identity, Quaternion.CreateFromYawPitchRoll(.7f, -1.3f, 2.2f) })
        {
            var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw * MathF.PI / 180);
            var side = left ? -Vector3.UnitX : Vector3.UnitX;
            var fingers = Vector3.Normalize(tPose ? side : -Vector3.UnitY + side * .12f);
            var palm = tPose ? -Vector3.UnitY : -side;
            var expectedPalm = Vector3.Normalize(palm - fingers * Vector3.Dot(palm, fingers));
            var localFingers = Vector3.Transform(Vector3.UnitZ, axes) * .08f * scale;
            var localPalm = Vector3.Transform(Vector3.UnitY, axes) * .001f * scale * scale;
            check(PalmPoseMath.TryRotation(localFingers, localPalm, Vector3.Transform(fingers, turn),
                Vector3.Transform(palm, turn), out var rotation), "Valid scaled hand rejected.");
            check(Vector3.Distance(Vector3.Transform(Vector3.Normalize(localFingers), rotation),
                Vector3.Transform(fingers, turn)) < .00001f, "Finger direction changed with model axes.");
            check(Vector3.Distance(Vector3.Transform(Vector3.Normalize(localPalm), rotation),
                Vector3.Transform(expectedPalm, turn)) < .00001f, "Palm is not down/inward in the chosen calibration pose.");
        }
        check(!PalmPoseMath.TryRotation(Vector3.Zero, Vector3.UnitY, Vector3.UnitZ, Vector3.UnitY, out _), "Missing fingers silently produced a reference.");
        foreach (var left in new[] { true, false })
        foreach (var scale in new[] { .1f, 1f, 3f })
        foreach (var standing in new[] { false, true })
        {
            var axes = Quaternion.CreateFromYawPitchRoll(.7f, -.4f, 1.2f);
            var side = left ? -1f : 1f;
            var index = Vector3.Transform(new Vector3(side * .07f, 0, .02f) * scale, Quaternion.Inverse(axes));
            var little = Vector3.Transform(new Vector3(side * .07f, 0, -.02f) * scale, Quaternion.Inverse(axes));
            var fingers = standing ? -Vector3.UnitY : Vector3.UnitX * side;
            var palm = standing ? -Vector3.UnitX * side : -Vector3.UnitY;
            check(PalmPoseMath.TryFingerReference(left, index, little, null, fingers, palm, out var rotation),
                "Finger geometry could not produce a palm reference.");
            var normal = Vector3.Normalize(Vector3.Cross(index, little) * (left ? 1 : -1));
            check(Vector3.Distance(Vector3.Transform(normal, rotation), palm) < .00001f,
                "Left/right finger geometry reversed the palm side.");
        }
        check(!PalmPoseMath.TryRotation(Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitY, out _), "Degenerate palm plane accepted.");
        check(!PalmPoseMath.TryRotation(Vector3.UnitZ, Vector3.UnitY, new(float.NaN, 0, 0), Vector3.UnitY, out _), "Invalid hand input accepted.");

        var torso = TorsoPoseSample.Capture(new(0, 1, 0), new(0, 1.3f, .05f), new(0, 1.48f, .06f),
            new(0, 1.55f, .09f), new(0, 1.65f, .17f), new(0, 1.65f, .17f), Vector3.UnitZ);
        check(torso.HeadFromChest.HasValue && Math.Abs(torso.HeadFromChest.Value.Y - .25f) < .00001f &&
            Math.Abs(torso.HeadFromChest.Value.Z - .04f) < .00001f, "Torso sample mixes height and forward offset.");
        var q = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1.2f);
        Vector3 Move(Vector3 p) => Vector3.Transform(p, q) + new Vector3(10, 2, -5);
        var moved = TorsoPoseSample.Capture(Move(new(0, 1, 0)), Move(new(0, 1.3f, .05f)), Move(new(0, 1.48f, .06f)),
            Move(new(0, 1.55f, .09f)), null, null, Vector3.Transform(Vector3.UnitZ, q));
        check(Vector3.Distance(moved.HeadFromChest!.Value, torso.HeadFromChest!.Value) < .00001f,
            "World translation/yaw changes measured torso posture.");
        check(moved.HmdFromEyes == null && moved.Summary.Contains("HMD-eyes=n/a"), "Missing eye bones are logged as a zero error.");
    }
}
