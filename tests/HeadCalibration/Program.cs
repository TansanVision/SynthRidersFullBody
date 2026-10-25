using SynthRidersFullBody;
using UnityEngine;
using NVector = System.Numerics.Vector3;
using NQuat = System.Numerics.Quaternion;

var checks = 0;
TrackingSpaceTests.Run(Check);
HandAdjustmentTests.Run(Check);
foreach (var size in new[] { .2f, 1f, 2.5f })
foreach (var yaw in new[] { 0f, 90f, 180f, 270f })
{
    var turn = NQuat.CreateFromAxisAngle(NVector.UnitY, yaw * MathF.PI / 180);
    var axes = NQuat.CreateFromAxisAngle(NVector.UnitZ, .6f);
    var joint = new Pose(new(0, 1.48f * size, -.06f * size), Quaternion.U(turn * axes));
    var input = new Pose(new(-.02f * size, 1.57f * size, 0), Quaternion.U(turn));
    var source = Bone(input);
    var headset = Bone(input);
    var head = Bone(joint);
    Transform? connected = source;
    var correction = new CalibratedHeadTarget();
    correction.Capture(input, joint);
    correction.Attach(head, headset, () => connected, t => connected = t);
    var owned = connected!;
    Check(owned != source && Near(owned, joint), "Attach failed to align the head joint before root calibration.");
    correction.Update(true, 1);
    Check(Near(owned, joint), "The logged 9 cm head mismatch survived calibration.");
    Check(Near(source, input), "Calibration modified the game-owned head input.");

    // Source motion is relative to the saved reference, including yaw/pitch/roll.
    var motion = NQuat.CreateFromYawPitchRoll(1.4f, -.4f, .3f);
    var translation = new NVector(.3f, -.2f, .7f);
    var movedInput = Move(input, motion, translation);
    headset.SetPositionAndRotation(movedInput.Position, movedInput.Rotation);
    for (var i = 0; i < 5; i++)
    {
        // Some drivers update the bound solver target instead of the detached native transform.
        owned.SetPositionAndRotation(movedInput.Position, movedInput.Rotation);
        correction.Update(true, 1);
        Check(Near(owned, Move(joint, motion, translation)), "A stale native target or direct game write froze/corrupted head motion.");
    }
    Check(Near(source, input) && Near(headset, movedInput), "Head correction modified the native target or HMD.");
    correction.Update(false, 1);
    Check(connected == source, "Stopping full-body driving did not restore the native target.");
    correction.Update(true, 0);
    Check(connected == owned && Near(owned, input), "Recovery did not start from the native target.");
    correction.Update(true, 1);
    Check(Near(owned, Move(joint, motion, translation)), "Recovery lost the calibrated head offset.");

    // Saving the original input, not the current moving head, preserves scene recovery.
    var oldSpace = new TrackingSpaceAnchor(new(0, 0, 0), Quaternion.identity);
    var newSpace = new TrackingSpaceAnchor(Vector3.U(translation), Quaternion.U(motion));
    var saved = new CalibrationSession("test", "rig", Array.Empty<CalibrationBone>(), joint,
        new(size, size, size), joint, joint, joint, new(), "test", oldSpace, input);
    var savedInput = saved.Remap(saved.Headset!.Value, newSpace);
    var savedJoint = saved.Remap(saved.Root, newSpace);
    correction.Detach();
    Check(connected == source && owned == null, "Detach leaked its target or destroyed the native input.");
    // Player is currently crouching: attach must still use the ORIGINAL reference for root setup.
    var lookingDown = Quaternion.U(NQuat.CreateFromYawPitchRoll(0, MathF.PI / 3, 0));
    source.SetPositionAndRotation(new(1, .7f, 1), lookingDown);
    var newHeadset = Bone(new(new(1, .7f, 1), lookingDown));
    UnityEngine.Object.Destroy(headset.gameObject); // The scene has replaced the HMD.
    var recovered = new CalibratedHeadTarget();
    recovered.Capture(savedInput, savedJoint);
    recovered.Attach(Bone(savedJoint), newHeadset, () => connected, t => connected = t);
    Check(Near(connected!, savedJoint), "Recovery learned the player's current crouch as its calibration.");
    for (var i = 0; i < 3; i++)
    {
        var live = Move(savedInput, NQuat.CreateFromYawPitchRoll(.1f * i, -.2f * i, .05f * i), NVector.Zero);
        newHeadset.SetPositionAndRotation(live.Position, live.Rotation);
        recovered.Update(true, 1);
        Check(Near(connected!, Move(savedJoint, NQuat.CreateFromYawPitchRoll(.1f * i, -.2f * i, .05f * i), NVector.Zero)),
            "Scene recovery kept the down-facing native target or learned the current crouch.");
    }
    var replacement = Bone(joint);
    connected = replacement;
    Check(!recovered.OwnsBinding, "A foreign target replacement was not detected.");
    recovered.Update(true, 1);
    recovered.Detach();
    Check(connected == replacement, "Cleanup overwrote a newer game target.");
}

var native = Bone(new(new(0, 1.6f, 0), Quaternion.identity));
Transform? current = native;
var skipped = new CalibratedHeadTarget();
skipped.Capture(null, default);
var liveHeadset = Bone(new(native.position, native.rotation));
skipped.Attach(native, liveHeadset, () => current, t => current = t);
Check(current == native && !skipped.Calibrated, "Missing calibration/assigned head tracker created a competing target.");
skipped.Capture(new Pose(new(float.NaN, 0, 0), Quaternion.identity), default);
Check(!skipped.Calibrated, "Non-finite calibration was accepted.");
skipped.Capture(new Pose(native.position, native.rotation), new Pose(native.position, native.rotation));
skipped.Attach(native, liveHeadset, () => current, t => current = t);
UnityEngine.Object.Destroy(liveHeadset.gameObject);
Check(!skipped.OwnsBinding, "A destroyed headset looked like a usable connection.");
skipped.Detach();
Check(current == native, "Headset loss did not restore the game's target.");
skipped.Attach(native, Bone(new(native.position, native.rotation)), () => current, t => current = t);
UnityEngine.Object.Destroy(native.gameObject);
Check(!skipped.OwnsBinding, "A destroyed head source looked like a usable connection.");
skipped.Detach();
Check(current == null, "Destroyed input cleanup left a live owned target attached.");

Console.WriteLine($"Head target calibration / recovery tests passed ({checks} checks).");

/// <summary>テスト用ボーンを値の姿勢から作成します。</summary>
static Transform Bone(Pose pose)
{
    var t = new GameObject("test").transform;
    t.SetPositionAndRotation(pose.Position, pose.Rotation);
    return t;
}
/// <summary>同じ剛体移動を入力と期待する骨格へ適用します。</summary>
static Pose Move(Pose pose, NQuat rotation, NVector translation) =>
    new(Vector3.U(translation + NVector.Transform(pose.Position.N, rotation)), Quaternion.U(rotation * pose.Rotation.N));
/// <summary>位置と回転の一致を独立に確認します。</summary>
static bool Near(Transform actual, Pose expected) =>
    NVector.Distance(actual.position.N, expected.Position.N) < .00001f &&
    Math.Abs(NQuat.Dot(actual.rotation.N, expected.Rotation.N)) > .99999f;
/// <summary>失敗時に理由を出力し、成功件数を集計します。</summary>
void Check(bool condition, string reason)
{
    if (!condition) throw new InvalidOperationException(reason);
    checks++;
}
