using System.Numerics;
using SynthRidersFullBody;

var checks = 0;
KneeReferenceTests.Run(Check);
KneePoseSampleTests.Run(Check);
TrackedFootPoseTests.Run(Check);
TrackedAnkleConstraintTests.Run(Check);
StraightLegTests.Run(Check);
HeadPoseFreshnessTests.Run(Check);
PalmPoseTests.Run(Check);
FootPoseWindowTests.Run(Check);
MenuBustCameraTests.Run(Check);
CameraContextTests.Run(Check);
// Heading must remain stable across the old projected-forward singularity at 90°.
foreach (var heading in new[] { -170f, -90f, 0f, 45f, 170f })
{
    var yaw = Turn(Vector3.UnitY, heading);
    var previous = yaw;
    foreach (var bend in new[] { 0f, 45f, 85f, 89.9f, 90f, 90.1f, 110f, 170f, 180f })
    {
        var delta = yaw * Turn(Vector3.UnitX, bend);
        var result = AvatarPoseMath.PelvisYaw(delta, previous);
        Check(MathF.Abs(Quaternion.Dot(result, yaw)) > 0.9999f,
            $"Heading flipped during a {bend} degree bend with yaw {heading}.");
        var oppositeSign = AvatarPoseMath.PelvisYaw(-delta, previous);
        Check(MathF.Abs(Quaternion.Dot(result, oppositeSign)) > 0.9999f,
            "Equivalent quaternion signs produced different pelvis headings.");
        previous = result;
    }
}

// The pole for a forward kick follows a continuous arc from forward to upward.
var previousBend = Vector3.UnitZ;
for (var degrees = 0; degrees <= 150; degrees++)
{
    var lift = Turn(Vector3.UnitX, -degrees);
    var axis = Vector3.Transform(-Vector3.UnitY, lift);
    var expectedBend = Vector3.Transform(Vector3.UnitZ, lift);
    var bend = AvatarPoseMath.LegBend(-Vector3.UnitY, Vector3.UnitZ, axis);
    Check(Vector3.Dot(bend, expectedBend) > 0.9999f, $"Knee pole went sideways at lift {degrees}.");
    Check(Vector3.Dot(bend, previousBend) > 0.999f, "Knee pole discontinuity while lifting the foot.");
    Check(MathF.Abs(Vector3.Dot(bend, axis)) < 0.0001f, "Knee pole is not perpendicular to the leg.");
    previousBend = bend;
}

// Rotating the whole rig must rotate the solution without depending on bone-local axes.
var rigTurn = Turn(Vector3.UnitY, 123f);
var referenceAxis = Vector3.Normalize(new Vector3(-0.08f, -1f, 0.05f));
var requestedAxis = Vector3.Normalize(new Vector3(0.15f, -0.2f, 1f));
var originalBend = AvatarPoseMath.LegBend(referenceAxis, Vector3.UnitZ, requestedAxis);
var rotatedBend = AvatarPoseMath.LegBend(Vector3.Transform(referenceAxis, rigTurn),
    Vector3.Transform(Vector3.UnitZ, rigTurn), Vector3.Transform(requestedAxis, rigTurn));
Check(Vector3.Distance(Vector3.Transform(originalBend, rigTurn), rotatedBend) < 0.0001f,
    "Rotating the avatar changed its knee bend plane.");
foreach (var axis in new[] { Vector3.UnitY, Vector3.Zero, -Vector3.UnitY, Vector3.UnitZ })
{
    var bend = AvatarPoseMath.LegBend(-Vector3.UnitY, -Vector3.UnitY, axis);
    Check(float.IsFinite(bend.X) && float.IsFinite(bend.Y) && float.IsFinite(bend.Z) &&
        MathF.Abs(bend.Length() - 1f) < 0.0001f, "Degenerate reference produced an invalid knee pole.");
}

// Both segment lengths and the flexion limit must survive unreachable and zero-distance targets.
foreach (var lengths in new[] { (0.45f, 0.45f), (0.60f, 0.35f), (0.02f, 0.015f) })
foreach (var target in new[] { Vector3.Zero, new Vector3(0f, -0.1f, 0f),
    new Vector3(0f, -0.7f, 0.2f), new Vector3(0f, 0f, 3f), new Vector3(-0.3f, -2f, 0f) })
foreach (var limit in new[] { 60f, 120f, 145f, 170f })
{
    var bend = AvatarPoseMath.LegBend(-Vector3.UnitY, Vector3.UnitZ, target);
    Check(AvatarPoseMath.SolveTwoBone(Vector3.Zero, target, -Vector3.UnitY, bend,
        lengths.Item1, lengths.Item2, limit, out var knee, out var foot), "Valid leg was rejected.");
    Check(MathF.Abs(knee.Length() - lengths.Item1) < 0.0001f, "Upper leg length changed.");
    Check(MathF.Abs(Vector3.Distance(knee, foot) - lengths.Item2) < 0.0001f,
        "Lower leg length changed.");
    var flexion = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(knee),
        Vector3.Normalize(foot - knee)), -1f, 1f)) * 180f / MathF.PI;
    Check(flexion <= limit + 0.02f, $"Knee flexion {flexion} exceeded limit {limit}.");
}
Check(!AvatarPoseMath.SolveTwoBone(Vector3.Zero, new Vector3(float.NaN, 0, 0),
    -Vector3.UnitY, Vector3.UnitZ, 0.45f, 0.45f, 145f, out _, out _),
    "Non-finite target was accepted.");
Check(!AvatarPoseMath.SolveTwoBone(Vector3.Zero, Vector3.One,
    -Vector3.UnitY, Vector3.UnitZ, 0f, 0.45f, 145f, out _, out _),
    "Zero-length bone was accepted.");
// A lost foot must not disable a valid waist. Every role involved in duplicate
// assignment must be disabled, including all three roles sharing one device.
for (var mask = 0; mask < 8; mask++)
{
    var waist = (mask & 1) != 0;
    var left = (mask & 2) != 0;
    var right = (mask & 4) != 0;
    Check(TrackerAvailability.Distinct(1, 2, 3, waist, left, right) ==
        new TrackerAvailability(waist, left, right), "Independent tracker availability was lost.");
}
Check(TrackerAvailability.Distinct(1, 1, 1, true, true, true) == default,
    "Three-way collision left a role enabled.");
Check(TrackerAvailability.Distinct(1, 1, 3, true, true, true) == new TrackerAvailability(false, false, true),
    "Waist/left collision disabled an unrelated foot.");
Check(TrackerAvailability.Distinct(1, 2, 1, true, true, true) == new TrackerAvailability(false, true, false),
    "Waist/right collision disabled an unrelated foot.");
Check(TrackerAvailability.Distinct(1, 2, 2, true, true, true) == new TrackerAvailability(true, false, false),
    "Foot collision disabled the valid waist.");
// Reproduce the log's four-tracker arrangement. The highest tracker is on
// the HMD, not the waist; turning or translating the play space must not change this.
Check(!TrackerAssignmentPolicy.CanFillAutomatically(2, 3), "Assigned a partial startup frame.");
Check(!TrackerAssignmentPolicy.CanFillAutomatically(4, 3), "Guessed among extra body trackers.");
Check(!TrackerAssignmentPolicy.CanFillAutomatically(0, 0), "Reassigned existing roles.");
Check(TrackerAssignmentPolicy.CanFillAutomatically(3, 3), "Rejected the three body trackers.");
Check(TrackerAssignmentPolicy.CanFillAutomatically(1, 1), "Rejected the only remaining role candidate.");
foreach (var degrees in new[] { 0f, 90f, 180f, 270f, 360f })
{
    var yaw = Turn(Vector3.UnitY, degrees);
    var shift = new Vector3(1.2f, 0.3f, -0.7f);
    var head = Vector3.Transform(new Vector3(0f, 1.65f, 0f), yaw) + shift;
    var trackers = new[] { new Vector3(0f, 1.70f, -0.12f), new Vector3(0f, 0.95f, -0.15f),
        new Vector3(-0.18f, 0.10f, 0.03f), new Vector3(0.18f, 0.10f, 0.03f) };
    var candidates = trackers.Select((p, i) => (Position: Vector3.Transform(p, yaw) + shift, Index: i))
        .Where(t => TrackerAssignmentPolicy.IsBodyCandidate(head, t.Position)).ToArray();
    Check(candidates.Length == 3, "HMD tracker was included or a body tracker was excluded.");
    Check(candidates.OrderByDescending(t => t.Position.Y).First().Index == 1,
        "The highest body candidate was not the real waist.");
}
Check(!TrackerAssignmentPolicy.IsBodyCandidate(new(0, 1.65f, 0), new(0, 1.42f, 0)),
    "Head-mounted tracker below the HMD was accepted.");
Check(!TrackerAssignmentPolicy.IsBodyCandidate(Vector3.Zero, new(float.NaN, -1, 0)),
    "Invalid body candidate was accepted.");

// A toe target has its own local orientation on many VRMs. Reconstructing the
// ankle from the toe target must preserve the same foot pose through a full turn.
var anklePosition = new Vector3(-0.2f, 0.15f, 0.3f);
var ankleRotation = Turn(Vector3.UnitZ, 25f) * Turn(Vector3.UnitY, -40f);
var toePosition = anklePosition + Vector3.Transform(new Vector3(0, -0.06f, 0.18f), ankleRotation);
var toeRotation = ankleRotation * Turn(Vector3.UnitX, 75f);
var footOffset = RigidPoseOffset.Between(anklePosition, ankleRotation, toePosition, toeRotation);
for (var degrees = 0; degrees <= 360; degrees += 10)
{
    var turn = Turn(Vector3.UnitY, degrees);
    var position = Vector3.Transform(anklePosition, turn) + new Vector3(1, 0, 2);
    var rotation = turn * ankleRotation;
    var end = footOffset.Apply(position, rotation);
    Check(Vector3.Distance(end.Position, Vector3.Transform(toePosition, turn) + new Vector3(1, 0, 2)) < 0.0001f,
        "Toe position offset drifted during a turn.");
    Check(MathF.Abs(Quaternion.Dot(end.Rotation, turn * toeRotation)) > 0.9999f,
        "Toe axes were lost during a turn.");
    var reconstructed = end.Position - Vector3.Transform(footOffset.Position, rotation);
    Check(Vector3.Distance(reconstructed, position) < 0.0001f, "Ankle position drifted at the toe target.");
}
var noToe = RigidPoseOffset.Between(anklePosition, ankleRotation, anklePosition, ankleRotation);
var unchanged = noToe.Apply(anklePosition, ankleRotation);
Check(Vector3.Distance(unchanged.Position, anklePosition) < 0.0001f &&
    MathF.Abs(Quaternion.Dot(unchanged.Rotation, ankleRotation)) > 0.9999f,
    "A rig without toes changed its foot target.");

// Version-specific generated camera getters must be optional, including when
// a getter exists but fails internally, or its declared type changes.
var oldCamera = new OldCameraMembers();
var newCamera = new NewCameraMembers();
Check(Equals(OptionalMember.Read(oldCamera, "customCamera"), 42), "Camera property was not read.");
Check(OptionalMember.Read(oldCamera, "customCameraTransform") == null, "Missing property was not optional.");
Check(Equals(OptionalMember.Read(newCamera, "customCameraTransform"), "transform"), "Changed member type was rejected.");
Check(OptionalMember.Read(newCamera, "customCamera") == null, "Throwing optional getter escaped.");
// Two physical inputs per foot: preserve a fixed ankle and foot length through
// turns and toe lifts even when a VRM uses unusual local bone axes.
foreach (var localRig in new[] { Quaternion.Identity, Turn(Vector3.UnitY, 75), Turn(Vector3.UnitX, -90) })
foreach (var heading in Enumerable.Range(0, 37).Select(n => n * 10f))
foreach (var lift in new[] { -35f, 0f, 45f })
{
    var yaw = Turn(Vector3.UnitY, heading);
    var localToe = Vector3.Transform(new Vector3(0, 0, 0.2f), Quaternion.Inverse(localRig));
    var rotation = yaw * localRig;
    var ankle = Vector3.Transform(new Vector3(-0.2f, 0.15f, 0), yaw) + new Vector3(1, 0, 2);
    var aim = Vector3.Transform(Vector3.UnitZ, yaw * Turn(Vector3.UnitX, lift));
    var solved = DualFootMath.Rotation(ankle, rotation, localToe, ankle + aim * 0.23f);
    var end = ankle + Vector3.Transform(localToe, solved);
    Check(Vector3.Dot(Vector3.Normalize(end - ankle), aim) > 0.9999f, "Dual toe axis drifted through a turn.");
    Check(MathF.Abs(Vector3.Distance(end, ankle) - 0.2f) < 0.0001f, "Dual toe stretched the foot.");
    Check(Vector3.Distance(end - Vector3.Transform(localToe, solved), ankle) < 0.0001f, "Dual toe moved the ankle.");
}
foreach (var bad in new[] { Vector3.Zero, new Vector3(float.NaN, 0, 0), -Vector3.UnitZ })
    Check(DualFootMath.Rotation(Vector3.Zero, Quaternion.Identity, Vector3.UnitZ * 0.2f, bad) == Quaternion.Identity,
        "Degenerate toe measurement caused a foot flip.");
var roll = Turn(Vector3.UnitZ, 28f);
var rolled = DualFootMath.Rotation(Vector3.Zero, roll, Vector3.UnitZ * 0.2f, Vector3.UnitZ * 0.3f);
Check(Math.Abs(Quaternion.Dot(roll, rolled)) > 0.9999f, "Dual foot discarded ankle roll.");

var body = new[] {
    new BodyCandidate("head-reference", new(0, 1.7f, -0.1f)),
    new BodyCandidate("waist", new(0, 0.95f, -0.12f)),
    new BodyCandidate("la", new(-0.18f, 0.16f, 0)), new BodyCandidate("ra", new(0.18f, 0.16f, 0)),
    new BodyCandidate("lt", new(-0.18f, 0.06f, 0.15f)), new BodyCandidate("rt", new(0.18f, 0.06f, 0.15f)) };
foreach (var heading in new[] { 0f, 90f, 180f, 270f, 360f })
{
    var turn = Turn(Vector3.UnitY, heading);
    var shift = new Vector3(1.4f, -0.25f, 2);
    var suggestion = AutoBodyAssignment.Suggest(body.Select(d => d with { Position = Vector3.Transform(d.Position, turn) + shift }),
        Vector3.Transform(new Vector3(0, 1.65f, 0), turn) + shift, Vector3.Transform(Vector3.UnitZ, turn));
    Check(suggestion.Count == 5 && suggestion[TrackerRole.Waist] == "waist" &&
        suggestion[TrackerRole.LeftFoot] == "la" && suggestion[TrackerRole.RightFoot] == "ra" &&
        suggestion[TrackerRole.LeftToes] == "lt" && suggestion[TrackerRole.RightToes] == "rt",
        "AUTO swapped roles with a HMD reference plus five body trackers.");
}
var three = AutoBodyAssignment.Suggest(body.Take(4), new(0, 1.65f, 0), Vector3.UnitZ);
Check(three.Count == 3 && three[TrackerRole.Waist] == "waist", "AUTO rejected the normal three body trackers.");
Check(AutoBodyAssignment.Suggest(body.Take(3), new(0, 1.65f, 0), Vector3.UnitZ).Count == 0,
    "AUTO guessed from an incomplete startup frame.");
var ambiguous = body.Select(d => d.Key == "lt" ? d with { Position = new(-0.18f, 0.15f, 0.15f) } : d);
var partial = AutoBodyAssignment.Suggest(ambiguous, new(0, 1.65f, 0), Vector3.UnitZ);
Check(!partial.ContainsKey(TrackerRole.LeftFoot) && !partial.ContainsKey(TrackerRole.LeftToes) &&
    partial.ContainsKey(TrackerRole.RightFoot), "AUTO guessed ambiguous ankle/toes or discarded unrelated suggestions.");
var ambiguousWaist = AutoBodyAssignment.Suggest(body.Append(new("extra", new(0.1f, 1f, 0.1f))), new(0, 1.65f, 0), Vector3.UnitZ);
Check(!ambiguousWaist.ContainsKey(TrackerRole.Waist), "AUTO guessed among multiple torso candidates.");
// Replay the uniform size changes observed at scene boundaries in Latest.log.
// The original calibration remains the source even after repeated round trips.
var originalScale = new Vector3(2.645f);
var originalTracker = new Vector3(-0.17f, 0.12f, 0.04f);
var originalTrackerRotation = Turn(Vector3.UnitY, 17);
var localBone = new Vector3(-0.065f, 0.04f, 0.02f);
var oldOrigin = new Vector3(0.2f, 0, -0.3f);
var oldYaw = Turn(Vector3.UnitY, -20);
foreach (var scaleValue in new[] { 2.41f, 2.645f, 2.44f, 2.604f, 2.645f })
foreach (var sceneYaw in new[] { 0f, 90f, 180f, 270f })
{
    var sceneScale = new Vector3(scaleValue);
    Check(CalibrationRecoveryMath.CompatibleScale(originalScale, sceneScale), "Scene's uniform VRM scale change rejected.");
    var newOrigin = new Vector3(3, 0.25f, -2);
    var newYaw = Turn(Vector3.UnitY, sceneYaw);
    var oldWorld = oldOrigin + Vector3.Transform(originalTracker, oldYaw);
    var remapped = CalibrationRecoveryMath.Remap(oldWorld, oldYaw * originalTrackerRotation,
        oldOrigin, oldYaw, newOrigin, newYaw);
    Check(Vector3.Distance(remapped.Position, newOrigin + Vector3.Transform(originalTracker, newYaw)) < 0.0001f,
        "Scene translation/rotation moved the saved tracker relative to tracking space.");
    Check(MathF.Abs(Quaternion.Dot(remapped.Rotation, newYaw * originalTrackerRotation)) > 0.9999f,
        "Scene transition changed tracker mounting rotation.");
    var bone = newOrigin + Vector3.Transform(localBone * sceneScale, newYaw);
    var offset = RigidPoseOffset.Between(remapped.Position, remapped.Rotation, bone, newYaw);
    var movement = Vector3.Transform(new Vector3(0.1f, 0.3f, 0.2f), newYaw);
    var resumed = offset.Apply(remapped.Position + movement, remapped.Rotation);
    Check(Vector3.Distance(resumed.Position, bone + movement) < 0.0001f,
        "Resumed target used stale size or learned the player's scene-loading pose.");
    var roundTrip = CalibrationRecoveryMath.Remap(remapped.Position, remapped.Rotation,
        newOrigin, newYaw, oldOrigin, oldYaw);
    Check(Vector3.Distance(roundTrip.Position, oldWorld) < 0.0001f, "Scene round trip accumulated a position offset.");
}
foreach (var badScale in new[] { Vector3.Zero, new Vector3(float.NaN), new Vector3(1, 2, 1), -Vector3.One })
    Check(!CalibrationRecoveryMath.CompatibleScale(originalScale, badScale), "Invalid or deformed rig was resumed.");
var recovery = new CalibrationRecoveryGate();
Check(!recovery.Ready("rig-a", Vector3.One, 0), "Resumed rig during initial loading.");
Check(!recovery.Ready("rig-a", Vector3.One, 0.3f), "Resumed before rig stabilized.");
Check(!recovery.Ready("rig-a", Vector3.One * 0.91f, 0.4f), "Scale animation did not restart stability timer.");
Check(recovery.Ready("rig-a", Vector3.One * 0.91f, 0.95f), "Stable scale never resumed.");
Check(!recovery.Ready("rig-b", Vector3.One * 0.91f, 1f), "Replaced solver bypassed initialization wait.");
recovery.Reset();
Check(!recovery.Ready("rig-b", Vector3.One * 0.91f, 3f), "Temporary rig loss retained a stale readiness timer.");
Check(recovery.Ready("rig-b", Vector3.One * 0.91f, 3.6f), "Rig did not recover after temporary loss.");

// Enlarged detail controls must stay near the user and leave projected entry
// button bounds clear, even for distant or low score anchors.
var eye = new Vector3(0, 1.65f, 0);
foreach (var distance in new[] { 0.6f, 1.15f, 1.6f, 3f, 4.5f })
foreach (var height in new[] { 0.7f, 1.3f, 1.65f, 2f })
{
    var entry = new Vector3(0, height, distance);
    var entryScale = VrPanelLayout.EntryScale(Vector3.Distance(eye, entry));
    var panel = VrPanelLayout.DetailPosition(eye, entry, entryScale);
    Check(panel.Z >= 1.15f && panel.Z <= 1.6f, "Detail controls remained at distant score anchor.");
    var ratio = panel.Z / distance;
    var projectedEntryY = eye.Y + (height - eye.Y) * ratio;
    Check(Math.Abs(panel.Y - projectedEntryY) > 400 * VrPanelLayout.PanelScale + 130 * entryScale * ratio + 0.11f,
        "Enlarged details obscured the permanent entry buttons.");
}
Check(VrPanelLayout.PanelScale == 0.00125f, "Panel physical scale is not 25% larger.");
// Large models and importer unit differences must produce the same physical
// rig, with the root pivot anywhere and after repeated calibrations.
foreach (var modelSize in new[] { 0.01f, 0.3f, 1f, 3f, 10f, 100f })
foreach (var origin in new[] { Vector3.Zero, new Vector3(2, 3, -4) })
{
    var modelHead = origin + new Vector3(0, 1.65f, 0.04f) * modelSize;
    var modelLeft = origin + new Vector3(-0.1f, 0.1f, 0) * modelSize;
    var modelRight = origin + new Vector3(0.1f, 0.1f, 0) * modelSize;
    var userHead = new Vector3(3, 1.8f, 2);
    var userLeft = new Vector3(2.9f, 0.12f, 2);
    var userRight = new Vector3(3.1f, 0.12f, 2);
    Check(AvatarScaleMath.TryFit(origin, new Vector3(modelSize), modelHead, modelLeft, modelRight,
        userHead, userLeft, userRight, out var fittedRoot, out var fittedScale, out var factor),
        "Size fit rejected a valid large/small model.");
    Vector3 Fitted(Vector3 point) => fittedRoot + (point - origin) * factor;
    var fittedHead = Fitted(modelHead);
    var fittedLeft = Fitted(modelLeft);
    var fittedRight = Fitted(modelRight);
    Check(Vector3.Distance(fittedHead, userHead) < 0.0001f, "Size fit moved the head away from its target.");
    Check(MathF.Abs((fittedLeft.Y + fittedRight.Y) * 0.5f - userLeft.Y) < 0.0001f,
        "Size fit left the feet above/below the calibration plane.");
    Check(MathF.Abs(fittedScale.Y - 1.68f / 1.55f) < 0.0001f, "Model units affected the fitted size.");
    Check(AvatarScaleMath.TryFit(fittedRoot, fittedScale, fittedHead, fittedLeft, fittedRight,
        userHead, userLeft, userRight, out var refittedRoot, out var refittedScale, out var repeatFactor) &&
        MathF.Abs(repeatFactor - 1f) < 0.0001f && Vector3.Distance(fittedRoot, refittedRoot) < 0.0001f &&
        Vector3.Distance(fittedScale, refittedScale) < 0.0001f, "Repeated size fit accumulated drift.");
}
foreach (var invalidHead in new[] { Vector3.Zero, new Vector3(0, -1, 0), new Vector3(0, 4, 0), new Vector3(float.NaN) })
    Check(!AvatarScaleMath.TryFit(Vector3.Zero, Vector3.One, new(0, 2, 0), Vector3.Zero, Vector3.Zero,
        invalidHead, Vector3.Zero, Vector3.Zero, out _, out _, out _), "Invalid standing span resized the avatar.");
Check(!AvatarScaleMath.TryFit(Vector3.Zero, Vector3.One, new(0, 2, 0), Vector3.Zero, Vector3.Zero,
    new(0, 1.7f, 0), Vector3.Zero, new(0, 0.4f, 0), out _, out _, out _), "A lifted foot changed calibration size.");
CameraResolverTests.Run(Check);
CameraGuideDrawTests.Run(Check);
AvatarHmdVisibilityTests.Run(Check);
AvatarHeadsetRenderGuardTests.Run(Check);
ToeRotationTests.Run(Check);
CalibrationPoseTests.Run(Check);
UltimateTrackerCorrectionTests.Run(Check);
ExpressionControlTests.Run(Check);
AvatarExpressionTests.Run(Check);
UiLocalizationTests.Run(Check);
PanelInteractionTests.Run(Check);
CalibrationFlowTests.Run(Check);
AssignmentDraftTests.Run(Check);
UiAppearanceTests.Run(Check);
Console.WriteLine($"Pose math / tracker assignment / camera compatibility / scene recovery / UI layout / size fit / expressions tests passed ({checks} checks).");

void Check(bool passed, string message)
{
    checks++;
    if (!passed) throw new Exception(message);
}

static Quaternion Turn(Vector3 axis, float degrees) =>
    Quaternion.CreateFromAxisAngle(axis, degrees * MathF.PI / 180f);

sealed class OldCameraMembers { public int customCamera => 42; }
sealed class NewCameraMembers
{
    public string customCameraTransform = "transform";
    public object customCamera => throw new MissingMethodException("Old accessor is unavailable");
}

