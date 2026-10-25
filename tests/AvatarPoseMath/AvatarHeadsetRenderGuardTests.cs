using SynthRidersFullBody;
using UnityEngine;
using UnityEngine.Rendering;

internal static class AvatarHeadsetRenderGuardTests
{
    /// <summary>小物の通常レイヤー、入れ子の鏡、描画中断、交換、復元失敗を本番の描画処理へ入力します。</summary>
    public static void Run(Action<bool, string> check)
    {
        Time.unscaledTime = 0;
        var root = new GameObject("Avatar");
        var body = new SkinnedMeshRenderer(Child(root, "Body", 9));
        var bag = new MeshRenderer(Child(root, "Winged backpack", 0));
        var hidden = new MeshRenderer(Child(root, "Already hidden")) { forceRenderingOff = true };
        var disabled = new MeshRenderer(Child(root, "Disabled")) { enabled = false };
        var scene = new MeshRenderer(new GameObject("Stage"));
        var hmd = new Camera("HMD") { cullingMask = ~(1 << 9) };
        var capture = new Camera("Capture");
        var mirror = new Camera("Mirror") { stereoEnabled = true }; // Only the explicit HMD is hidden.
        using var guard = new AvatarHeadsetRenderGuard();
        guard.Initialize();
        guard.Initialize();
        guard.Update(root, hmd, true);
        var diagnostic = guard.TakeDiagnostic();
        check(diagnostic?.Contains("Winged backpack") == true && diagnostic.Contains("layer=0") &&
            diagnostic.Contains("HMD callback observed=False"), "Diagnostic omitted native mask leakage or hook readiness.");
        check(!body.forceRenderingOff && !bag.forceRenderingOff, "Update hid the avatar outside rendering.");
        Camera.onPreCull?.Invoke(hmd);
        check(body.forceRenderingOff && bag.forceRenderingOff, "HMD did not hide skinned body and ordinary mesh accessory.");
        check(hidden.forceRenderingOff && !disabled.forceRenderingOff && !scene.forceRenderingOff &&
            !disabled.enabled && root.activeInHierarchy && bag.gameObject.layer == 0 &&
            hmd.cullingMask == ~(1 << 9) && capture.cullingMask == -1,
            "Suppression changed disabled objects, scene, layers, masks or hierarchy.");
        check(guard.TakeDiagnostic()?.Contains("HMD callback observed=True") == true, "First actual HMD callback was not reported.");
        Camera.onPreCull?.Invoke(mirror);
        check(!bag.forceRenderingOff && !body.forceRenderingOff, "Nested mirror inherited HMD suppression.");
        Camera.onPreCull?.Invoke(capture);
        check(!bag.forceRenderingOff, "Nested capture inherited HMD suppression.");
        Camera.onPostRender?.Invoke(capture);
        check(!bag.forceRenderingOff, "Capture end hid the parent mirror.");
        Camera.onPostRender?.Invoke(mirror);
        check(bag.forceRenderingOff, "Returning from mirror failed to re-hide HMD.");
        Camera.onPostRender?.Invoke(hmd);
        check(!body.forceRenderingOff && !bag.forceRenderingOff && hidden.forceRenderingOff,
            "Render end failed to restore original renderer flags.");
        Camera.onPreCull?.Invoke(capture);
        check(!bag.forceRenderingOff, "Capture after HMD was hidden.");
        Camera.onPostRender?.Invoke(capture);

        // Both pipeline event pairs may fire; the first end must not expose the ongoing HMD.
        RenderPipelineManager.beginCameraRendering?.Invoke(default, hmd);
        Camera.onPreCull?.Invoke(hmd);
        Camera.onPostRender?.Invoke(hmd);
        check(bag.forceRenderingOff, "Duplicate pipeline events restored the HMD too early.");
        RenderPipelineManager.beginCameraRendering?.Invoke(default, capture);
        check(!bag.forceRenderingOff, "SRP nested capture was hidden.");
        RenderPipelineManager.endCameraRendering?.Invoke(default, capture);
        check(bag.forceRenderingOff, "SRP parent HMD was not resumed.");
        RenderPipelineManager.endCameraRendering?.Invoke(default, hmd);
        check(!bag.forceRenderingOff, "SRP did not restore renderers.");
        Camera.onPostRender?.Invoke(hmd);
        check(!bag.forceRenderingOff, "Unmatched end callback changed visibility.");

        Camera.onPreCull?.Invoke(hmd);
        guard.Recover(); // Aborted render.
        check(!bag.forceRenderingOff, "Aborted render was not recovered.");
        Camera.onPreCull?.Invoke(hmd);
        guard.Update(root, hmd, false);
        check(!bag.forceRenderingOff, "Turning visibility ON during an aborted render left the accessory hidden.");
        Camera.onPreCull?.Invoke(hmd);
        check(!bag.forceRenderingOff, "Disabled guard still hid accessories.");
        guard.Update(root, hmd, true);
        var late = new MeshRenderer(Child(root, "Added later"));
        Time.unscaledTime += 2;
        guard.Update(root, hmd, true);
        Camera.onPreCull?.Invoke(hmd);
        check(late.forceRenderingOff, "Late-added accessory was never discovered.");
        Camera.onPostRender?.Invoke(hmd);
        bag.transform.parent = null;
        Camera.onPreCull?.Invoke(hmd);
        check(!bag.forceRenderingOff, "Detached object was still hidden using an old cache.");
        Camera.onPostRender?.Invoke(hmd);
        bag.transform.parent = root.transform;
        var newHmd = new Camera("Replacement HMD");
        guard.Update(root, newHmd, true);
        Camera.onPreCull?.Invoke(hmd);
        check(!bag.forceRenderingOff, "Old HMD retained suppression.");
        Camera.onPostRender?.Invoke(hmd);
        Camera.onPreCull?.Invoke(newHmd);
        check(bag.forceRenderingOff, "Replacement HMD did not hide accessories.");
        guard.Update(new GameObject("New avatar"), newHmd, true);
        check(!bag.forceRenderingOff && hidden.forceRenderingOff, "Avatar change corrupted previous flags.");
        Camera.onPreCull?.Invoke(newHmd);
        check(!bag.forceRenderingOff, "Old avatar remained bound after replacement.");
        Camera.onPostRender?.Invoke(newHmd);

        guard.Update(root, hmd, true);
        bag.ThrowOnHide = true;
        Camera.onPreCull?.Invoke(hmd);
        check(guard.Error != null && !body.forceRenderingOff && !bag.forceRenderingOff,
            "Partial suppression failure escaped or failed to roll back.");
        bag.ThrowOnHide = false;
        Camera.onPreCull?.Invoke(hmd);
        check(bag.forceRenderingOff, "Transient renderer failure prevented recovery.");
        bag.ThrowOnRestore = true;
        Camera.onPostRender?.Invoke(hmd);
        check(!body.forceRenderingOff && guard.Error != null, "One restore failure blocked other renderers.");
        bag.ThrowOnRestore = false;
        guard.Recover();
        check(!bag.forceRenderingOff, "Failed restore was dropped instead of retried.");
        var otherCalls = 0;
        Camera.CameraCallback other = (Action<Camera>)(_ => otherCalls++);
        Camera.onPreCull += other;
        Camera.onPreCull?.Invoke(hmd);
        guard.Dispose();
        check(!bag.forceRenderingOff && hidden.forceRenderingOff, "Dispose failed to restore original visibility.");
        Camera.onPreCull?.Invoke(hmd);
        check(otherCalls == 2 && !bag.forceRenderingOff, "Dispose removed other listeners or left its own listener.");
        Camera.onPreCull -= other;
    }

    /// <summary>テスト用アバターの子オブジェクトを作成します。</summary>
    private static GameObject Child(GameObject root, string name, int layer = 0)
    {
        var child = new GameObject(name) { layer = layer };
        child.transform.parent = root.transform;
        return child;
    }
}
