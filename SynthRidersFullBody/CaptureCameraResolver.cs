using Il2Cpp;
using Il2Cppcom.Kluge.XR.Utils;
using UnityEngine;

namespace SynthRidersFullBody;

/// <summary>
/// ゲームが選択した配信用カメラを、バージョン差のあるメンバーを介して探索します。
/// </summary>
internal static class CaptureCameraResolver
{
    private static readonly string[] ThirdPersonObjects =
    {
        "ThirdPerson_CameraObject", "ThirdPersonSwing_CameraObject",
        "ThirdPersonCustom_CameraObject", "Spectator_CameraObject", "Cinematic_CameraObject"
    };

    /// <summary>
    /// 現在のカメラ管理を優先し、画面出力先または旧版の三人称カメラ参照を探します。
    /// </summary>
    /// <param name="headset">HMD用として識別したカメラ。</param>
    /// <param name="status">選択結果、またはガイド対象がない理由を受け取ります。</param>
    /// <returns>表示対象のカメラ。一人称・OFF・LIV・未検出の場合はnull。</returns>
    /// <remarks>Camera.enabledがfalseでも手動描画されるカメラを候補に残します。欠けた任意アクセサーは例外を吸収して旧版の取得経路へ進みます。</remarks>
    public static Camera? Find(Camera headset, out string status)
    {
        status = "No active capture camera; enable a third-person view in the game";
        var currentOwner = OptionalMember.ReadStatic(typeof(MainCameraEnabler), "s_instance") as MainCameraEnabler;
        foreach (var owner in Resources.FindObjectsOfTypeAll<MainCameraEnabler>()
                     .OrderBy(o => o == currentOwner ? 0 : 1))
        {
            if (owner == null || !owner.gameObject.scene.IsValid()) continue;
            // This is the actual screen output selected by the game, including
            // its standard/swing/spectator/cinematic modes. It may be disabled
            // because ManualCameraRenderer calls Render() explicitly.
            var selected = CameraOf(OptionalMember.Read(owner, "currentScreenCamera"));
            if (selected != null && selected.gameObject.activeInHierarchy)
            {
                if (Contains(OptionalMember.Read(owner, "OFF_CameraObject"), selected.transform))
                {
                    status = "Game screen camera is OFF";
                    return null;
                }
                if (IsLiv(selected)) { status = "LIV supplies its own camera guide"; return null; }
                if (selected == headset || selected.stereoEnabled ||
                    Contains(OptionalMember.Read(owner, "FirstPerson_CameraObject"), selected.transform) ||
                    selected == CameraOf(OptionalMember.Read(owner, "FirstCameraObject")))
                {
                    status = "First-person view selected; switch to a third-person view";
                    return null;
                }
                if (Usable(selected, headset))
                {
                    status = $"Screen camera: {selected.name}";
                    return selected;
                }
            }
            if (ActiveObject(OptionalMember.Read(owner, "OFF_CameraObject")))
            {
                status = "Game screen camera is OFF";
                return null;
            }
            // Older game versions may not expose currentScreenCamera.
            foreach (var name in ThirdPersonObjects)
            {
                var candidate = CameraOf(OptionalMember.Read(owner, name));
                if (!Usable(candidate, headset)) continue;
                status = $"{name}: {candidate!.name}";
                return candidate;
            }
        }
        foreach (var settings in Resources.FindObjectsOfTypeAll<CustomCameraSettings>())
        {
            if (settings == null || !settings.gameObject.scene.IsValid()) continue;
            foreach (var member in new[] { "customCamera", "customCameraTransform" })
            {
                var camera = CameraOf(OptionalMember.Read(settings, member));
                if (!Usable(camera, headset)) continue;
                status = $"Custom camera: {camera!.name}";
                return camera;
            }
        }
        return null;
    }

    /// <summary>
    /// カメラの祖先名とゲームのLIVラッパー参照から、LIV管理下かを調べます。
    /// </summary>
    /// <param name="camera">LIVの管理下かどうかを調べるカメラ。</param>
    /// <returns>LIVのカメラとして識別できた場合はtrue。</returns>
    internal static bool IsLiv(Camera camera)
    {
        for (var node = camera.transform; node != null; node = node.parent)
        {
            var name = node.name;
            if (name.Equals("LIV", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("LIV ", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("LIV_", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("LIV.", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("[LIV]", StringComparison.OrdinalIgnoreCase)) return true;
        }
        foreach (var wrapper in Resources.FindObjectsOfTypeAll<LivCameraWrapper>())
            if (wrapper != null && Contains(OptionalMember.Read(wrapper, "CameraWrapper"), camera.transform)) return true;
        return false;
    }

    /// <summary>
    /// シーン内の有効な外部視点で、HMD・鏡・プレビュー・LIVではないかを確認します。
    /// </summary>
    /// <param name="camera">配信用ガイドの対象にできるか調べる候補カメラ。</param>
    /// <param name="headset">HMD用として識別したカメラ。</param>
    /// <returns>ガイド対象として使用できる場合はtrue。</returns>
    private static bool Usable(Camera? camera, Camera headset) => camera != null && camera != headset &&
        camera.gameObject.scene.IsValid() && camera.gameObject.activeInHierarchy &&
        !camera.stereoEnabled && camera.cameraType != CameraType.Reflection &&
        camera.cameraType != CameraType.Preview && !IsLiv(camera);

    /// <summary>
    /// Transform・GameObject・ComponentからTransformを取り出します。
    /// </summary>
    /// <param name="value">変換または検証する値。</param>
    /// <returns>取り出したTransform。対応しない値ならnull。</returns>
    private static Transform? TransformOf(object? value) => value is Transform t ? t :
        value is GameObject go ? go.transform : value is Component c ? c.transform : null;

    /// <summary>
    /// 参照先のTransformが有効なGameObjectに属するかを調べます。
    /// </summary>
    /// <param name="value">変換または検証する値。</param>
    /// <returns>Transformを取得でき、階層内で有効ならtrue。</returns>
    private static bool ActiveObject(object? value) => TransformOf(value) is { } t && t != null && t.gameObject.activeInHierarchy;

    /// <summary>
    /// 値から取得したTransformが、指定した子の自身または祖先かを調べます。
    /// </summary>
    /// <param name="value">変換または検証する値。</param>
    /// <param name="child">包含関係を調べる子Transform。</param>
    /// <returns>同一または祖先であればtrue。</returns>
    private static bool Contains(object? value, Transform child) =>
        TransformOf(value) is { } parent && parent != null && (child == parent || child.IsChildOf(parent));

    /// <summary>
    /// カメラ参照またはその親オブジェクトから、使用対象のカメラを取り出します。
    /// </summary>
    /// <param name="value">変換または検証する値。</param>
    /// <returns>直接参照されたカメラ、または有効な子カメラ。なければnull。</returns>
    private static Camera? CameraOf(object? value)
    {
        if (value is Camera camera && camera != null) return camera;
        var transform = TransformOf(value);
        if (transform == null) return null;
        // Ignore inactive prefab/preview children; keep disabled Camera
        // components on active objects, which can be rendered manually.
        foreach (var child in transform.GetComponentsInChildren<Camera>(true))
            if (child != null && child.gameObject.activeInHierarchy) return child;
        return null;
    }
}
