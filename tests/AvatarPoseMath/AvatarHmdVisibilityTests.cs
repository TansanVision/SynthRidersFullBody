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
        using var visibility = new AvatarHmdVisibility();
        visibility.Initialize();
        var accessoryObject = new GameObject("Backpack") { layer = 0 };
        accessoryObject.transform.parent = loader.vrmObject!.transform;
        var accessory = new MeshRenderer(accessoryObject);
        var key = AvatarHmdVisibility.AvatarKey(loader)!;
        config.AvatarVisibleInHeadset[key] = false;
        check(visibility.ApplySaved(config) && loader.specificSettings!.HideInFirtPersonView &&
            loader.infoProvider!.HideInFirtPersonView && loader.LayerCount == 1,
            "Saved hidden preference was not applied.");
        check(!visibility.ApplySaved(config) && loader.ToggleCount == 0,
            "Visibility still relied on a menu toggle with an unspecified setting target.");
        Camera.onPreCull?.Invoke(loader.m_HMDCamera!);
        check(accessory.forceRenderingOff, "A default-layer accessory bypassed saved HMD hiding.");
        Camera.onPostRender?.Invoke(loader.m_HMDCamera!);
        check(!accessory.forceRenderingOff, "Saved HMD hiding leaked outside a render.");

        // The common view can change independently from per-avatar calibration/settings.
        loader.infoProvider!.HideInFirtPersonView = false;
        check(visibility.TryGet(out var actualVisible) && actualVisible,
            "UI read stale per-avatar data instead of the common view setting.");
        Time.unscaledTime += 2;
        check(visibility.ApplySaved(config) && loader.infoProvider.HideInFirtPersonView,
            "An independent common-view reset was not repaired.");

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
        Camera.onPreCull?.Invoke(loader.m_HMDCamera!);
        check(!accessory.forceRenderingOff, "ON left accessory suppression active.");
        Camera.onPostRender?.Invoke(loader.m_HMDCamera!);
        var capture = new Camera("Capture");
        var captureMask = capture.cullingMask;
        check(visibility.TryToggle(config) && !config.AvatarVisibleInHeadset[key] &&
            loader.vrmObject!.activeInHierarchy && capture.cullingMask == captureMask,
            "Hiding the headset avatar disabled the whole avatar or changed the capture camera.");

        // An API no-op must not be saved as a successful user change.
        loader.infoProvider.IgnoreVisibilityWrites = true;
        check(!visibility.TryToggle(config) && !config.AvatarVisibleInHeadset[key] &&
            loader.specificSettings.HideInFirtPersonView && visibility.TryGet(out visible) && !visible,
            "Rejected common-view write was saved or left per-avatar state flipped.");
        loader.infoProvider.IgnoreVisibilityWrites = false;
        loader.ThrowOnView = true;
        var oldMask = loader.m_HMDCamera.cullingMask;
        check(!visibility.TryToggle(config) && !config.AvatarVisibleInHeadset[key] &&
            loader.specificSettings.HideInFirtPersonView && loader.infoProvider.HideInFirtPersonView &&
            loader.m_HMDCamera.cullingMask == oldMask,
            "Partial native view failure left a failed click applied to flags or mask.");
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
        Camera.onPreCull?.Invoke(loader.m_HMDCamera!);
        check(accessory.forceRenderingOff, "A failed toggle prevented subsequent HMD accessory suppression.");
        visibility.RequestRefresh();
        check(!accessory.forceRenderingOff, "Scene refresh retained temporary hidden renderers.");
        visibility.ApplySaved(config);

        loader.ThrowOnLayers = true;
        check(!visibility.TryToggle(config) && !config.AvatarVisibleInHeadset[key] &&
            loader.infoProvider.HideInFirtPersonView && loader.specificSettings.HideInFirtPersonView,
            "Layer initialization failure was treated as a successful visibility change.");
        loader.ThrowOnLayers = false;
        var provider = loader.infoProvider;
        loader.infoProvider = null;
        check(!visibility.TryGet(out _) && !visibility.TryToggle(config),
            "An uninitialized view provider accepted a visibility change.");
        loader.infoProvider = provider;

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
        public Il2Cpp.Game_InfoProvider? infoProvider = new();
        public Camera? m_HMDCamera = new("HMD");
        public bool avatarIsLoading;
        public bool hasAvatarLoaded = true;
        public bool ThrowOnView;
        public bool ThrowOnLayers;
        public int ToggleCount;
        public int LayerCount;
        public int ViewCount;
        public void ToggleAvatarsFirstPersonModel()
        {
            ToggleCount++;
            // Deliberately separate data stores: the earlier fake updated only
            // specificSettings and could not reproduce this reported failure.
            infoProvider!.HideInFirtPersonView = !infoProvider.HideInFirtPersonView;
        }
        public void SetFirstPersonCameraLayers()
        {
            LayerCount++;
            if (ThrowOnLayers) throw new InvalidOperationException("Native layers unavailable");
            if (infoProvider!.HideInFirtPersonView) m_HMDCamera!.cullingMask &= ~AvatarLayerBit;
            else m_HMDCamera!.cullingMask |= AvatarLayerBit;
        }
        public void SetFirstPersonCameraView()
        {
            ViewCount++;
            if (ThrowOnView) throw new InvalidOperationException("Native view unavailable");
        }
    }
}
