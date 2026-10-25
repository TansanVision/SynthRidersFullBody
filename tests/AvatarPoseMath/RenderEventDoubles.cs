namespace UnityEngine
{
    public partial class Camera
    {
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
    public class Renderer : Component
    {
        public bool enabled = true, ThrowOnHide, ThrowOnRestore;
        private bool _off;
        public bool forceRenderingOff
        {
            get => _off;
            set
            {
                if ((value && ThrowOnHide) || (!value && ThrowOnRestore))
                    throw new InvalidOperationException("Injected renderer failure");
                _off = value;
            }
        }
        public Renderer(GameObject go) : base(go) { }
    }
    public sealed class MeshRenderer : Renderer { public MeshRenderer(GameObject go) : base(go) { } }
    public sealed class SkinnedMeshRenderer : Renderer { public SkinnedMeshRenderer(GameObject go) : base(go) { } }
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
