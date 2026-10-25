using SynthRidersFullBody;
using UnityEngine;
using N = System.Numerics.Vector3;

internal static class TrackingSpaceTests
{
    /// <summary>頭入力の時間差を床へ混ぜず、曲間の移動でAPI間の登録が変化しないことを確認します。</summary>
    public static void Run(Action<bool, string> check)
    {
        var camera = new Camera();
        var raw = new Pose(new(-.216f, 1.578f, -1.284f), Quaternion.AngleAxis(-7, new(1, 0, 0)));
        foreach (var yaw in new[] { -170f, 0f, 90f, 179f })
        foreach (var pitch in new[] { -90f, -32f, 0f, 60f, 90f })
        {
            camera.transform.SetPositionAndRotation(new(.2f, 1.6f, 0),
                Quaternion.AngleAxis(yaw, new(0, 1, 0)) * Quaternion.AngleAxis(pitch, new(1, 0, 0)));
            var anchor = TrackingSpaceAnchor.FromHead(raw, camera);
            check(N.Distance((anchor.Rotation * new Vector3(0, 1, 0)).N, N.UnitY) < .00001f,
                "Mismatched/stale head pitch tilted the standing tracker floor.");
            check(N.Distance(anchor.ToWorld(raw).Position.N, camera.transform.position.N) < .00001f, "Level registration lost HMD position.");
            var feet = anchor.ToWorld(new(new(-.4f, .07f, -1.5f), Quaternion.identity));
            var other = anchor.ToWorld(new(new(.4f, .07f, -1.5f), Quaternion.identity));
            check(Math.Abs(feet.Position.y - other.Position.y) < .00001f, "Lateral foot motion became vertical motion.");
        }
        // OpenXR is current; SteamVR HMD remains frozen. Across songs move
        // only the native world origin, never register the frozen head again.
        var native = new TrackingSpaceAnchor(new(0, 0, 0), Quaternion.identity);
        var steam = new TrackingSpaceAnchor(new(.7f, .02f, 1f), Quaternion.AngleAxis(25, new(0, 1, 0)));
        var tracker = new Pose(new(-.2f, .95f, -1.7f), Quaternion.identity);
        var calibrated = steam.ToWorld(tracker);
        var original = steam;
        for (var song = 0; song < 20; song++)
        {
            var current = new TrackingSpaceAnchor(new(song % 2, 0, song % 3), Quaternion.AngleAxis(song * 9, new(0, 1, 0)));
            steam = steam.Rebase(native, current);
            var expected = current.ToWorld(calibrated);
            var actual = steam.ToWorld(tracker);
            check(N.Distance(actual.Position.N, expected.Position.N) < .0001f, "Song recovery changed cross-runtime tracker registration.");
            check(Math.Abs(System.Numerics.Quaternion.Dot(actual.Rotation.N, expected.Rotation.N)) > .99999f,
                "Song recovery rotated the calibrated tracker offsets.");
            native = current;
        }
        steam = steam.Rebase(native, new(new(0, 0, 0), Quaternion.identity));
        check(N.Distance(steam.Translation.N, original.Translation.N) < .0001f, "Returning home accumulated tracker drift.");
    }
}
