using UnityEngine;
using UnityEngine.Rendering;

namespace SynthRidersFullBody;

/// <summary>目印のメッシュをHMDカメラだけへ提出します。ゲームのレイヤーや描画マスクは変更しません。</summary>
internal static class CameraGuideDraw
{
    /// <summary>HMDが既に表示しているレイヤーを1つ選びます。未使用レイヤーである必要はありません。</summary>
    public static int VisibleLayer(int mask)
    {
        for (var layer = 0; layer < 32; layer++)
            if ((mask & (1 << layer)) != 0) return layer;
        return -1;
    }

    /// <summary>有効なHMDへだけ、このフレームの描画を提出します。nullカメラによる全カメラ描画を防ぎます。</summary>
    /// <returns>描画を提出できた場合はtrue。影・ライトプローブは使用しません。</returns>
    public static bool Submit(Camera? headset, Camera? capture, Mesh? mesh, Material? material, Matrix4x4 matrix)
    {
        if (headset == null || headset == capture || !headset.enabled ||
            !headset.gameObject.activeInHierarchy || !headset.gameObject.scene.IsValid() ||
            mesh == null || material == null) return false;
        var layer = VisibleLayer(headset.cullingMask);
        if (layer < 0) return false;
        // This overload exists in the game's generated Unity API. RenderMesh
        // is stripped there. The explicit camera restricts visibility even if
        // every other camera also renders the selected (already used) layer.
        Graphics.DrawMesh(mesh, matrix, material, layer, headset, 0, null,
            ShadowCastingMode.Off, false, null, LightProbeUsage.Off, null);
        return true;
    }
}
