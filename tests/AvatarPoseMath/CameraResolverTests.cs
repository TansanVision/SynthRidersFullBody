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
    }

    private sealed class Current : Il2Cpp.MainCameraEnabler
    {
        public Camera? currentScreenCamera;
        public GameObject? FirstPerson_CameraObject;
        public GameObject? OFF_CameraObject;
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
        public string name;
        public bool activeInHierarchy = true;
        public Scene scene { get; } = new();
        public Transform transform { get; }
        public GameObject(string name) { this.name = name; transform = new Transform(this); }
    }
    public class Scene { public bool Valid = true; public bool IsValid() => Valid; }
    public class Transform : Component
    {
        public Transform? parent;
        public Transform(GameObject go) : base(go) { }
        public bool IsChildOf(Transform other) => this == other || parent?.IsChildOf(other) == true;
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component =>
            Instances.OfType<T>().Where(c => c.transform.IsChildOf(this)).ToArray();
    }
    public enum CameraType { Game, Reflection, Preview }
    public class Camera : Component
    {
        public bool stereoEnabled;
        public bool enabled = true;
        public CameraType cameraType;
        public Camera(string name) : base(new GameObject(name)) { }
    }
}
namespace Il2Cpp
{
    public class MainCameraEnabler : UnityEngine.Component { public static MainCameraEnabler? s_instance; }
    public class CustomCameraSettings : UnityEngine.Component { }
}
namespace Il2Cppcom.Kluge.XR.Utils { public class LivCameraWrapper : UnityEngine.Component { } }
