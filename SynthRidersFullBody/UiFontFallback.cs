using Il2CppTMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace SynthRidersFullBody;

/// <summary>不足グリフを描画できるMOD専用の動的フォントを作成します。ゲームのアトラスは変更しません。</summary>
internal static class UiFontFallback
{
    /// <summary>専用アトラスと、MODが作った場合だけ破棄する元のOS書体を保持します。</summary>
    private sealed record OwnedFont(TMP_FontAsset Asset, Font? Source);
    private static readonly Dictionary<string, OwnedFont> Owned = new();

    /// <summary>ゲームの元フォント、次にインストール済みOSフォントから全文言を描画できる書体を探します。</summary>
    public static TMP_FontAsset? Find(string language, IEnumerable<TMP_FontAsset> candidates, string required)
    {
        if (Owned.TryGetValue(language, out var cached) && cached.Asset != null) return cached.Asset;
        var seen = new HashSet<int>();
        foreach (var candidate in candidates)
        {
            try
            {
                var source = candidate.sourceFontFile;
                if (source == null || !seen.Add(source.GetInstanceID())) continue;
                var asset = Create(source, required);
                if (asset == null) continue;
                Owned[language] = new(asset, null);
                return asset;
            }
            catch { /* Some static game atlases omit the source font entirely. */ }
        }
        try
        {
            var installed = Font.GetOSInstalledFontNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var name in Families(language))
            {
                // A nonexistent family may silently resolve to a Latin-only default font.
                if (!installed.Contains(name)) continue;
                Font? source = null;
                try
                {
                    source = Font.CreateDynamicFontFromOSFont(name, 64);
                    var asset = Create(source, required);
                    if (asset == null) continue;
                    source.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    Owned[language] = new(asset, source);
                    source = null; // Ownership moves to the language cache.
                    return asset;
                }
                catch { /* Try the next installed family if this one cannot be opened by Unity. */ }
                finally { if (source != null) UnityEngine.Object.Destroy(source); }
            }
        }
        catch { /* Leave the best loaded font in use and report missing glyphs in UiLocalization. */ }
        return null;
    }

    /// <summary>対応するOS書体の候補名を返します。フォントファイルのコピーや配布は行いません。</summary>
    private static string[] Families(string language) => language switch
    {
        "ja" => new[] { "Yu Gothic", "Meiryo", "MS Gothic", "Noto Sans CJK JP", "Noto Sans JP" },
        "ko" => new[] { "Malgun Gothic", "Gulim", "Noto Sans CJK KR", "Noto Sans KR" },
        "zh" => new[] { "Microsoft YaHei", "SimHei", "SimSun", "Noto Sans CJK SC", "Noto Sans SC" },
        _ => new[] { "Segoe UI", "Arial", "Noto Sans" }
    };

    /// <summary>元フォントから独立したSDFアトラスを作り、必要な全文字が追加できた場合だけ採用します。</summary>
    private static TMP_FontAsset? Create(Font source, string required)
    {
        TMP_FontAsset? asset = null;
        try
        {
            // Cheap coverage check avoids allocating an atlas for every Latin font in the game.
            if (source == null || required.Any(c => !source.HasCharacter(c))) return null;
            asset = TMP_FontAsset.CreateFontAsset(source, 64, 6, GlyphRenderMode.SDFAA,
                2048, 2048, AtlasPopulationMode.Dynamic, true);
            if (asset == null || !asset.TryAddCharacters(required, out var missing, false) ||
                !string.IsNullOrEmpty(missing)) return null;
            asset.name = "SRFB UI " + source.name;
            asset.hideFlags = HideFlags.DontUnloadUnusedAsset;
            if (asset.material != null) asset.material.hideFlags = HideFlags.DontUnloadUnusedAsset;
            foreach (var texture in asset.atlasTextures)
                if (texture != null) texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
            var result = asset;
            asset = null;
            return result;
        }
        catch { return null; }
        finally { Destroy(asset); }
    }

    /// <summary>MODが作った書体と専用マテリアル・アトラスだけを解放します。</summary>
    private static void Destroy(TMP_FontAsset? asset)
    {
        if (asset == null) return;
        if (asset.atlasTextures != null)
            foreach (var texture in asset.atlasTextures)
                if (texture != null) UnityEngine.Object.Destroy(texture);
        if (asset.material != null) UnityEngine.Object.Destroy(asset.material);
        UnityEngine.Object.Destroy(asset);
    }

    /// <summary>終了時に動的フォントを解放します。ゲーム所有の元フォントは破棄しません。</summary>
    public static void Clear()
    {
        foreach (var font in Owned.Values)
        {
            try { Destroy(font.Asset); }
            catch { /* Unity may already be tearing down font resources on shutdown. */ }
            try { if (font.Source != null) UnityEngine.Object.Destroy(font.Source); }
            catch { /* Continue releasing the other cached languages. */ }
        }
        Owned.Clear();
    }
}
