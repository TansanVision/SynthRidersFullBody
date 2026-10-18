// Transform math and connection lifetime only. These doubles do not run the native VRIK solver.
namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static void DontDestroyOnLoad(Object obj) { }
        public static void Destroy(Object obj)
        {
            obj.Destroyed = true;
            if (obj is GameObject go) go.transform.Destroyed = true;
        }
        public static bool operator ==(Object? a, Object? b) =>
            ReferenceEquals(a, b) || ((a is null || a.Destroyed) && (b is null || b.Destroyed));
        public static bool operator !=(Object? a, Object? b) => !(a == b);
        public override bool Equals(object? obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public sealed class GameObject : Object
    {
        public Transform transform;
        public GameObject(string name) { transform = new Transform(this); }
    }
    public sealed class Transform : Object
    {
        public readonly GameObject gameObject;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public Transform(GameObject go) { gameObject = go; }
        public void SetPositionAndRotation(Vector3 p, Quaternion q) { position = p; rotation = q; }
    }
    public readonly record struct Vector3(float x, float y, float z)
    {
        internal System.Numerics.Vector3 N => new(x, y, z);
        internal static Vector3 U(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => U(System.Numerics.Vector3.Lerp(a.N, b.N, t));
    }
    public readonly record struct Quaternion(float x, float y, float z, float w)
    {
        internal System.Numerics.Quaternion N => new(x, y, z, w);
        internal static Quaternion U(System.Numerics.Quaternion q) => new(q.X, q.Y, q.Z, q.W);
        public static Quaternion identity => new(0, 0, 0, 1);
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) => U(System.Numerics.Quaternion.Slerp(a.N, b.N, t));
    }
    public static class Mathf { public static float Clamp01(float value) => Math.Clamp(value, 0, 1); }
}
namespace SynthRidersFullBody
{
    internal readonly record struct Pose(UnityEngine.Vector3 Position, UnityEngine.Quaternion Rotation)
    {
        public bool IsFinite => float.IsFinite(Position.x) && float.IsFinite(Position.y) && float.IsFinite(Position.z) &&
            float.IsFinite(Rotation.x) && float.IsFinite(Rotation.y) && float.IsFinite(Rotation.z) && float.IsFinite(Rotation.w) &&
            Rotation.N.LengthSquared() is > .25f and < 4f;
    }
    internal enum TrackerRole { Head }
    internal readonly record struct TrackingSpaceAnchor(UnityEngine.Vector3 Translation, UnityEngine.Quaternion Rotation);
}
