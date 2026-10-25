using System.Numerics;

namespace SynthRidersFullBody;

/// <summary>立位追跡空間を床に水平に保ち、異なるランタイムの頭の傾きを床の傾きへ混ぜません。</summary>
internal static class LevelTrackingSpace
{
    /// <summary>HMDの水平方向だけを合わせます。真上・真下を向いた入力では右方向を使います。</summary>
    public static Quaternion Rotation(Quaternion rawHead, Quaternion worldHead)
    {
        var raw = Vector3.Transform(Vector3.UnitZ, rawHead);
        var world = Vector3.Transform(Vector3.UnitZ, worldHead);
        if (raw.X * raw.X + raw.Z * raw.Z < .01f || world.X * world.X + world.Z * world.Z < .01f)
        {
            raw = Vector3.Transform(Vector3.UnitX, rawHead);
            world = Vector3.Transform(Vector3.UnitX, worldHead);
        }
        return Quaternion.CreateFromAxisAngle(Vector3.UnitY,
            MathF.Atan2(world.X, world.Z) - MathF.Atan2(raw.X, raw.Z));
    }
}
