using Il2Cpp;
using Il2CppSynth.SongSelection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SynthRidersFullBody;

/// <summary>曲の開始・プレイ・一時停止中はゲームのカメラ制御へ戻すため、実際のゲーム状態を確認します。</summary>
internal static class MenuCameraContext
{
    /// <summary>プレイ用の管理オブジェクト、またはメニューからの曲開始処理が有効な場合はtrue。</summary>
    public static bool UseGameCamera()
    {
        var scene = SceneManager.GetActiveScene();
        var game = OptionalMember.ReadStatic(typeof(GameControlManager), "s_instance") as GameControlManager;
        if (Active(game) && (game!.gameObject.scene.handle == scene.handle ||
            OptionalMember.Read(game, "IsPlaying") is true ||
            OptionalMember.ReadStatic(typeof(GameControlManager), "IsPaused") is true)) return true;
        var menu = OptionalMember.ReadStatic(typeof(SongSelectionManager), "s_instance") as SongSelectionManager;
        if (!Active(menu) || menu!.gameObject.scene.handle != scene.handle) return false;
        // Generic menu loading and settings/results panels do not cancel a bust shot.
        if (OptionalMember.Read(menu, "IsLoadingGameScene") is true ||
            OptionalMember.Read(menu, "onGameStartCounter") is true) return true;
        return OptionalMember.Read(menu, "currentSelectedPanel")?.ToString() is
            "PlayableTutorial" or "MultiplayerGameStartCounter";
    }

    /// <summary>破棄済み・無効・シーン外の管理オブジェクトを除外します。</summary>
    private static bool Active(Component? component) => component != null &&
        component.gameObject.activeInHierarchy && component.gameObject.scene.IsValid();
}
