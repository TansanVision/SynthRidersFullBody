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
    private int _boundLoaderId;
    private int _boundAvatarId;
    private int _boundHeadsetId;
    private string? _boundKey;
    private bool? _appliedVisible;
    private int _appliedMask;
    private float _nextApply;

    /// <summary>最後の適用エラー。成功または対象切り替えで解除します。</summary>
    public string? Error { get; private set; }

    /// <summary>最後に適用したアバター・表示希望値・HMD描画マスクの診断情報です。</summary>
    public string LastApplication { get; private set; } = "";

    /// <summary>
    /// 現在ロードされているアバターのルートを取得します。
    /// </summary>
    /// <returns>有効なローダーのアバター。見つからない場合はnull。</returns>
    public GameObject? GetActiveAvatarRoot() => FindLoader()?.vrmObject;

    /// <summary>
    /// ローダーと描画先の記録を無効にして、シーン移動後の再適用を要求します。
    /// </summary>
    public void RequestRefresh()
    {
        _loader = null;
        _nextSearch = 0f;
        ResetBinding();
    }

    /// <summary>
    /// 現在のアバターをHMD内に表示する設定かどうかを取得します。
    /// </summary>
    /// <param name="visible">取得成功時に、HMD内へ表示する設定かどうかを受け取ります。</param>
    /// <returns>アバターの読み込みが完了し、固有設定を取得できた場合はtrue。</returns>
    public bool TryGet(out bool visible)
    {
        visible = false;
        var loader = FindLoader();
        if (!IsReady(loader)) return false;
        visible = !loader!.specificSettings!.HideInFirtPersonView;
        return true;
    }

    /// <summary>
    /// ロード完了後に一人称表示を反転し、ゲーム側設定の反映を確認して希望値を記録します。
    /// </summary>
    /// <param name="config">使用する共有設定。保存処理がないメソッドではファイルへの書き込みは行いません。</param>
    /// <returns>表示を変更して希望値を記録できた場合はtrue。</returns>
    public bool TryToggle(ModConfig config)
    {
        var loader = FindLoader();
        if (!IsReady(loader))
        {
            Error = "Avatar is not fully loaded; wait for loading to finish.";
            return false;
        }
        var key = AvatarKey(loader!);
        var headset = ReadyHeadset(loader!);
        if (key == null || headset == null)
        {
            Error = "Avatar identity or active headset camera is not available yet.";
            return false;
        }
        Bind(loader!, key, headset);
        var desired = loader!.specificSettings!.HideInFirtPersonView;
        if (!Apply(loader, headset, desired, "user toggle")) return false;
        config.AvatarVisibleInHeadset ??= new Dictionary<string, bool>();
        config.AvatarVisibleInHeadset[key] = desired;
        return true;
    }

    /// <summary>
    /// 保存済み設定を監視し、同じアバターの設定再読み込みやHMD交換・描画マスク変更後も再適用します。
    /// </summary>
    /// <param name="config">使用する共有設定。保存処理がないメソッドではファイルへの書き込みは行いません。</param>
    /// <returns>ゲームの設定値を確認し、一人称カメラへの適用処理が完了した場合はtrue。実際の描画結果は含みません。</returns>
    public bool ApplySaved(ModConfig config)
    {
        var loader = FindLoader();
        if (!IsReady(loader))
        {
            ResetBinding();
            return false;
        }
        var key = AvatarKey(loader!);
        var headset = ReadyHeadset(loader!);
        if (key == null || headset == null || config.AvatarVisibleInHeadset == null ||
            !config.AvatarVisibleInHeadset.TryGetValue(key, out var desired))
        {
            ResetBinding();
            return false;
        }
        Bind(loader!, key, headset);
        // Never use avatar identity alone as proof that the current setting is
        // still applied: the game can reset both settings and camera in place.
        var settingChanged = desired != !loader!.specificSettings!.HideInFirtPersonView;
        if (!settingChanged && _appliedVisible == desired && _appliedMask == headset.cullingMask)
            return false;
        if (Time.unscaledTime < _nextApply) return false;
        return Apply(loader, headset, desired, settingChanged ? "saved setting changed" : "headset/view refresh");
    }

    /// <summary>適用先が変わった場合だけ再試行待ちと前回の描画状態を解除します。</summary>
    /// <param name="loader">ロード完了済みのローダー。</param>
    /// <param name="key">保存設定の識別キー。</param>
    /// <param name="headset">ゲームが管理する一人称カメラ。</param>
    private void Bind(AvatarLoader loader, string key, Camera headset)
    {
        var loaderId = loader.GetInstanceID();
        var avatarId = loader.vrmObject!.GetInstanceID();
        var headsetId = headset.GetInstanceID();
        if (_boundLoaderId == loaderId && _boundAvatarId == avatarId &&
            _boundHeadsetId == headsetId && _boundKey == key) return;
        ResetBinding();
        _boundLoaderId = loaderId;
        _boundAvatarId = avatarId;
        _boundHeadsetId = headsetId;
        _boundKey = key;
    }

    /// <summary>ゲームの標準APIで設定と一人称カメラを更新し、失敗時は間隔を空けて再試行可能にします。</summary>
    /// <param name="loader">対象アバターのローダー。</param>
    /// <param name="headset">対象のHMDカメラ。</param>
    /// <param name="desired">HMD内に表示する希望値。</param>
    /// <param name="reason">ログに残す適用理由。</param>
    /// <returns>設定値が一致し、ゲームのカメラ更新APIが例外なく完了した場合はtrue。</returns>
    private bool Apply(AvatarLoader loader, Camera headset, bool desired, string reason)
    {
        _nextApply = Time.unscaledTime + 1f;
        _appliedVisible = null;
        var beforeMask = headset.cullingMask;
        try
        {
            if (desired != !loader.specificSettings!.HideInFirtPersonView)
                loader.ToggleAvatarsFirstPersonModel();
            if (desired != !loader.specificSettings.HideInFirtPersonView)
            {
                Error = $"Game did not accept headset visibility={desired}; avatar #{_boundAvatarId}.";
                return false;
            }
            // Refresh the native view even when its boolean is already right.
            // A recreated/reset HMD camera may still have its default mask.
            loader.SetFirstPersonCameraView();
            if (desired != !loader.specificSettings.HideInFirtPersonView)
            {
                Error = $"Game reset headset visibility during view refresh; avatar #{_boundAvatarId}.";
                return false;
            }
            _appliedVisible = desired;
            _appliedMask = headset.cullingMask;
            Error = null;
            LastApplication = $"avatar={loader.vrmObject!.name} #{_boundAvatarId}, loader=#{_boundLoaderId}, " +
                $"visible={desired}, headset={headset.name} #{_boundHeadsetId}, " +
                $"mask={beforeMask:X8}->{_appliedMask:X8}, reason={reason}";
            return true;
        }
        catch (Exception exception)
        {
            Error = $"Headset visibility apply failed for avatar #{_boundAvatarId}: {exception}";
            return false;
        }
    }

    /// <summary>読み込み中や対象不在の状態を、前回の適用完了として扱わないようにします。</summary>
    private void ResetBinding()
    {
        _boundLoaderId = _boundAvatarId = _boundHeadsetId = 0;
        _boundKey = null;
        _appliedVisible = null;
        _nextApply = 0f;
        Error = null;
        LastApplication = "";
    }

    /// <summary>アバターと固有設定の読み込みが完了しているかを確認します。</summary>
    /// <param name="loader">対象のローダー。</param>
    /// <returns>操作可能なロード済みアバターの場合はtrue。</returns>
    private static bool IsReady(AvatarLoader? loader) => HasAvatar(loader) &&
        !loader!.avatarIsLoading && loader.hasAvatarLoaded && loader.specificSettings != null;

    /// <summary>ゲームが管理する、シーン内で有効な一人称カメラを取得します。</summary>
    /// <param name="loader">対象のローダー。</param>
    /// <returns>有効なHMDカメラ。ロード途中などで存在しない場合はnull。</returns>
    private static Camera? ReadyHeadset(AvatarLoader loader)
    {
        var camera = loader.m_HMDCamera;
        return camera != null && camera.gameObject.scene.IsValid() && camera.gameObject.activeInHierarchy
            ? camera : null;
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
    /// 現在のシングルトンを優先します。ロード中は旧シーンのアバターへフォールバックしません。
    /// </summary>
    /// <returns>利用できるローダー。探索待機中または見つからない場合はnull。</returns>
    private AvatarLoader? FindLoader()
    {
        var instance = AvatarLoader.s_instance;
        if (instance != null && instance.gameObject.scene.IsValid() && instance.gameObject.activeInHierarchy)
        {
            _loader = instance;
            return HasAvatar(instance) ? instance : null;
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
    /// ローダーとそのアバターが有効なシーンと階層に存在するかを確認します。
    /// </summary>
    /// <param name="loader">対象アバターを管理するゲーム側ローダー。</param>
    /// <returns>両方が存在し有効な場合はtrue。</returns>
    private static bool HasAvatar(AvatarLoader? loader) =>
        loader != null && loader.gameObject.scene.IsValid() && loader.gameObject.activeInHierarchy &&
        loader.vrmObject != null && loader.vrmObject.scene.IsValid() && loader.vrmObject.activeInHierarchy;
}
