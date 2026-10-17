using Il2Cpp;
using Il2Cppcom.Kluge.XR.Utils;
using Il2CppSynth.Avatars;
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
        var mode = CurrentMode();
        if (mode == "OFF") { status = "Game screen camera is OFF"; return null; }
        if (mode is "FirstPerson" or "FirstPersonWide")
        {
            status = "First-person view selected; switch to a third-person view";
            return null;
        }
        var currentOwner = OptionalMember.ReadStatic(typeof(MainCameraEnabler), "s_instance") as MainCameraEnabler;
        var owners = currentOwner != null && currentOwner.gameObject.scene.IsValid() && currentOwner.gameObject.activeInHierarchy
            ? new[] { currentOwner }
            : Resources.FindObjectsOfTypeAll<MainCameraEnabler>()
                .Where(o => o != null && o.gameObject.scene.IsValid() && o.gameObject.activeInHierarchy).ToArray();
        var fallback = new List<Camera>();
        foreach (var owner in owners)
        {
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
                    var expected = ModeMember(mode);
                    var modeRoot = expected != null ? OptionalMember.Read(owner, expected) : null;
                    // During a mode switch the old camera may remain active.
                    // Prefer the requested mode when both references exist.
                    if (modeRoot == null || Contains(modeRoot, selected.transform))
                    {
                        status = $"{mode ?? InferMode(owner, selected)}: {Identity(selected)}";
                        return selected;
                    }
                }
            }
            if (mode == null && ActiveObject(OptionalMember.Read(owner, "OFF_CameraObject")))
            {
                status = "Game screen camera is OFF";
                return null;
            }
            // Older game versions may not expose currentScreenCamera.
            var modeMember = ModeMember(mode);
            foreach (var name in modeMember != null ? new[] { modeMember } : ThirdPersonObjects)
            {
                var candidate = CameraOf(OptionalMember.Read(owner, name));
                if (!Usable(candidate, headset)) continue;
                fallback.Add(candidate!);
            }
        }
        if (fallback.Count > 0) return UniqueCandidate(fallback, mode ?? "Third-person", out status);
        if (mode != null && mode != "ThirdPersonCustom")
        {
            status = $"Waiting for {mode} camera";
            return null;
        }
        foreach (var settings in Resources.FindObjectsOfTypeAll<CustomCameraSettings>())
        {
            if (settings == null || !settings.gameObject.scene.IsValid()) continue;
            foreach (var member in new[] { "customCamera", "customCameraTransform" })
            {
                var camera = CameraOf(OptionalMember.Read(settings, member));
                if (!Usable(camera, headset)) continue;
                fallback.Add(camera!);
            }
        }
        if (fallback.Count > 0) return UniqueCandidate(fallback, mode ?? "Custom", out status);
        return null;
    }

    /// <summary>現在選択されている画面出力モードを、任意アクセサー経由で読み取ります。</summary>
    internal static string? CurrentMode()
    {
        var value = OptionalMember.ReadStatic(typeof(Game_InfoProvider), "CurrentCamType") ??
            OptionalMember.ReadStatic(typeof(AvatarLoader), "CurrentCamType");
        var mode = value?.ToString();
        return mode is "FirstPerson" or "FirstPersonWide" or "ThirdPerson" or "ThirdPersonSwing" or
            "ThirdPersonCustom" or "Spectator" or "Cinematic" or "OFF" ? mode : null;
    }

    /// <summary>探索後から描画直前までに、ゲームの出力モード・出力カメラが切り替わっていないかを軽量に確認します。</summary>
    /// <param name="camera">直近の探索で採用したカメラ。</param>
    /// <param name="mode">探索時のカメラモード。旧版で取得不能ならnull。</param>
    /// <returns>取得できた現在の選択情報と矛盾しない場合はtrue。対象の適格性はFindで検証します。</returns>
    internal static bool IsStillSelected(Camera camera, string? mode)
    {
        if (CurrentMode() != mode || camera == null || !camera.gameObject.activeInHierarchy) return false;
        var owner = OptionalMember.ReadStatic(typeof(MainCameraEnabler), "s_instance") as MainCameraEnabler;
        if (owner == null) return true; // Older builds use the mode-root fallback in Find.
        var selected = CameraOf(OptionalMember.Read(owner, "currentScreenCamera"));
        return selected == null || selected == camera;
    }

    /// <summary>表示モードに対応するゲームのカメラルート名を返します。</summary>
    private static string? ModeMember(string? mode) => mode switch
    {
        "ThirdPerson" => "ThirdPerson_CameraObject",
        "ThirdPersonSwing" => "ThirdPersonSwing_CameraObject",
        "ThirdPersonCustom" => "ThirdPersonCustom_CameraObject",
        "Spectator" => "Spectator_CameraObject",
        "Cinematic" => "Cinematic_CameraObject",
        _ => null
    };

    /// <summary>モード取得ができない版では、出力カメラが属するルートから診断用の名前を得ます。</summary>
    private static string InferMode(MainCameraEnabler owner, Camera camera)
    {
        foreach (var member in ThirdPersonObjects)
            if (Contains(OptionalMember.Read(owner, member), camera.transform))
                return member.Replace("_CameraObject", "");
        return "Screen camera";
    }

    /// <summary>重複参照をまとめ、一意な候補だけを採用します。複数の有効候補から順番で推測しません。</summary>
    private static Camera? UniqueCandidate(List<Camera> cameras, string mode, out string status)
    {
        var candidates = cameras.DistinctBy(c => c.GetInstanceID()).ToArray();
        if (candidates.Length == 1)
        {
            status = $"{mode}: {Identity(candidates[0])}";
            return candidates[0];
        }
        status = $"Waiting for selected camera ({mode}; {candidates.Length} candidates)";
        return null;
    }

    /// <summary>同名でも切り替えを識別できる、カメラ名と実行中の個体IDを返します。</summary>
    internal static string Identity(Camera? camera) => camera != null ? $"{camera.name} #{camera.GetInstanceID()}" : "none";

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
        Camera? candidate = null;
        foreach (var child in transform.GetComponentsInChildren<Camera>(true))
        {
            if (child == null || !child.gameObject.activeInHierarchy) continue;
            if (candidate != null && candidate != child) return null;
            candidate = child;
        }
        return candidate;
    }
}
