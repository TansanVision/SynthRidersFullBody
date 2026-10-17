using System.Numerics;
using SynthRidersFullBody;

/// <summary>実装した構図計算とメニュー判定を、ゲームを起動せず検証します。</summary>
internal static class MenuBustCameraTests
{
    /// <summary>体格・向き・縦横比・近クリップ、および曲開始・シーン移動の対象制限を確認します。</summary>
    public static void Run(Action<bool, string> check)
    {
        foreach (var scale in new[] { .15f, 1f, 3f, 10f })
        foreach (var aspect in new[] { .5f, 9f / 16f, 16f / 9f, 3f })
        foreach (var yaw in new[] { 0f, 90f, 180f, 270f })
        foreach (var clip in new[] { .01f, .3f })
        {
            var root = new Vector3(3, -.2f, 1);
            var head = root + Vector3.UnitY * (1.6f * scale);
            var chest = root + Vector3.UnitY * (1.25f * scale);
            var hips = root + Vector3.UnitY * scale;
            var forward = Vector3.Transform(Vector3.UnitZ, Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw * MathF.PI / 180));
            check(MenuBustFraming.TryCreate(head, chest, hips, forward, .5f * scale, aspect, clip,
                out var camera, out var focus), "Valid avatar could not be framed.");
            var distance = Vector3.Distance(camera, focus);
            check(Vector3.Dot(camera - focus, forward) > 0 && MathF.Abs(camera.Y - focus.Y) < .0001f,
                "Bust camera was behind the avatar or tilted above/below the framing center.");
            var torso = head.Y - hips.Y;
            var depth = torso * .35f;
            var tangent = MathF.Tan(MenuBustFraming.FieldOfView * MathF.PI / 360);
            var halfHeight = (head.Y + torso * .38f - (chest.Y - torso * .18f)) * .5f;
            var halfWidth = MathF.Max(.5f * scale * .65f, torso * .48f);
            check(halfHeight / ((distance - depth) * tangent) <= 1.0001f &&
                halfWidth / ((distance - depth) * tangent * aspect) <= 1.0001f,
                "Head/chest/shoulders were clipped by the framing frustum.");
            check(distance - depth >= clip, "Near clip intersected the framed body.");
        }
        foreach (var aspect in new[] { 0f, float.NaN, float.PositiveInfinity })
            check(!MenuBustFraming.TryCreate(Vector3.UnitY * 1.6f, Vector3.UnitY * 1.2f, Vector3.UnitY,
                Vector3.UnitZ, .5f, aspect, .1f, out _, out _), "Invalid output aspect framed the camera.");
        check(!MenuBustFraming.TryCreate(Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.UnitZ,
            .5f, 1f, .1f, out _, out _), "Unloaded/degenerate skeleton framed the camera.");
        check(!MenuBustFraming.TryCreate(Vector3.UnitY * 1.6f, Vector3.UnitY * 1.2f, Vector3.UnitY,
            Vector3.UnitY, .5f, 1f, .1f, out _, out _), "Vertical avatar forward produced an arbitrary camera side.");

        var manager = new Il2CppSynth.SongSelection.SongSelectionManager();
        Il2CppSynth.SongSelection.SongSelectionManager.s_instance = manager;
        UnityEngine.SceneManagement.SceneManager.Active.handle = 1;
        manager.homePanel = new UnityEngine.GameObject("Home");
        manager.songSelectionPanel = new UnityEngine.GameObject("Songs");
        foreach (var panel in new[] { "Home", "SongSelection" })
        {
            manager.currentSelectedPanel = panel;
            check(MenuCameraContext.IsOpen(), "Home/song panel was not allowed.");
            manager.IsLoadingGameScene = true;
            check(!MenuCameraContext.IsOpen(), "Camera remained locked during song loading.");
            manager.IsLoadingGameScene = false;
            manager.isOnSceneLoading = true;
            check(!MenuCameraContext.IsOpen(), "Camera remained locked during scene loading.");
            manager.isOnSceneLoading = false;
            manager.onGameStartCounter = true;
            check(!MenuCameraContext.IsOpen(), "Camera remained locked during the start countdown.");
            manager.onGameStartCounter = false;
        }
        foreach (var panel in new[] { "None", "Settings", "PlayableTutorial", "MultiplayerGameStartCounter", "StageSelection" })
        {
            manager.currentSelectedPanel = panel;
            check(!MenuCameraContext.IsOpen(), "An unrelated/game panel activated the bust camera.");
        }
        manager.currentSelectedPanel = "Home";
        manager.homePanel.activeInHierarchy = false;
        check(!MenuCameraContext.IsOpen(), "Hidden home panel activated the bust camera.");
        manager.homePanel.activeInHierarchy = true;
        UnityEngine.SceneManagement.SceneManager.Active.handle = 2;
        check(!MenuCameraContext.IsOpen(), "Old scene's menu affected a new stage camera.");
        UnityEngine.SceneManagement.SceneManager.Active.handle = 1;
        manager.gameObject.activeInHierarchy = false;
        check(!MenuCameraContext.IsOpen(), "Inactive menu manager activated the camera.");
        Il2CppSynth.SongSelection.SongSelectionManager.s_instance = null;
        check(!MenuCameraContext.IsOpen(), "Missing manager activated the camera.");
    }
}

namespace UnityEngine.SceneManagement
{
    public static class SceneManager
    {
        public static UnityEngine.Scene Active = new();
        public static UnityEngine.Scene GetActiveScene() => Active;
    }
}

namespace Il2CppSynth.SongSelection
{
    public sealed class SongSelectionManager : UnityEngine.Component
    {
        public static SongSelectionManager? s_instance;
        public string currentSelectedPanel = "Home";
        public UnityEngine.GameObject? homePanel, songSelectionPanel;
        public bool IsLoadingGameScene, isOnSceneLoading, onGameStartCounter;
    }
}
