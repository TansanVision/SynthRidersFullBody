using SynthRidersFullBody;
using UnityEngine;
using UnityEngine.Rendering;

internal static class CameraGuideDrawTests
{
    /// <summary>ログの描画マスク・全使用レイヤー・同名の別カメラでも、HMDだけへ描画することを検証します。</summary>
    public static void Run(Action<bool, string> check)
    {
        var head = new Camera("Cam") { stereoEnabled = true };
        var capture = new Camera("Cam");
        var mesh = new Mesh();
        var material = new Material();
        foreach (var mask in Enumerable.Range(0, 32).Select(i => 1 << i)
                     .Concat(new[] { -1, 0x7FFF9B37 }))
        {
            head.cullingMask = mask;
            var originalCapture = capture.cullingMask;
            Graphics.Calls.Clear();
            check(CameraGuideDraw.Submit(head, capture, mesh, material, default), "Visible HMD layer was rejected.");
            var draw = Graphics.Calls.Single();
            check(draw.Camera == head && draw.Camera != capture && draw.Mesh == mesh,
                "Marker draw was submitted globally or to the capture camera.");
            check((mask & (1 << draw.Layer)) != 0, "Marker uses a layer excluded by the HMD.");
            check(head.cullingMask == mask && capture.cullingMask == originalCapture,
                "Marker changed an existing camera's culling mask.");
            check(draw.Shadows == ShadowCastingMode.Off && !draw.ReceiveShadows && draw.Probes == LightProbeUsage.Off,
                "Marker can cast a shadow into capture output.");
        }
        Graphics.Calls.Clear();
        check(!CameraGuideDraw.Submit(null, capture, mesh, material, default), "Null camera enabled global drawing.");
        check(!CameraGuideDraw.Submit(capture, capture, mesh, material, default), "Capture camera accepted as HMD.");
        head.cullingMask = 0;
        check(!CameraGuideDraw.Submit(head, capture, mesh, material, default), "Zero camera mask was overwritten.");
        head.cullingMask = -1; head.enabled = false;
        check(!CameraGuideDraw.Submit(head, capture, mesh, material, default), "Disabled HMD accumulated draw submissions.");
        head.enabled = true; head.gameObject.activeInHierarchy = false;
        check(!CameraGuideDraw.Submit(head, capture, mesh, material, default), "Inactive scene camera received a marker.");
        head.gameObject.activeInHierarchy = true; head.gameObject.scene.Valid = false;
        check(!CameraGuideDraw.Submit(head, capture, mesh, material, default), "Prefab camera received a marker.");
        head.gameObject.scene.Valid = true;
        check(!CameraGuideDraw.Submit(head, capture, null, material, default), "Destroyed mesh was submitted.");
        check(!CameraGuideDraw.Submit(head, capture, mesh, null, default), "Destroyed material was submitted.");
        check(Graphics.Calls.Count == 0, "Rejected draw still reached the rendering API.");
        check(CameraGuideDraw.Submit(head, capture, mesh, material, default), "Valid input did not recover after rejection.");
    }
}

// API call recorder only: real Unity/URP/XR rendering still needs a headset test.
namespace UnityEngine
{
    public sealed class Mesh { }
    public sealed class Material { }
    public readonly struct Matrix4x4 { }
    public sealed class MaterialPropertyBlock { }
    public sealed class LightProbeProxyVolume { }
    public static class Graphics
    {
        public sealed record Draw(Mesh Mesh, Camera Camera, int Layer, ShadowCastingMode Shadows,
            bool ReceiveShadows, LightProbeUsage Probes);
        public static readonly List<Draw> Calls = new();
        public static void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int layer, Camera camera,
            int submesh, MaterialPropertyBlock? properties, ShadowCastingMode shadows, bool receiveShadows,
            Transform? anchor, LightProbeUsage probes, LightProbeProxyVolume? volume) =>
            Calls.Add(new(mesh, camera, layer, shadows, receiveShadows, probes));
    }
}
namespace UnityEngine.Rendering
{
    public enum ShadowCastingMode { Off, On }
    public enum LightProbeUsage { Off, BlendProbes }
}
