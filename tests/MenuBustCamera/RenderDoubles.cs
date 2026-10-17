// Test doubles for callback delivery and mutable camera state only. They do not render Unity images.
namespace UnityEngine
{
    public readonly record struct Vector3(float x, float y, float z)
    {
        internal System.Numerics.Vector3 N => new(x, y, z);
        internal static Vector3 U(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
        public float sqrMagnitude => N.LengthSquared();
        public static Vector3 up => new(0, 1, 0);
        public static Vector3 operator -(Vector3 a, Vector3 b) => U(a.N - b.N);
        public static float Distance(Vector3 a, Vector3 b) => System.Numerics.Vector3.Distance(a.N, b.N);
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => U(System.Numerics.Vector3.Lerp(a.N, b.N, t));
    }
    public readonly record struct Quaternion(System.Numerics.Quaternion Value)
    {
        public static Quaternion LookRotation(Vector3 forward, Vector3 up)
        {
            var z = System.Numerics.Vector3.Normalize(forward.N);
            var x = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(up.N, z));
            var y = System.Numerics.Vector3.Cross(z, x);
            return new(System.Numerics.Quaternion.CreateFromRotationMatrix(new(
                x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1)));
        }
    }
    public sealed class Transform
    {
        public Vector3 localPosition, lossyScale = new(1, 1, 1);
        public Quaternion localRotation = new(System.Numerics.Quaternion.Identity);
        public Vector3 position => localPosition;
        public Vector3 forward => new(0, 0, 1);
        public bool ThrowOnSet;
        public void SetPositionAndRotation(Vector3 p, Quaternion q)
        {
            localPosition = p;
            if (ThrowOnSet) throw new InvalidOperationException("Injected write failure");
            localRotation = q;
        }
    }
    public sealed class GameObject { public bool activeInHierarchy = true; }
    public sealed class Camera
    {
        public Transform transform = new();
        public GameObject gameObject = new();
        public bool stereoEnabled, orthographic;
        public float aspect = 16f / 9f, nearClipPlane = .1f, farClipPlane = 1000f, fieldOfView = 60f;
        public static CameraCallback? onPreCull, onPostRender;
        public sealed class CameraCallback
        {
            private System.Action<Camera>? _action;
            public static implicit operator CameraCallback(System.Action<Camera> action) => new() { _action = action };
            public static CameraCallback operator +(CameraCallback? a, CameraCallback b) => new() { _action = a?._action + b._action };
            public static CameraCallback operator -(CameraCallback? a, CameraCallback b) => new() { _action = a?._action - b._action };
            public void Invoke(Camera camera) => _action?.Invoke(camera);
        }
    }
    public enum HumanBodyBones { Head, Hips, UpperChest, Chest, LeftUpperArm, RightUpperArm }
    public sealed class Animator
    {
        public GameObject gameObject = new();
        public Transform transform = new();
        public Dictionary<HumanBodyBones, Transform> Bones = new();
        public Transform? GetBoneTransform(HumanBodyBones bone) => Bones.GetValueOrDefault(bone);
    }
    public static class Time { public static float unscaledTime = 1f; }
    public static class Mathf { public static float Abs(float value) => Math.Abs(value); }
}
namespace Il2CppSystem
{
    public sealed class Action<T1, T2>
    {
        private System.Action<T1, T2>? _action;
        public static implicit operator Action<T1, T2>(System.Action<T1, T2> action) => new() { _action = action };
        public static Action<T1, T2> operator +(Action<T1, T2>? a, Action<T1, T2> b) => new() { _action = a?._action + b._action };
        public static Action<T1, T2> operator -(Action<T1, T2>? a, Action<T1, T2> b) => new() { _action = a?._action - b._action };
        public void Invoke(T1 a, T2 b) => _action?.Invoke(a, b);
    }
}
namespace UnityEngine.Rendering
{
    public readonly struct ScriptableRenderContext { }
    public static class RenderPipelineManager
    {
        public static Il2CppSystem.Action<ScriptableRenderContext, UnityEngine.Camera>? beginCameraRendering, endCameraRendering;
    }
}
namespace SynthRidersFullBody
{
    internal static class MenuCameraContext { public static bool Open = true; public static bool IsOpen() => Open; }
    internal static class CaptureCameraResolver
    {
        public static UnityEngine.Camera? Selected;
        public static string Identity(UnityEngine.Camera? camera) => camera == null ? "none" : camera.GetHashCode().ToString();
        public static string CurrentMode() => "ThirdPerson";
        public static UnityEngine.Camera? Find(UnityEngine.Camera headset, out string status) { status = ""; return Selected; }
        public static bool IsStillSelected(UnityEngine.Camera camera, string? mode) => camera == Selected;
    }
}
