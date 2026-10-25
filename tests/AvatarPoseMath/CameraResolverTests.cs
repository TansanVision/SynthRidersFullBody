using SynthRidersFullBody;
using UnityEngine;

// A small scene double exercises the production resolver/optional accessors.
// It cannot verify IL2CPP or rendering; those remain hardware checks.
internal static class CameraResolverTests
{
    public static void Run(Action<bool, string> check)
    {
        Resources.All.Clear();
        var head = new Camera("Head") { stereoEnabled = true };
        var third = new Camera("Third") { enabled = false }; // manually rendered
        var unusedCustom = new Camera("Custom");
        _ = new Custom { customCamera = unusedCustom };
        var owner = new Current { currentScreenCamera = third };
        check(CaptureCameraResolver.Find(head, out _) == third,
            "Default manually rendered screen camera was skipped for unused custom camera.");
        var cinematic = new Camera("Cinematic");
        owner.currentScreenCamera = cinematic;
        check(CaptureCameraResolver.Find(head, out _) == cinematic, "Camera mode switch retained old camera.");
        var replacementOwner = new Current { currentScreenCamera = third };
        Il2Cpp.MainCameraEnabler.s_instance = replacementOwner;
        check(CaptureCameraResolver.Find(head, out _) == third, "Old scene's camera manager overrode the active manager.");
        Resources.All.Remove(replacementOwner);
        Il2Cpp.MainCameraEnabler.s_instance = null;
        owner.currentScreenCamera = head;
        check(CaptureCameraResolver.Find(head, out var status) == null && status.Contains("First-person"),
            "First-person mode incorrectly marked an unused third-person camera.");
        owner.currentScreenCamera = third;
        owner.FirstPerson_CameraObject = third.gameObject;
        check(CaptureCameraResolver.Find(head, out _) == null, "Non-stereo first-person output received a marker.");
        owner.FirstPerson_CameraObject = null;
        owner.OFF_CameraObject = third.gameObject;
        check(CaptureCameraResolver.Find(head, out status) == null && status.Contains("OFF"),
            "OFF output was treated as a capture camera.");

        Resources.All.Clear();
        var legacy = new BrokenCurrent { Spectator_CameraObject = third.gameObject };
        check(CaptureCameraResolver.Find(head, out _) == third, "Missing screen-camera getter broke legacy spectator fallback.");
        legacy.Spectator_CameraObject = null;
        legacy.ThirdPersonSwing_CameraObject = third.gameObject;
        check(CaptureCameraResolver.Find(head, out _) == third, "Swing camera mode was missed.");
        legacy.ThirdPersonSwing_CameraObject = null;
        legacy.Cinematic_CameraObject = cinematic.gameObject;
        check(CaptureCameraResolver.Find(head, out _) == cinematic, "Legacy cinematic camera was missed.");

        Resources.All.Clear();
        _ = new BrokenCustom { customCameraTransform = third.transform };
        check(CaptureCameraResolver.Find(head, out _) == third, "Missing custom getter prevented transform fallback.");
        third.gameObject.activeInHierarchy = false;
        check(CaptureCameraResolver.Find(head, out _) == null, "Inactive camera was reported as visible.");
        third.gameObject.activeInHierarchy = true;
        third.gameObject.scene.Valid = false;
        check(CaptureCameraResolver.Find(head, out _) == null, "Prefab camera was reported as a scene camera.");
        third.gameObject.scene.Valid = true;
        third.cameraType = CameraType.Reflection;
        check(CaptureCameraResolver.Find(head, out _) == null, "Mirror rendering camera received a marker.");
        third.cameraType = CameraType.Game;

        Resources.All.Clear();
        _ = new Current { currentScreenCamera = third };
        var liv = new UnityEngine.GameObject("LIV Camera Rig");
        third.transform.parent = liv.transform;
        check(CaptureCameraResolver.Find(head, out status) == null && status.Contains("LIV"),
            "LIV's camera received a duplicate guide.");
        liv.name = "NativeWrapper";
        _ = new Liv { CameraWrapper = liv };
        check(CaptureCameraResolver.Find(head, out _) == null, "LIV wrapper was ignored when camera was renamed.");
        Resources.All.Clear();
        check(CaptureCameraResolver.Find(head, out status) == null && status.Contains("No active"),
            "Missing cameras did not produce an actionable status.");

        // All modes can keep their objects active. A missing/stale current
        // output must use the selected mode, never the first active camera.
        third.transform.parent = null;
        owner = new Current { currentScreenCamera = third, ThirdPerson_CameraObject = third.gameObject,
            Spectator_CameraObject = new Camera("Cam").gameObject,
            Cinematic_CameraObject = new Camera("Cam").gameObject,
            ThirdPersonSwing_CameraObject = new Camera("Cam") { enabled = false }.gameObject };
        Il2Cpp.MainCameraEnabler.s_instance = owner;
        foreach (var (mode, expected) in new[] { ("Spectator", owner.Spectator_CameraObject),
                     ("Cinematic", owner.Cinematic_CameraObject), ("ThirdPersonSwing", owner.ThirdPersonSwing_CameraObject) })
        {
            Il2Cpp.Game_InfoProvider.CurrentCamType = mode;
            check(CaptureCameraResolver.Find(head, out status)?.gameObject == expected && status.Contains(mode),
                $"{mode} selected the stale third-person output.");
            owner.currentScreenCamera = null;
            check(CaptureCameraResolver.Find(head, out _)?.gameObject == expected,
                $"{mode} fallback selected a different active mode.");
            owner.currentScreenCamera = third;
        }
        var spectator = owner.Spectator_CameraObject.GetComponentInChildren<Camera>(true)!;
        var film = owner.Cinematic_CameraObject.GetComponentInChildren<Camera>(true)!;
        check(CaptureCameraResolver.Identity(spectator) != CaptureCameraResolver.Identity(film),
            "Same-name camera replacement would not change diagnostics.");
        foreach (var mode in new[] { "OFF", "FirstPerson", "FirstPersonWide" })
        {
            Il2Cpp.Game_InfoProvider.CurrentCamType = mode;
            check(CaptureCameraResolver.Find(head, out _) == null, $"{mode} selected an unused third-person camera.");
        }
        Il2Cpp.Game_InfoProvider.CurrentCamType = null;
        owner.currentScreenCamera = null;
        check(CaptureCameraResolver.Find(head, out status) == null && status.Contains("candidates"),
            "Ambiguous active modes were resolved by enumeration order.");
        Il2Cpp.MainCameraEnabler.s_instance = new Current();
        check(CaptureCameraResolver.Find(head, out _) == null,
            "A stale scene's manager supplied a camera when the current manager was waiting.");
        Il2Cpp.MainCameraEnabler.s_instance = null;
        Resources.All.Clear();
        var closedSettings = new Custom { customCamera = third };
        closedSettings.gameObject.activeInHierarchy = false;
        Il2Cpp.Game_InfoProvider.CurrentCamType = "ThirdPersonCustom";
        check(CaptureCameraResolver.Find(head, out _) == third,
            "Closing the camera settings UI hid its independent output camera.");
        Resources.All.Clear();
        var ambiguousRoot = new GameObject("SpectatorRoot");
        new Camera("Cam").transform.parent = ambiguousRoot.transform;
        new Camera("Cam").transform.parent = ambiguousRoot.transform;
        owner = new Current { Spectator_CameraObject = ambiguousRoot };
        Il2Cpp.MainCameraEnabler.s_instance = owner;
        Il2Cpp.Game_InfoProvider.CurrentCamType = "Spectator";
        check(CaptureCameraResolver.Find(head, out _) == null,
            "Multiple active child cameras were resolved by enumeration order.");
        owner.currentScreenCamera = ambiguousRoot.GetComponentInChildren<Camera>(true);
        check(CaptureCameraResolver.Find(head, out _) == owner.currentScreenCamera,
            "Explicit current output was rejected just because its root has multiple cameras.");
        var selected = owner.currentScreenCamera!;
        check(CaptureCameraResolver.IsStillSelected(selected, "Spectator"), "Selected output failed the render-time guard.");
        owner.currentScreenCamera = film;
        check(!CaptureCameraResolver.IsStillSelected(selected, "Spectator"), "Old output passed the render-time guard after replacement.");
        owner.currentScreenCamera = selected;
        Il2Cpp.Game_InfoProvider.CurrentCamType = "OFF";
        check(!CaptureCameraResolver.IsStillSelected(selected, "Spectator"), "Camera OFF did not invalidate a cached output.");
        Il2Cpp.MainCameraEnabler.s_instance = null;
        Il2Cpp.Game_InfoProvider.CurrentCamType = null;
        Resources.All.Clear();
    }

    private sealed class Current : Il2Cpp.MainCameraEnabler
    {
        public Camera? currentScreenCamera;
        public GameObject? FirstPerson_CameraObject;
        public GameObject? OFF_CameraObject;
        public GameObject? ThirdPerson_CameraObject;
        public GameObject? ThirdPersonSwing_CameraObject;
        public GameObject? Spectator_CameraObject;
        public GameObject? Cinematic_CameraObject;
    }
    private sealed class BrokenCurrent : Il2Cpp.MainCameraEnabler
    {
        public Camera currentScreenCamera => throw new MissingMethodException();
        public GameObject? Spectator_CameraObject;
        public GameObject? ThirdPersonSwing_CameraObject;
        public GameObject? Cinematic_CameraObject;
    }
    private sealed class Custom : Il2Cpp.CustomCameraSettings { public Camera? customCamera; }
    private sealed class BrokenCustom : Il2Cpp.CustomCameraSettings
    {
        public Camera customCamera => throw new MissingMethodException();
        public Transform? customCameraTransform;
    }
    private sealed class Liv : Il2Cppcom.Kluge.XR.Utils.LivCameraWrapper { public GameObject? CameraWrapper; }
}

namespace UnityEngine
{
    public static class Resources
    {
        public static List<object> All { get; } = new();
        public static T[] FindObjectsOfTypeAll<T>() => All.OfType<T>().ToArray();
    }
    public class Component
    {
        private static int _nextId;
        private readonly int _id = ++_nextId;
        public int GetInstanceID() => _id;
        internal static readonly List<Component> Instances = new();
        public GameObject gameObject { get; }
        public Transform transform => gameObject.transform;
        public string name => gameObject.name;
        public Component(GameObject? go = null)
        {
            gameObject = go ?? new GameObject("Object");
            Resources.All.Add(this);
            Instances.Add(this);
        }
    }
    public class GameObject
    {
        public int GetInstanceID() => transform.GetInstanceID();
        public string name;
        public int layer;
        public bool activeInHierarchy = true;
        public Scene scene { get; } = new();
        public Transform transform { get; }
        public GameObject(string name) { this.name = name; transform = new Transform(this); }
        public T? GetComponentInChildren<T>(bool includeInactive) where T : Component =>
            transform.GetComponentsInChildren<T>(includeInactive).FirstOrDefault();
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component =>
            transform.GetComponentsInChildren<T>(includeInactive);
    }
    public class Scene { public bool Valid = true; public int handle = 1; public bool IsValid() => Valid; }
    public class Transform : Component
    {
        public Transform? parent;
        public System.Numerics.Vector3 localPosition;
        public System.Numerics.Vector3 localScale = System.Numerics.Vector3.One;
        public Quaternion localRotation = Quaternion.identity;
        public Quaternion rotation
        {
            get => parent == null ? localRotation : parent.rotation * localRotation;
            set => localRotation = parent == null ? value : Quaternion.Inverse(parent.rotation) * value;
        }
        public System.Numerics.Vector3 lossyScale => (parent?.lossyScale ?? System.Numerics.Vector3.One) * localScale;
        public System.Numerics.Vector3 position => parent == null ? localPosition : parent.position +
            System.Numerics.Vector3.Transform(localPosition * parent.lossyScale, parent.rotation.Value);
        public Transform(GameObject go) : base(go) { }
        public bool IsChildOf(Transform other) => this == other || parent?.IsChildOf(other) == true;
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component =>
            Instances.OfType<T>().Where(c => c.transform.IsChildOf(this)).ToArray();
    }
    public enum CameraType { Game, Reflection, Preview }
    public partial class Camera : Component
    {
        public bool stereoEnabled;
        public bool enabled = true;
        public CameraType cameraType;
        public int cullingMask = -1;
        public Camera(string name) : base(new GameObject(name)) { }
    }
}
namespace Il2Cpp
{
    public class MainCameraEnabler : UnityEngine.Component { public static MainCameraEnabler? s_instance; }
    public class CustomCameraSettings : UnityEngine.Component { }
    public class Game_InfoProvider
    {
        private bool _hideInFirstPerson;
        public bool IgnoreVisibilityWrites;
        public bool HideInFirtPersonView
        {
            get => _hideInFirstPerson;
            set { if (!IgnoreVisibilityWrites) _hideInFirstPerson = value; }
        }
        public static object? CurrentCamType;
        public static string? Language;
        public static bool LanguageUnavailable;
        public static string? CurrentLangString => LanguageUnavailable ? throw new MissingMethodException() : Language;
    }
}
namespace Il2Cppcom.Kluge.XR.Utils { public class LivCameraWrapper : UnityEngine.Component { } }
