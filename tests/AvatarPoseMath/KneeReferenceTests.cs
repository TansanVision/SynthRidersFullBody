using System.Numerics;
using SynthRidersFullBody;

/// <summary>モデルの大きさ・膝の張り出し・向きが基準膝ガイドを反転させないことを検証します。</summary>
internal static class KneeReferenceTests
{
    /// <summary>横・後方へ張り出した直立骨格と、正常な軽い前曲げを比較します。</summary>
    public static void Run(Action<bool, string> check)
    {
        foreach (var scale in new[] { 0.02f, 0.3f, 1f, 2.376f, 100f })
        foreach (var yaw in new[] { 0f, 90f, 180f, 270f })
        foreach (var offset in new[] { new Vector3(.03f, 0, .001f), new Vector3(-.03f, 0, .001f),
            new Vector3(0, 0, -.04f), new Vector3(.001f, 0, 0), new Vector3(.01f, 0, .08f) })
        {
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw * MathF.PI / 180f);
            var hip = new Vector3(0, .8f, 0);
            var knee = new Vector3(0, .4f, 0) + offset;
            var foot = Vector3.Zero;
            var reference = AvatarPoseMath.CalibrationKneeBend(hip, knee, foot, Vector3.UnitZ);
            var origin = new Vector3(3, -1, 2);
            Vector3 Map(Vector3 p) => origin + Vector3.Transform(p * scale, rotation);
            var mapped = AvatarPoseMath.CalibrationKneeBend(Map(hip), Map(knee), Map(foot),
                Vector3.Transform(Vector3.UnitZ, rotation));
            check(Vector3.Distance(mapped, Vector3.Transform(reference, rotation)) < .0001f,
                "Same skeleton selected a different knee guide after scaling or turning.");
            if (offset.Z < .01f)
                check(Vector3.Dot(reference, Vector3.UnitZ) > .9999f, "Sideways/reversed rest geometry became a knee bend direction.");
            else
                check(Vector3.Distance(reference, Vector3.Normalize(offset)) < .0001f, "Normal forward bend was discarded.");
            foreach (var angle in new[] { 0f, 45f, 90f, 130f })
            {
                var lift = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -angle * MathF.PI / 180f);
                var axis = Vector3.Transform(-Vector3.UnitY, lift);
                var bent = AvatarPoseMath.LegBend(-Vector3.UnitY, reference, axis);
                check(Vector3.Dot(bent, Vector3.Transform(Vector3.UnitZ, lift)) > .98f,
                    "Rest-pose side offset drove the knee sideways during a forward kick.");
            }
        }
        foreach (var offset in new[] { Vector3.Zero, new Vector3(float.NaN, 0, 0), new Vector3(1, -.5f, .6f) })
        {
            var direction = AvatarPoseMath.CalibrationKneeBend(Vector3.Zero, offset, -Vector3.UnitY, Vector3.UnitZ);
            check(float.IsFinite(direction.X) && Math.Abs(direction.Length() - 1f) < .0001f &&
                Vector3.Dot(direction, Vector3.UnitZ) >= MathF.Cos(35 * MathF.PI / 180f) - .0001f,
                "Degenerate or strongly sideways rest pose produced an invalid knee reference.");
        }
    }
}
