using Il2CppSynth.Avatars;
using UnityEngine;

namespace SynthRidersFullBody;

// Uses the game's per-avatar first-person setting instead of changing model layers.
/// <summary>
/// ゲームの一人称表示設定を操作し、アバターごとのHMD表示設定を再適用します。
/// </summary>
internal sealed class AvatarHmdVisibility
{
    private AvatarLoader? _loader;
    private float _nextSearch;
    private int _lastAppliedAvatarId;
    private string? _lastAppliedKey;

    /// <summary>
    /// 現在ロードされているアバターのルートを取得します。
    /// </summary>
    /// <returns>有効なローダーのアバター。見つからない場合はnull。</returns>
    public GameObject? GetActiveAvatarRoot() => FindLoader()?.vrmObject;

    /// <summary>
    /// ローダーと適用済みアバターのキャッシュを無効にして再探索を要求します。
    /// </summary>
    public void RequestRefresh()
    {
        _loader = null;
        _nextSearch = 0f;
        _lastAppliedAvatarId = 0;
        _lastAppliedKey = null;
    }

    /// <summary>
    /// 現在のアバターをHMD内に表示する設定かどうかを取得します。
    /// </summary>
    /// <param name="visible">取得成功時に、HMD内へ表示する設定かどうかを受け取ります。</param>
    /// <returns>アバターと固有設定を取得できた場合はtrue。</returns>
    public bool TryGet(out bool visible)
    {
        visible = false;
        var loader = FindLoader();
        if (loader == null || loader.vrmObject == null ||
            !loader.vrmObject.activeInHierarchy || loader.specificSettings == null)
            return false;
        visible = !loader.specificSettings.HideInFirtPersonView;
        return true;
    }

    /// <summary>
    /// ゲームの一人称表示を反転し、そのアバターの希望値を設定オブジェクトへ記録します。
    /// </summary>
    /// <param name="config">使用する共有設定。保存処理がないメソッドではファイルへの書き込みは行いません。</param>
    /// <returns>表示を変更して希望値を記録できた場合はtrue。</returns>
    public bool TryToggle(ModConfig config)
    {
        if (!TryGet(out var currentlyVisible)) return false;
        var key = AvatarKey(_loader!);
        if (key == null) return false;
        _loader!.ToggleAvatarsFirstPersonModel();
        config.AvatarVisibleInHeadset ??= new Dictionary<string, bool>();
        config.AvatarVisibleInHeadset[key] = !currentlyVisible;
        _lastAppliedAvatarId = _loader.vrmObject.GetInstanceID();
        _lastAppliedKey = key;
        return true;
    }

    /// <summary>
    /// ロード完了後のアバターへ、保存済みのHMD表示設定を必要な場合だけ再適用します。
    /// </summary>
    /// <param name="config">使用する共有設定。保存処理がないメソッドではファイルへの書き込みは行いません。</param>
    /// <returns>実際に表示状態を切り替えた場合はtrue。</returns>
    public bool ApplySaved(ModConfig config)
    {
        var loader = FindLoader();
        if (loader == null || loader.vrmObject == null || loader.specificSettings == null)
        {
            _lastAppliedAvatarId = 0;
            _lastAppliedKey = null;
            return false;
        }
        if (loader.avatarIsLoading || !loader.hasAvatarLoaded) return false;
        var avatarId = loader.vrmObject.GetInstanceID();
        var key = AvatarKey(loader);
        if (key == null) return false;
        if (avatarId == _lastAppliedAvatarId && key == _lastAppliedKey) return false;
        if (config.AvatarVisibleInHeadset == null ||
            !config.AvatarVisibleInHeadset.TryGetValue(key, out var desired) ||
            desired == !loader.specificSettings.HideInFirtPersonView)
        {
            _lastAppliedAvatarId = avatarId;
            _lastAppliedKey = key;
            return false;
        }
        loader.ToggleAvatarsFirstPersonModel();
        _lastAppliedAvatarId = avatarId;
        _lastAppliedKey = key;
        return true;
    }

    /// <summary>
    /// ファイルパス、または標準モデルの番号と名前からアバターの識別キーを作成します。
    /// </summary>
    /// <param name="loader">対象アバターを管理するゲーム側ローダー。</param>
    /// <returns>識別キー。選択中アバターの情報がない場合はnull。</returns>
    internal static string? AvatarKey(AvatarLoader loader)
    {
        var item = loader.activeAvatar;
        if (item == null) return null;
        if (!string.IsNullOrWhiteSpace(item.avatarPath))
            return "file:" + item.avatarPath.Trim().Replace('\\', '/').ToLowerInvariant();
        return "internal:" + item.avatarIndex + ":" + (item.avatarName ?? "");
    }

    /// <summary>
    /// 現在のアバターを持つローダーを、シングルトン・キャッシュ・シーンの順で探します。
    /// </summary>
    /// <returns>利用できるローダー。探索待機中または見つからない場合はnull。</returns>
    private AvatarLoader? FindLoader()
    {
        var instance = AvatarLoader.s_instance;
        if (HasAvatar(instance))
        {
            _loader = instance;
            return instance;
        }
        if (HasAvatar(_loader))
            return _loader;
        _loader = null;
        if (Time.unscaledTime < _nextSearch) return null;
        _nextSearch = Time.unscaledTime + 2f;
        foreach (var loader in Resources.FindObjectsOfTypeAll<AvatarLoader>())
            if (HasAvatar(loader))
            {
                _loader = loader;
                return loader;
            }
        return null;
    }

    /// <summary>
    /// ローダーとそのアバターが有効な階層に存在するかを確認します。
    /// </summary>
    /// <param name="loader">対象アバターを管理するゲーム側ローダー。</param>
    /// <returns>両方が存在し有効な場合はtrue。</returns>
    private static bool HasAvatar(AvatarLoader? loader) =>
        loader != null && loader.gameObject.activeInHierarchy &&
        loader.vrmObject != null && loader.vrmObject.activeInHierarchy;
}
