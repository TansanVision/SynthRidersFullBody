using Il2Cpp;
using Il2CppSynth.SongSelection;
using SynthRidersFullBody;
using UnityEngine.SceneManagement;

/// <summary>プレイ・一時停止・曲開始と、表示を維持するメニュー／リザルトを区別します。</summary>
internal static class CameraContextTests
{
    /// <summary>実際の状態判定コードへ各管理オブジェクトの状態を与えます。</summary>
    public static void Run(Action<bool, string> check)
    {
        SceneManager.Active.handle = 1;
        var menu = new SongSelectionManager();
        SongSelectionManager.s_instance = menu;
        foreach (var panel in new[] { "Home", "SongSelection", "Settings", "StageSelection", "None" })
        {
            menu.currentSelectedPanel = panel;
            check(!MenuCameraContext.UseGameCamera(), "A non-gameplay panel canceled the bust shot: " + panel);
        }
        menu.isOnSceneLoading = true;
        check(!MenuCameraContext.UseGameCamera(), "Generic menu loading canceled the visible avatar's bust shot.");
        menu.IsLoadingGameScene = true;
        check(MenuCameraContext.UseGameCamera(), "Song loading retained a bust shot.");
        menu.IsLoadingGameScene = false;
        menu.onGameStartCounter = true;
        check(MenuCameraContext.UseGameCamera(), "Song start countdown retained a bust shot.");
        menu.onGameStartCounter = false;
        foreach (var panel in new[] { "PlayableTutorial", "MultiplayerGameStartCounter" })
        {
            menu.currentSelectedPanel = panel;
            check(MenuCameraContext.UseGameCamera(), "Playable/tutorial countdown retained a bust shot.");
        }
        SceneManager.Active.handle = 2;
        check(!MenuCameraContext.UseGameCamera(), "An old scene's menu flags canceled the results bust shot.");
        var game = new GameControlManager();
        GameControlManager.s_instance = game;
        game.gameObject.scene.handle = 2;
        check(MenuCameraContext.UseGameCamera(), "The gameplay scene before music starts retained a bust shot.");
        game.IsPlaying = true;
        check(MenuCameraContext.UseGameCamera(), "Active gameplay retained a bust shot.");
        game.IsPlaying = false;
        GameControlManager.IsPaused = true;
        game.gameObject.scene.handle = 9; // A persistent manager can belong to another scene.
        check(MenuCameraContext.UseGameCamera(), "Paused gameplay retained a bust shot.");
        GameControlManager.IsPaused = false;
        check(!MenuCameraContext.UseGameCamera(), "An old inactive gameplay state canceled results framing.");
        game.IsPlaying = true;
        check(MenuCameraContext.UseGameCamera(), "A persistent active gameplay manager retained a bust shot.");
        game.gameObject.activeInHierarchy = false;
        check(!MenuCameraContext.UseGameCamera(), "Disabled gameplay manager canceled results framing.");
        GameControlManager.s_instance = null;
        SongSelectionManager.s_instance = null;
        check(!MenuCameraContext.UseGameCamera(), "Results without a menu manager canceled the bust shot.");
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
namespace Il2Cpp
{
    public sealed class GameControlManager : UnityEngine.Component
    {
        public static GameControlManager? s_instance;
        public bool IsPlaying;
        public static bool IsPaused;
    }
}
namespace Il2CppSynth.SongSelection
{
    public sealed class SongSelectionManager : UnityEngine.Component
    {
        public static SongSelectionManager? s_instance;
        public string currentSelectedPanel = "Home";
        public bool IsLoadingGameScene, isOnSceneLoading, onGameStartCounter;
    }
}
