using System.Numerics;

namespace SynthRidersFullBody;

/// <summary>モデル固有の指先軸・手のひら法線を、指定された標準姿勢へ対応付けます。</summary>
internal static class PalmPoseMath
{
    /// <summary>手首から人差し指・小指の付け根へのベクトルで掌面を求めます。左右の鏡像を区別します。</summary>
    public static bool TryFingerReference(bool left, Vector3 index, Vector3 little, Vector3? middle,
        Vector3 fingers, Vector3 palm, out Quaternion rotation) =>
        TryRotation(middle ?? (index + little) * .5f, Vector3.Cross(index, little) * (left ? 1f : -1f),
            fingers, palm, out rotation);

    /// <summary>骨のローカル軸に依存せず、指先方向と手のひら方向から手ボーンの回転を求めます。</summary>
    public static bool TryRotation(Vector3 localFingers, Vector3 localPalm, Vector3 fingers, Vector3 palm,
        out Quaternion rotation)
    {
        rotation = Quaternion.Identity;
        if (!TryFrame(localFingers, localPalm, out var local) || !TryFrame(fingers, palm, out var world)) return false;
        rotation = Quaternion.Normalize(world * Quaternion.Inverse(local));
        return true;
    }

    /// <summary>指先と掌面から直交基底を作り、退化した指ボーン構成を拒否します。</summary>
    private static bool TryFrame(Vector3 forward, Vector3 up, out Quaternion rotation)
    {
        rotation = Quaternion.Identity;
        if (!Finite(forward) || !Finite(up) || forward.LengthSquared() < 1e-12f || up.LengthSquared() < 1e-16f) return false;
        forward = Vector3.Normalize(forward);
        up = Vector3.Normalize(up);
        up -= forward * Vector3.Dot(up, forward);
        if (up.LengthSquared() < .000001f) return false;
        up = Vector3.Normalize(up);
        var right = Vector3.Normalize(Vector3.Cross(up, forward));
        rotation = Quaternion.CreateFromRotationMatrix(new Matrix4x4(
            right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0,
            forward.X, forward.Y, forward.Z, 0, 0, 0, 0, 1));
        return true;
    }

    /// <summary>各成分が有限であることを確認します。</summary>
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
