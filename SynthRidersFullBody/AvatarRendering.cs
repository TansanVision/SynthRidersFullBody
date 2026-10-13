using UnityEngine;
using UnityEngine.Rendering;

namespace SynthRidersFullBody;

// Reversible per-avatar rendering controls. Never touches scene lights or global quality.
/// <summary>
/// 対象アバターの影・ライトを一時変更し、元の描画設定の保存と復元を管理します。
/// </summary>
internal sealed class AvatarRendering
{
    private readonly Dictionary<int, (Renderer Renderer, ShadowCastingMode Casting, bool Receiving)>
        _renderers = new();
    private readonly Dictionary<int, (Light Light, bool Enabled)> _lights = new();
    private GameObject? _avatar;
    private bool _shadowsOff;
    private bool _lightsOff;
    private float _nextScan;

    /// <summary>
    /// 次回更新でRendererとLightを再探索するよう要求します。
    /// </summary>
    public void RequestRescan() => _nextScan = 0f;

    /// <summary>
    /// アバター内のRendererとLightを走査し、共有設定に従って影とライトを切り替えます。
    /// </summary>
    /// <param name="avatar">処理対象のアバター。</param>
    /// <param name="shadowsOff">アバターの影の投射と受け取りを無効にするか。</param>
    /// <param name="lightsOff">アバター配下のライトを無効にするか。</param>
    public void Update(GameObject? avatar, bool shadowsOff, bool lightsOff)
    {
        if (_avatar != avatar)
        {
            Restore();
            _avatar = avatar;
        }
        if (_shadowsOff && !shadowsOff) RestoreRenderers();
        if (_lightsOff && !lightsOff) RestoreLights();
        if (_shadowsOff != shadowsOff || _lightsOff != lightsOff)
            _nextScan = 0f;
        _shadowsOff = shadowsOff;
        _lightsOff = lightsOff;
        if (_avatar == null || (!shadowsOff && !lightsOff)) return;
        if (Time.unscaledTime >= _nextScan)
        {
            _nextScan = Time.unscaledTime + 2f;
            if (shadowsOff)
                foreach (var renderer in _avatar.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null) continue;
                    var id = renderer.GetInstanceID();
                    if (!_renderers.TryGetValue(id, out var saved) ||
                        saved.Renderer != renderer)
                        _renderers[id] = (renderer, renderer.shadowCastingMode,
                            renderer.receiveShadows);
                }
            if (lightsOff)
                foreach (var light in _avatar.GetComponentsInChildren<Light>(true))
                {
                    if (light == null) continue;
                    var id = light.GetInstanceID();
                    if (!_lights.TryGetValue(id, out var saved) || saved.Light != light)
                        _lights[id] = (light, light.enabled);
                }
        }
        if (shadowsOff)
            foreach (var entry in _renderers.Values)
            {
                if (entry.Renderer == null) continue;
                if (entry.Renderer.shadowCastingMode != ShadowCastingMode.Off)
                    entry.Renderer.shadowCastingMode = ShadowCastingMode.Off;
                if (entry.Renderer.receiveShadows) entry.Renderer.receiveShadows = false;
            }
        if (lightsOff)
            foreach (var entry in _lights.Values)
                if (entry.Light != null && entry.Light.enabled) entry.Light.enabled = false;
    }

    /// <summary>
    /// 変更した影とライトの状態を元に戻し、対象と探索キャッシュを解除します。
    /// </summary>
    public void Restore()
    {
        RestoreRenderers();
        RestoreLights();
        _avatar = null;
        _shadowsOff = _lightsOff = false;
        _nextScan = 0f;
    }

    /// <summary>
    /// 保存したシャドウ設定を生存しているRendererへ戻し、保存一覧を空にします。
    /// </summary>
    private void RestoreRenderers()
    {
        foreach (var entry in _renderers.Values)
            try
            {
                if (entry.Renderer == null) continue;
                entry.Renderer.shadowCastingMode = entry.Casting;
                entry.Renderer.receiveShadows = entry.Receiving;
            }
            catch { /* Avatar may have been destroyed. */ }
        _renderers.Clear();
    }

    /// <summary>
    /// 保存した有効状態を生存しているLightへ戻し、保存一覧を空にします。
    /// </summary>
    private void RestoreLights()
    {
        foreach (var entry in _lights.Values)
            try
            {
                if (entry.Light != null) entry.Light.enabled = entry.Enabled;
            }
            catch { /* Avatar may have been destroyed. */ }
        _lights.Clear();
    }
}
