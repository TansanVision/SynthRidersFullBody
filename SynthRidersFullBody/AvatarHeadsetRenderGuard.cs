using UnityEngine;
using UnityEngine.Rendering;

namespace SynthRidersFullBody;

/// <summary>
/// アバター配下の描画部品をHMD描画中だけ隠します。通常レイヤーの小物も対象にし、
/// 鏡・配信カメラの描画前と描画終了時には元の状態へ戻します。
/// </summary>
internal sealed class AvatarHeadsetRenderGuard : IDisposable
{
    private GameObject? _avatar;
    private Camera? _headset;
    private Renderer[] _renderers = Array.Empty<Renderer>();
    private readonly List<Renderer> _suppressed = new();
    private readonly List<(Camera Camera, bool Srp)> _renders = new();
    private float _nextScan;
    private bool _observedRender;
    private string _inventory = "";
    private string? _pendingDiagnostic;
    private Camera.CameraCallback? _before, _after;
    private Il2CppSystem.Action<ScriptableRenderContext, Camera>? _beginSrp, _endSrp;

    /// <summary>イベント処理や復元の失敗。ゲームの描画イベント外でログへ出します。</summary>
    public string? Error { get; private set; }

    /// <summary>カリング前と描画終了のイベントを、Built-inとSRPの両方へ登録します。</summary>
    public void Initialize()
    {
        if (_before != null) return;
        try
        {
            _before = (Action<Camera>)(camera => BeforeRender(camera, false));
            _after = (Action<Camera>)(camera => AfterRender(camera, false));
            _beginSrp = (Action<ScriptableRenderContext, Camera>)((_, camera) => BeforeRender(camera, true));
            _endSrp = (Action<ScriptableRenderContext, Camera>)((_, camera) => AfterRender(camera, true));
            Camera.onPreCull += _before;
            Camera.onPostRender += _after;
            RenderPipelineManager.beginCameraRendering += _beginSrp;
            RenderPipelineManager.endCameraRendering += _endSrp;
        }
        catch { Dispose(); throw; }
    }

    /// <summary>前フレームの描画中断に備え、残った一時変更と描画スタックを解除します。</summary>
    public void Recover()
    {
        RestoreRenderers();
        _renders.Clear();
    }

    /// <summary>
    /// 読み込み済みアバターとHMDを設定します。非表示中は1秒ごとに小物の追加も再探索します。
    /// 呼び出しは描画イベント外で行います。
    /// </summary>
    public void Update(GameObject? avatar, Camera? headset, bool hide)
    {
        Recover();
        if (!hide) { avatar = null; headset = null; }
        if (_avatar != avatar || _headset != headset)
        {
            _avatar = avatar;
            _headset = headset;
            _renderers = Array.Empty<Renderer>();
            _nextScan = 0f;
            _observedRender = false;
            _inventory = "";
            _pendingDiagnostic = null;
            Error = null;
        }
        if (_avatar == null || _headset == null) return;
        if (_before == null)
        {
            Error = "HMD renderer guard has no render callbacks.";
            return;
        }
        if (Time.unscaledTime < _nextScan) return;
        _nextScan = Time.unscaledTime + 1f;
        try
        {
            _renderers = _avatar.GetComponentsInChildren<Renderer>(true).ToArray();
            // Include mask-admitted objects even when currently disabled: an
            // expression or animation may enable an accessory later.
            var admitted = _renderers.Where(r => r != null &&
                (_headset.cullingMask & (1 << r.gameObject.layer)) != 0).ToArray();
            var objects = string.Join("; ", admitted.Take(64).Select(r =>
                $"{r.name} #{r.GetInstanceID()} layer={r.gameObject.layer}"));
            var inventory = $"avatar={_avatar.name} #{_avatar.GetInstanceID()}, " +
                $"headset={_headset.name} #{_headset.GetInstanceID()}, renderers={_renderers.Length}, " +
                $"native-mask-admitted={admitted.Length}, " +
                $"objects=[{objects}]" +
                (admitted.Length > 64 ? " (first 64)" : "");
            if (_inventory != inventory)
            {
                _inventory = inventory;
                _pendingDiagnostic = $"{inventory}, HMD callback observed={_observedRender}";
            }
        }
        catch (Exception exception) { Error = $"HMD renderer scan failed: {exception.Message}"; }
    }

    /// <summary>対象一覧の変化と最初のHMD描画を一度だけログへ渡します。</summary>
    public string? TakeDiagnostic()
    {
        var result = _pendingDiagnostic;
        _pendingDiagnostic = null;
        return result;
    }

    /// <summary>直前の描画変更を戻し、このカメラに必要な非表示だけを適用します。</summary>
    private void BeforeRender(Camera camera, bool srp)
    {
        if (_avatar == null || _headset == null || camera == null) return;
        try
        {
            // A mirror can render recursively while the headset is rendering.
            // Restore before it culls; re-hide when it returns to the headset.
            RestoreRenderers();
            if (_renders.Count >= 32) _renders.Clear(); // Broken/missing end callbacks.
            _renders.Add((camera, srp));
            SuppressForCurrentCamera();
        }
        catch (Exception exception)
        {
            Error = $"HMD renderer begin failed: {exception.Message}";
            Recover();
        }
    }

    /// <summary>対応するイベントだけを終了し、入れ子の親カメラの表示状態を復帰します。</summary>
    private void AfterRender(Camera camera, bool srp)
    {
        try
        {
            var index = _renders.Count - 1;
            while (index >= 0 && (_renders[index].Camera != camera || _renders[index].Srp != srp)) index--;
            if (index < 0) return;
            RestoreRenderers();
            _renders.RemoveAt(index);
            SuppressForCurrentCamera();
        }
        catch (Exception exception)
        {
            Error = $"HMD renderer end failed: {exception.Message}";
            Recover();
        }
    }

    /// <summary>現在の描画が対象HMDのときだけ、生存するアバター配下のRendererを抑止します。</summary>
    private void SuppressForCurrentCamera()
    {
        if (_renders.Count == 0 || _renders[^1].Camera != _headset ||
            _avatar == null || !_avatar.activeInHierarchy) return;
        foreach (var renderer in _renderers)
        {
            if (renderer == null || !renderer.transform.IsChildOf(_avatar.transform) ||
                !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.forceRenderingOff) continue;
            // Only remember flags we own. Already-hidden objects must stay hidden.
            _suppressed.Add(renderer);
            renderer.forceRenderingOff = true;
        }
        if (!_observedRender)
        {
            _observedRender = true;
            _pendingDiagnostic = $"{_inventory}, HMD callback observed=True, suppressed={_suppressed.Count}";
        }
    }

    /// <summary>変更したフラグだけを復元します。一部が破棄されても他の部品の復元を続けます。</summary>
    private void RestoreRenderers()
    {
        for (var i = _suppressed.Count - 1; i >= 0; i--)
        {
            try
            {
                var renderer = _suppressed[i];
                if (renderer != null) renderer.forceRenderingOff = false;
                _suppressed.RemoveAt(i);
            }
            catch (Exception exception)
            {
                // Retain failed entries so the next camera/update can retry.
                Error = $"HMD renderer restore failed: {exception.Message}";
            }
        }
    }

    /// <summary>一時非表示を解除し、このインスタンスが登録したイベントだけを取り外します。</summary>
    public void Dispose()
    {
        Recover();
        if (_before != null) Camera.onPreCull -= _before;
        if (_after != null) Camera.onPostRender -= _after;
        if (_beginSrp != null) RenderPipelineManager.beginCameraRendering -= _beginSrp;
        if (_endSrp != null) RenderPipelineManager.endCameraRendering -= _endSrp;
        _before = _after = null;
        _beginSrp = _endSrp = null;
        _avatar = null;
        _headset = null;
        _renderers = Array.Empty<Renderer>();
    }
}
