using Il2CppSynth.SongSelection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SynthRidersFullBody;

/// <summary>ホームと曲選択の実際のパネル状態を確認し、プレイ・ロード・旧シーンを除外します。</summary>
internal static class MenuCameraContext
{
    /// <summary>現在のシーンでホームまたは曲選択が表示され、曲開始処理中でない場合はtrue。</summary>
    public static bool IsOpen()
    {
        var manager = OptionalMember.ReadStatic(typeof(SongSelectionManager), "s_instance") as SongSelectionManager;
        if (manager == null || !manager.gameObject.activeInHierarchy || !manager.gameObject.scene.IsValid() ||
            manager.gameObject.scene.handle != SceneManager.GetActiveScene().handle) return false;
        if (OptionalMember.Read(manager, "IsLoadingGameScene") is true ||
            OptionalMember.Read(manager, "isOnSceneLoading") is true ||
            OptionalMember.Read(manager, "onGameStartCounter") is true) return false;
        var panel = OptionalMember.Read(manager, "currentSelectedPanel")?.ToString();
        var member = panel switch { "Home" => "homePanel", "SongSelection" => "songSelectionPanel", _ => null };
        return member != null && OptionalMember.Read(manager, member) is GameObject go && go != null && go.activeInHierarchy;
    }
}
