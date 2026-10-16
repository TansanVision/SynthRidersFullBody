using Il2Cpp;
using Il2CppSynth.Utils;
using Il2CppTMPro;
using UnityEngine;

namespace SynthRidersFullBody;

/// <summary>ゲームの言語設定とフォントを読み、MODが所有する文字部品だけへ反映します。</summary>
internal static class UiLocalization
{
    /// <summary>MOD所有の文字、元の書体、固定文言の翻訳キーを保持します。</summary>
    private sealed record Label(TMP_Text Text, TMP_FontAsset? OriginalFont, string? Key);
    private static readonly List<Label> Labels = new();
    private static TMP_FontAsset? _localizedFont;
    private static float _nextPoll;
    private static float _nextFontScan;
    private static int _revision;
    private static bool _fontComplete;
    private static readonly HashSet<string> FontReports = new();

    /// <summary>言語またはフォントが変わった回数。文字をメッシュ化する表示の再生成に使用します。</summary>
    public static int Revision => _revision;

    /// <summary>ゲームの設定を定期的に確認し、既存の静的ラベルと書体を更新します。</summary>
    public static void Update()
    {
        if (Time.unscaledTime < _nextPoll) return;
        _nextPoll = Time.unscaledTime + 0.5f;
        // Read the locale string, never assume that the language menu's numeric order is fixed.
        var changed = UiText.SetLanguage(OptionalMember.ReadStatic(typeof(Game_InfoProvider), "CurrentLangString") as string);
        if (changed) { _localizedFont = null; _fontComplete = false; _nextFontScan = 0f; }
        if ((_localizedFont == null || !_fontComplete) && Time.unscaledTime >= _nextFontScan)
        {
            _nextFontScan = Time.unscaledTime + 5f;
            var previous = _localizedFont;
            _localizedFont = FindFont(UiText.Language);
            changed |= previous != _localizedFont;
        }
        if (changed) _revision++;
        for (var i = Labels.Count - 1; i >= 0; i--)
        {
            var label = Labels[i];
            if (label.Text == null) { Labels.RemoveAt(i); continue; }
            if (!changed) continue;
            ApplyFont(label.Text, label.OriginalFont);
            if (label.Key != null) label.Text.text = UiText.Get(label.Key);
        }
    }

    /// <summary>作成した文字部品を登録し、現在の言語の書体を適用します。</summary>
    public static void Register(TMP_Text text)
    {
        Labels.Add(new Label(text, text.font, null));
        ApplyFont(text, text.font);
    }

    /// <summary>固定文言を登録し、画面を作り直さず言語変更へ追従させます。</summary>
    public static void Bind(TMP_Text text, string english)
    {
        var index = Labels.FindIndex(label => label.Text == text);
        if (index < 0) { Register(text); index = Labels.Count - 1; }
        Labels[index] = Labels[index] with { Key = english };
        text.text = UiText.Get(english);
    }

    /// <summary>一時的な文字メッシュにも、現在の言語の書体を適用します。</summary>
    public static void ApplyFont(TMP_Text text, TMP_FontAsset? original)
    {
        var font = _localizedFont != null ? _localizedFont : original;
        if (font != null && text.font != font) text.font = font;
    }

    /// <summary>ゲームの指定書体を優先しつつ、翻訳全文と記号を確認して不足グリフを動的書体で補います。</summary>
    private static TMP_FontAsset? FindFont(string language)
    {
        var candidates = new List<TMP_FontAsset>();
        try
        {
            foreach (var pairs in Resources.FindObjectsOfTypeAll<LanguageFontsPairSO>())
            {
                if (pairs == null || pairs.LangFontPairs == null) continue;
                foreach (var pair in pairs.LangFontPairs)
                    if (pair != null && UiText.NormalizeLanguage(pair.Language.ToString()) == language && pair.Font != null)
                        candidates.Add(pair.Font);
            }
        }
        catch { /* A game update may change optional font metadata; inspect the loaded fonts instead. */ }
        try
        {
            foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                if (font != null && !candidates.Contains(font)) candidates.Add(font);
        }
        catch { /* Keep the original font and retry after the game finishes loading its assets. */ }
        var required = UiText.RequiredCharacters(language);
        TMP_FontAsset? best = null;
        var missing = required;
        _fontComplete = false;
        foreach (var font in candidates)
        {
            try
            {
                // Search fallback tables, but never add glyphs to game-owned font assets.
                var remaining = new string(required.Where(c => !font.HasCharacter(c, true, false)).ToArray());
                if (best == null || remaining.Length < missing.Length) { best = font; missing = remaining; }
                if (remaining.Length == 0) break;
            }
            catch { /* A destroyed or incompatible font must not block the rest of the candidates. */ }
        }
        if (missing.Length > 0)
        {
            var dynamicFont = UiFontFallback.Find(language, candidates, required);
            if (dynamicFont != null) { best = dynamicFont; missing = ""; }
        }
        _fontComplete = missing.Length == 0;
        var report = $"UI font: language={language}; font={best?.name ?? "original"}; " +
            $"required={required.Length}; missing={missing.Length}";
        if (FontReports.Add(report))
        {
            if (_fontComplete) MelonLoader.MelonLogger.Msg(report);
            else MelonLoader.MelonLogger.Warning(report + "; codepoints=" +
                string.Join(",", missing.Take(32).Select(c => $"U+{(int)c:X4}")));
        }
        return best;
    }

    /// <summary>MOD終了時に保持したUnityオブジェクト参照を解放します。ゲーム所有のフォントは破棄しません。</summary>
    public static void Clear()
    {
        Labels.Clear();
        UiFontFallback.Clear();
        FontReports.Clear();
        _localizedFont = null;
        _fontComplete = false;
        _nextPoll = _nextFontScan = 0f;
    }
}
