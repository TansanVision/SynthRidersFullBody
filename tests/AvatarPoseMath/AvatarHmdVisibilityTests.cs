using Il2CppSynth.Avatars;
using SynthRidersFullBody;
using UnityEngine;

internal static class AvatarHmdVisibilityTests
{
    /// <summary>ゲーム設定の再読み込み・HMD交換・操作失敗を本番の表示制御へ入力します。</summary>
    public static void Run(Action<bool, string> check)
    {
        Resources.All.Clear();
        Time.unscaledTime = 0;
        var loader = new AvatarLoader();
        AvatarLoader.s_instance = loader;
        var config = new ModConfig();
        var visibility = new AvatarHmdVisibility();
        var key = AvatarHmdVisibility.AvatarKey(loader)!;
        config.AvatarVisibleInHeadset[key] = false;
        check(visibility.ApplySaved(config) && loader.specificSettings!.HideInFirtPersonView,
            "Saved hidden preference was not applied.");
        check(!visibility.ApplySaved(config) && loader.ToggleCount == 1,
            "Stable avatar was toggled a second time.");

        // The game reloads per-avatar data without replacing vrmObject.
        Time.unscaledTime += 2;
        loader.specificSettings = new();
        check(visibility.ApplySaved(config) && loader.specificSettings.HideInFirtPersonView,
            "Reloading settings on the same avatar lost the saved hidden preference.");
        var toggles = loader.ToggleCount;
        var views = loader.ViewCount;
        check(!visibility.ApplySaved(config) && loader.ToggleCount == toggles && loader.ViewCount == views,
            "Stable frames repeatedly invoked native visibility methods.");

        Time.unscaledTime += 2;
        loader.m_HMDCamera = new Camera("Replacement HMD");
        check(visibility.ApplySaved(config) && loader.ToggleCount == toggles &&
            (loader.m_HMDCamera.cullingMask & AvatarLoader.AvatarLayerBit) == 0,
            "HMD replacement with an unchanged setting did not refresh the native view.");
        Time.unscaledTime += 2;
        loader.m_HMDCamera.cullingMask |= AvatarLoader.AvatarLayerBit;
        check(visibility.ApplySaved(config) && loader.ToggleCount == toggles &&
            (loader.m_HMDCamera.cullingMask & AvatarLoader.AvatarLayerBit) == 0,
            "Camera mask reset was ignored because the saved boolean was already correct.");

        loader.avatarIsLoading = true;
        loader.specificSettings = new();
        Time.unscaledTime += 2;
        check(!visibility.TryGet(out _) && !visibility.TryToggle(config) && !visibility.ApplySaved(config) &&
            loader.ToggleCount == toggles && !config.AvatarVisibleInHeadset[key],
            "An unfinished load accepted a toggle or overwrote the saved preference.");
        loader.avatarIsLoading = false;
        loader.hasAvatarLoaded = false;
        check(!visibility.TryGet(out _) && !visibility.ApplySaved(config),
            "The gap between loading and hasAvatarLoaded accepted visibility changes.");
        loader.hasAvatarLoaded = true;
        check(visibility.ApplySaved(config) && visibility.TryGet(out var visible) && !visible,
            "Finishing a same-instance load did not restore hidden visibility.");

        check(visibility.TryToggle(config) && config.AvatarVisibleInHeadset[key] &&
            !loader.specificSettings.HideInFirtPersonView &&
            (loader.m_HMDCamera.cullingMask & AvatarLoader.AvatarLayerBit) != 0,
            "Explicit ON did not persist and refresh the headset view.");
        var capture = new Camera("Capture");
        var captureMask = capture.cullingMask;
        check(visibility.TryToggle(config) && !config.AvatarVisibleInHeadset[key] &&
            loader.vrmObject!.activeInHierarchy && capture.cullingMask == captureMask,
            "Hiding the headset avatar disabled the whole avatar or changed the capture camera.");

        // An API no-op must not be saved as a successful user change.
        loader.IgnoreToggle = true;
        check(!visibility.TryToggle(config) && !config.AvatarVisibleInHeadset[key],
            "A failed native toggle was reported and saved as successful.");
        loader.IgnoreToggle = false;
        loader.ThrowOnView = true;
        Time.unscaledTime += 2;
        loader.m_HMDCamera.cullingMask |= AvatarLoader.AvatarLayerBit;
        check(!visibility.ApplySaved(config), "A native view failure was reported as applied.");
        views = loader.ViewCount;
        check(!visibility.ApplySaved(config) && loader.ViewCount == views,
            "A failed native view was retried every frame.");
        loader.ThrowOnView = false;
        Time.unscaledTime += 2;
        check(visibility.ApplySaved(config) &&
            (loader.m_HMDCamera.cullingMask & AvatarLoader.AvatarLayerBit) == 0,
            "A temporary native view failure never recovered.");

        // A ready avatar with correct flags still needs its new camera initialized.
        var another = new AvatarLoader();
        another.specificSettings!.HideInFirtPersonView = true;
        AvatarLoader.s_instance = another;
        check(visibility.ApplySaved(config) && another.ToggleCount == 0 && another.ViewCount == 1,
            "Correct flags on a recreated avatar skipped camera application.");
        check(visibility.GetActiveAvatarRoot() == another.vrmObject,
            "Active avatar lookup retained the previous loader.");

        // Current loader is authoritative even while it has no avatar yet.
        var waiting = new AvatarLoader { vrmObject = null, hasAvatarLoaded = false };
        AvatarLoader.s_instance = waiting;
        toggles = another.ToggleCount;
        check(visibility.GetActiveAvatarRoot() == null && !visibility.TryGet(out _) &&
            !visibility.ApplySaved(config) && another.ToggleCount == toggles,
            "A loading current singleton fell back to the previous scene's avatar.");

        waiting.vrmObject = new GameObject("New Avatar");
        waiting.activeAvatar = new AvatarItem { avatarPath = "C:/Avatars/other.vrm" };
        waiting.hasAvatarLoaded = true;
        check(!visibility.ApplySaved(config) && waiting.ToggleCount == 0 && waiting.ViewCount == 0,
            "An avatar with no saved preference inherited another avatar's setting.");
        check(visibility.TryToggle(config) && config.AvatarVisibleInHeadset.Count == 2 &&
            !config.AvatarVisibleInHeadset[AvatarHmdVisibility.AvatarKey(waiting)!],
            "The second avatar's preference was not stored independently.");
        waiting.activeAvatar = new AvatarItem { avatarPath = "  C:\\Avatars\\Example.VRM  " };
        check(AvatarHmdVisibility.AvatarKey(waiting) == key,
            "Path normalization changed the existing preference key format.");
        waiting.activeAvatar = new AvatarItem { avatarIndex = 7, avatarName = "Internal" };
        check(AvatarHmdVisibility.AvatarKey(waiting) == "internal:7:Internal",
            "Internal-avatar preference keys changed format.");

        AvatarLoader.s_instance = null;
        Resources.All.Clear();
        visibility.RequestRefresh();
        var prefab = new AvatarLoader();
        prefab.gameObject.scene.Valid = false;
        check(visibility.GetActiveAvatarRoot() == null && !visibility.TryToggle(config),
            "Resource lookup selected an avatar loader prefab.");
        prefab.gameObject.scene.Valid = true;
        prefab.vrmObject!.scene.Valid = false;
        visibility.RequestRefresh();
        check(visibility.GetActiveAvatarRoot() == null,
            "Resource lookup selected an avatar prefab.");
        prefab.vrmObject.scene.Valid = true;
        visibility.RequestRefresh();
        check(visibility.GetActiveAvatarRoot() == prefab.vrmObject,
            "Scene fallback failed when no singleton exists.");
        Resources.All.Clear();
        AvatarLoader.s_instance = null;
    }
}

// These doubles simulate native flag/camera changes and failures; they cannot
// prove the game's IL2CPP implementation or what either eye actually renders.
namespace Il2CppSynth.Avatars
{
    public sealed class AvatarItem
    {
        public string? avatarPath;
        public int avatarIndex;
        public string? avatarName;
    }
    public sealed class AvatarSettings { public bool HideInFirtPersonView; }
    public sealed class AvatarLoader : Component
    {
        public const int AvatarLayerBit = 1 << 9;
        public static AvatarLoader? s_instance;
        public static object? CurrentCamType => null;
        public GameObject? vrmObject = new("Avatar");
        public AvatarItem? activeAvatar = new() { avatarPath = "C:/Avatars/Example.vrm" };
        public AvatarSettings? specificSettings = new();
        public Camera? m_HMDCamera = new("HMD");
        public bool avatarIsLoading;
        public bool hasAvatarLoaded = true;
        public bool IgnoreToggle;
        public bool ThrowOnView;
        public int ToggleCount;
        public int ViewCount;
        public void ToggleAvatarsFirstPersonModel()
        {
            ToggleCount++;
            if (!IgnoreToggle) specificSettings!.HideInFirtPersonView = !specificSettings.HideInFirtPersonView;
        }
        public void SetFirstPersonCameraView()
        {
            ViewCount++;
            if (ThrowOnView) throw new InvalidOperationException("Native view unavailable");
            if (specificSettings!.HideInFirtPersonView) m_HMDCamera!.cullingMask &= ~AvatarLayerBit;
            else m_HMDCamera!.cullingMask |= AvatarLayerBit;
        }
    }
}
