using System.Numerics;
using SynthRidersFullBody;

/// <summary>
/// Ultimate Tracker補正の機種選別、移動方向、回転と校正オフセット、シーン復帰を検証します。
/// </summary>
internal static class UltimateTrackerCorrectionTests
{
    /// <summary>
    /// 実際の製品コードへ左右・前後・旋回・装着軸・原点移動の入力を与えます。
    /// </summary>
    /// <param name="check">失敗時にテストを停止する検証関数。</param>
    public static void Run(Action<bool, string> check)
    {
        foreach (var name in new[] { "VIVE Ultimate Tracker", "VIVE Ultimate Tracker 5", "vive ultimate tracker 2" })
            check(UltimateTrackerCorrection.IsTarget("SteamVR", "Tracker", name), "Logged Ultimate model was not detected.");
        foreach (var device in new[] {
            ("SteamVR", "Tracker", "VIVE Tracker 3.0"), ("SteamVR", "Tracker", "VIVE Tracker Pro MV"),
            ("SteamVR", "Controller", "VIVE Ultimate Tracker"), ("SteamVR", "HMD", "VIVE Ultimate Tracker"),
            ("Unity XR", "Tracker", "VIVE Ultimate Tracker"), ("SteamVR", "Tracker", "") })
            check(!UltimateTrackerCorrection.IsTarget(device.Item1, device.Item2, device.Item3),
                "Ultimate-only correction selected an unrelated input.");

        foreach (var worldYaw in new[] { 0f, 45f, 90f, 180f, 270f })
        foreach (var tilt in new[] { 0f, 12f })
        {
            var anchorRotation = Turn(Vector3.UnitY, worldYaw) * Turn(Vector3.UnitX, tilt);
            var anchorTranslation = new Vector3(4, -1, 7);
            var inverseAnchor = Quaternion.Inverse(anchorRotation);
            var pivot = new Vector3(0.56f, 0.92f, -0.11f);
            var correction = UltimateTrackerCorrection.Create(pivot, Vector3.Transform(Vector3.UnitY, inverseAnchor));
            var mounting = Turn(Vector3.UnitZ, 73) * Turn(Vector3.UnitX, -40);
            var start = pivot + Vector3.Transform(new Vector3(-0.2f, -0.85f, 0.1f), inverseAnchor);
            var baseline = correction.Apply(start, mounting);
            var waist = correction.Apply(pivot, mounting);
            check(Near(waist.Position, pivot), "Correction moved its fixed waist pivot.");
            check(MathF.Abs(Vector3.Distance(start, pivot) - Vector3.Distance(baseline.Position, pivot)) < 0.00001f,
                "A rigid correction changed body distances.");

            // Synthetic reversed driver: horizontal motion is inverted but height is correct.
            foreach (var actual in new[] { new Vector3(0, 0, 0.4f), new Vector3(0.3f, 0, 0),
                         new Vector3(-0.3f, 0, 0), new Vector3(0, 0.25f, 0), new Vector3(0.2f, 0.1f, -0.3f) })
            {
                var rawDelta = Vector3.Transform(new Vector3(-actual.X, actual.Y, -actual.Z), inverseAnchor);
                var moved = correction.Apply(start + rawDelta, mounting);
                var worldDelta = Vector3.Transform(moved.Position - baseline.Position, anchorRotation);
                check(Near(worldDelta, actual), $"Reversed driver movement was not corrected at yaw={worldYaw}, tilt={tilt}.");
            }

            // A foot mounted at an arbitrary angle must still rotate its calibrated end bone correctly.
            var target = new Vector3(-0.2f, 0.1f, 0.08f);
            var targetRotation = Turn(Vector3.UnitY, 35);
            var offset = RigidPoseOffset.Between(baseline.Position, baseline.Rotation, target, targetRotation);
            foreach (var turn in new[] { 30f, 90f, 180f, 360f })
            {
                var actualTurn = Turn(Vector3.Normalize(new Vector3(0.6f, 1, 0.2f)), turn);
                var rawTurn = Quaternion.Inverse(correction.Rotation) * actualTurn * correction.Rotation;
                var moved = correction.Apply(start, rawTurn * mounting);
                var driven = offset.Apply(moved.Position, moved.Rotation);
                check(SameRotation(driven.Rotation, actualTurn * targetRotation),
                    "Corrected rotation disagrees with the calibrated foot target.");
                check(Near(driven.Position, baseline.Position + Vector3.Transform(target - baseline.Position, actualTurn)),
                    "Corrected position and rotation separated a calibrated end-bone offset.");
            }

            // Scene recovery keeps the raw correction and only replaces the raw->world anchor.
            var beforePosition = anchorTranslation + Vector3.Transform(baseline.Position, anchorRotation);
            var beforeRotation = anchorRotation * baseline.Rotation;
            var nextRotation = Turn(Vector3.UnitY, -83) * anchorRotation;
            var nextTranslation = new Vector3(-5, 2, 3);
            var remapped = CalibrationRecoveryMath.Remap(beforePosition, beforeRotation,
                anchorTranslation, anchorRotation, nextTranslation, nextRotation);
            var live = correction.Apply(start, mounting);
            check(Near(remapped.Position, nextTranslation + Vector3.Transform(live.Position, nextRotation)) &&
                SameRotation(remapped.Rotation, nextRotation * live.Rotation),
                "Scene remapping diverged from the preserved tracker correction.");
        }

        // Previously rejected log coordinates become ordered against FRONT after a half turn.
        var loggedWaist = new Vector3(0.561f, 0.921f, -0.117f);
        var loggedCorrection = UltimateTrackerCorrection.Create(loggedWaist, Vector3.UnitY);
        var loggedLeft = loggedCorrection.Apply(new(0.626f, 0.059f, -0.023f), Quaternion.Identity).Position;
        var loggedRight = loggedCorrection.Apply(new(0.426f, 0.047f, -0.006f), Quaternion.Identity).Position;
        check(loggedRight.X > loggedLeft.X, "The supplied log's reversed left/right geometry was not corrected.");
        foreach (var tPose in new[] { false, true })
        {
            var ready = CalibrationPoseCheck.Check(new(-0.039f, 1.618f, -0.015f), Vector3.UnitZ, Vector3.UnitZ,
                true, new(-0.7f, 1.4f, 0), new(0.7f, 1.4f, 0), true, loggedWaist, loggedLeft, loggedRight, tPose);
            check(ready.Ready && ready.Warning == null, "Corrected log geometry still reports reversed feet.");
        }
        CheckAlignment(check);
    }

    /// <summary>
    /// v0.48のログにあったメートル単位の共通ずれが、足や腰の回転による移動へ混ざらないことを検証します。
    /// </summary>
    /// <param name="check">失敗時にテストを停止する検証関数。</param>
    private static void CheckAlignment(Action<bool, string> check)
    {
        // Latest (1).log, 20:26 calibration: recorded raw poses and raw->world
        // anchor. The pelvis is logged; foot bone positions and later rotations
        // are synthetic because that version did not record those values.
        var pivot = new Vector3(1.421f, 0.914f, -0.430f);
        var anchorTranslation = new Vector3(0.002f, 0, -0.002f);
        var anchorRotation = Quaternion.Normalize(new Quaternion(0.0002f, -0.0008f, 0.0005f, 1));
        var inverse = Quaternion.Inverse(anchorRotation);
        var headAtStart = new Vector3(0.110f, 1.593f, -0.045f);
        var rawHead = Vector3.Transform(headAtStart - anchorTranslation, inverse);
        var up = Vector3.Transform(Vector3.UnitY, inverse);
        var destination = UltimateTrackerCorrection.AlignWaistBelowHead(pivot, rawHead, up);
        var corrected = UltimateTrackerCorrection.Create(pivot, up, destination);
        var rotationOnly = UltimateTrackerCorrection.Create(pivot, up);
        var bodyPoints = new[] { new Vector3(1.451f, 0.926f, -0.427f),
            new Vector3(1.532f, 0.063f, -0.369f), new Vector3(1.338f, 0.047f, -0.331f) };
        var mounting = Turn(Vector3.UnitX, 45) * Turn(Vector3.UnitY, 22);
        var expectedShift = Vector3.Transform(destination - pivot, anchorRotation);
        check(expectedShift.Length() > 1.3f, "Regression input lost the reported large space mismatch.");
        check(MathF.Abs(expectedShift.Y) < 0.0001f, "Alignment changed the measured floor/waist height.");
        var mappedPivot = anchorTranslation + Vector3.Transform(corrected.Apply(pivot, mounting).Position, anchorRotation);
        check(MathF.Abs(mappedPivot.X - headAtStart.X) < 0.0001f && MathF.Abs(mappedPivot.Z - headAtStart.Z) < 0.0001f,
            "The fixed waist center was not placed below the calibration HMD.");

        foreach (var point in bodyPoints)
        {
            var oldPose = rotationOnly.Apply(point, mounting);
            var newPose = corrected.Apply(point, mounting);
            check(Near(Vector3.Transform(newPose.Position - oldPose.Position, anchorRotation), expectedShift),
                "Body parts received different space translations.");
            check(SameRotation(oldPose.Rotation, newPose.Rotation), "Translation alignment altered mounting rotation.");
            foreach (var delta in new[] { new Vector3(0.15f, 0, 0), new Vector3(0, 0, 0.2f), new Vector3(0, 0.1f, 0) })
            {
                var rawDelta = Vector3.Transform(new Vector3(-delta.X, delta.Y, -delta.Z), inverse);
                var moved = corrected.Apply(point + rawDelta, mounting);
                check(Near(Vector3.Transform(moved.Position - newPose.Position, anchorRotation), delta),
                    "Fixed alignment scaled or reversed a subsequent movement.");
            }
            var worldPosition = anchorTranslation + Vector3.Transform(newPose.Position, anchorRotation);
            var worldRotation = anchorRotation * newPose.Rotation;
            var nextTranslation = new Vector3(3, -2, 5);
            var nextRotation = Turn(Vector3.UnitY, -120) * anchorRotation;
            var saved = CalibrationRecoveryMath.Remap(worldPosition, worldRotation,
                anchorTranslation, anchorRotation, nextTranslation, nextRotation);
            // Use the same correction after head/world origin changes; never re-learn the live HMD position.
            var live = corrected.Apply(point, mounting);
            check(Near(saved.Position, nextTranslation + Vector3.Transform(live.Position, nextRotation)),
                "Scene recovery changed the saved translation correction.");
        }

        var newFoot = corrected.Apply(bodyPoints[1], mounting);
        var oldFoot = rotationOnly.Apply(bodyPoints[1], mounting);
        var target = new Vector3(-0.01f, 0.06f, -0.025f);
        var newFootWorld = anchorTranslation + Vector3.Transform(newFoot.Position, anchorRotation);
        var oldFootWorld = anchorTranslation + Vector3.Transform(oldFoot.Position, anchorRotation);
        var rotationWorld = anchorRotation * newFoot.Rotation;
        var newOffset = RigidPoseOffset.Between(newFootWorld, rotationWorld, target, Quaternion.Identity);
        var oldOffset = RigidPoseOffset.Between(oldFootWorld, rotationWorld, target, Quaternion.Identity);
        check(oldOffset.Position.Length() > 1.2f && newOffset.Position.Length() < 0.2f,
            "The large common translation still became a rotating foot offset.");
        var roll = Turn(Vector3.UnitZ, -60);
        var oldRaised = oldOffset.Apply(oldFootWorld, roll * rotationWorld);
        var newRaised = newOffset.Apply(newFootWorld, roll * rotationWorld);
        check(oldRaised.Position.Y - target.Y > 1f && MathF.Abs(newRaised.Position.Y - target.Y) < 0.2f,
            "The regression no longer reproduces/removes the excessive rise caused by a rolled foot.");

        var oldWaist = anchorTranslation + Vector3.Transform(rotationOnly.Apply(bodyPoints[0], mounting).Position, anchorRotation);
        var newWaist = oldWaist + expectedShift;
        var pelvis = new Vector3(0.11f, 1.04f, -0.07f);
        var yaw = Turn(Vector3.UnitY, 30);
        var oldSwing = oldWaist + Vector3.Transform(pelvis - oldWaist, yaw);
        var newSwing = newWaist + Vector3.Transform(pelvis - newWaist, yaw);
        check(Vector3.Distance(oldSwing, pelvis) > 0.6f && Vector3.Distance(newSwing, pelvis) < 0.05f,
            "Common space offset still makes the pelvis orbit its waist tracker.");

        foreach (var tilt in new[] { 0f, 15f, -30f })
        {
            var basis = Turn(Vector3.UnitZ, tilt) * Turn(Vector3.UnitY, 71);
            var origin = new Vector3(5, -3, 8);
            var waist = origin + Vector3.Transform(new Vector3(1.5f, 0.9f, -0.4f), basis);
            var head = origin + Vector3.Transform(new Vector3(0, 1.6f, 0), basis);
            var axis = Vector3.Transform(Vector3.UnitY, basis);
            var aligned = UltimateTrackerCorrection.AlignWaistBelowHead(waist, head, axis);
            check(Near(aligned, origin + Vector3.Transform(new Vector3(0, 0.9f, 0), basis)),
                "Alignment assumed raw Y was the game vertical axis.");
        }
    }

    /// <summary>ベクトルの差を浮動小数点誤差の範囲で比較します。</summary>
    private static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 0.0001f;

    /// <summary>符号の異なるQuaternionも同じ回転として比較します。</summary>
    private static bool SameRotation(Quaternion a, Quaternion b) =>
        MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b))) > 0.99999f;

    /// <summary>指定軸周りの度数を単位Quaternionへ変換します。</summary>
    private static Quaternion Turn(Vector3 axis, float degrees) =>
        Quaternion.CreateFromAxisAngle(axis, degrees * MathF.PI / 180f);
}
