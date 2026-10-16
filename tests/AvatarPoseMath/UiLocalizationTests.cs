using System.Globalization;
using System.Text.RegularExpressions;
using SynthRidersFullBody;
using Il2CppTMPro;
using Il2CppSynth.Utils;
using UnityEngine;

internal static class UiLocalizationTests
{
    public static void Run(Action<bool, string> check)
    {
        check(UiText.Catalogs.Count == 6, "All six non-English catalogs must be embedded.");
        var englishKeys = UiText.Catalogs["ja"].Keys.ToHashSet();
        foreach (var (language, catalog) in UiText.Catalogs)
        {
            UiText.SetLanguage(language);
            check(englishKeys.SetEquals(catalog.Keys), $"Incomplete catalog: {language}");
            foreach (var (key, value) in catalog)
            {
                check(!string.IsNullOrWhiteSpace(value), $"Empty translation: {language}/{key}");
                var indices = Regex.Matches(key, @"\{(\d+)\}").Select(m => int.Parse(m.Groups[1].Value)).OrderBy(i => i).ToArray();
                var translatedIndices = Regex.Matches(value, @"\{(\d+)\}").Select(m => int.Parse(m.Groups[1].Value)).OrderBy(i => i).ToArray();
                check(indices.SequenceEqual(translatedIndices), $"Changed placeholders: {language}/{key}");
                var args = Enumerable.Range(0, indices.DefaultIfEmpty(-1).Max() + 1).Select(i => (object)$"[{i}]").ToArray();
                check(UiText.Format(key, args) == string.Format(CultureInfo.CurrentCulture, value, args), $"Bad format: {language}/{key}");
            }
            check(UiText.Status("T-pose: spread arms (0.42 m)") == UiText.Format("T-pose: spread arms ({0} m)", "0.42"), "Status template lost measurement.");
            check(UiText.Status("Calibration failed: Left ankle: assigned device is not tracked") ==
                UiText.Format("Calibration failed: {0}", UiText.Get("Left ankle") + ": " + UiText.Get("assigned device is not tracked")), "Nested calibration failure not translated.");
            check(UiText.Status("Waist: 0.14m 6deg -> target: 0.12m 5deg") ==
                UiText.Format("Waist: {0}m {1}deg -> target: {2}m {3}deg", "0.14", "6", "0.12", "5"), "Waist diagnostics changed values.");
            check(UiText.Status("Waist = LHR-ABCD (SteamVR #4) | No serial: this session only") ==
                UiText.Format("{0} = {1} | No serial: this session only", UiText.Get("Waist"), "LHR-ABCD (SteamVR #4)"), "Serial or role was mistranslated.");
            check(UiText.Status("Waiting for selected camera (Spectator; 2 candidates)") ==
                UiText.Format("Waiting for selected camera ({0}; {1} candidates)", UiText.Get("Spectator"), "2"), "Camera status not translated.");
            check(UiText.Status("Active: Head") == UiText.Format("Active: {0}", "Head"), "Custom expression name was translated.");
            check(UiText.Status("Spectator: Camera Head #36") == UiText.Get("Spectator") + ": Camera Head #36", "Camera identity was changed.");
            check(UiText.Status("MISSING LHR-123 (select device)") == UiText.Format("MISSING {0} (select device)", "LHR-123"), "Missing device hint untranslated.");
            check(UiText.Status("VRIK needs the VRM Left toes bone for dual foot tracking") ==
                UiText.Format("VRIK needs the VRM {0} bone for dual foot tracking", UiText.Get("Left toes")), "Additional-role failure untranslated.");
            check(UiText.Get("Unknown future message") == "Unknown future message", "Unknown message must remain readable.");
        }
        foreach (var (input, expected) in new[] { ("jp", "ja"), ("JA_jp", "ja"), ("日本語", "ja"), ("kr", "ko"),
            ("ko-KR", "ko"), ("ch", "zh"), ("zh-CN", "zh"), ("fr-FR", "fr"), ("de-DE", "de"), ("es-ES", "es"), ("en-US", "en"), ("unknown", "en") })
            check(UiText.NormalizeLanguage(input) == expected, $"Wrong locale: {input}");
        UiText.SetLanguage("jp");
        check(UiText.Get("CALIBRATE FULL BODY") == "キャリブレーション", "Use the game's Japanese calibration terminology.");
        check(!UiText.SetLanguage(null) && !UiText.SetLanguage("") && UiText.Language == "ja", "Temporary missing locale reset language.");
        UiText.SetLanguage("en");
        check(UiText.Status("Calibration complete") == "Calibration complete", "English fallback changed source.");
        CheckLiveSwitch(check);
        CheckMissingGlyphs(check);
    }

    private static void CheckLiveSwitch(Action<bool, string> check)
    {
        UiLocalization.Clear();
        Resources.All.Clear();
        Time.unscaledTime = 100;
        var original = new TMP_FontAsset("ABC");
        var fonts = Enum.GetValues<LanguageFontsPairSO.SupportedLanguageString>()
            .Select(l => new LanguageFontsPairSO.LanguageFontPair { Language = l,
                Font = new TMP_FontAsset(UiText.RequiredCharacters(UiText.NormalizeLanguage(l.ToString()))) }).ToArray();
        Resources.All.Add(new LanguageFontsPairSO { LangFontPairs = fonts });
        var label = new TMP_Text { font = original };
        var dynamicLabel = new TMP_Text { font = original, text = "device-name" };
        var gameLabel = new TMP_Text { font = original, text = "game-owned" };
        UiLocalization.Register(label);
        UiLocalization.Register(dynamicLabel);
        UiLocalization.Bind(label, "CALIBRATE FULL BODY");
        foreach (var font in fonts)
        {
            Il2Cpp.Game_InfoProvider.Language = font.Language.ToString();
            Time.unscaledTime += 1;
            UiLocalization.Update();
            check(label.text == UiText.Get("CALIBRATE FULL BODY"), "Existing entry button did not switch language.");
            check(label.font == font.Font && dynamicLabel.font == font.Font, "Game's language font was not applied.");
            check(dynamicLabel.text == "device-name", "Font update replaced dynamic metadata.");
            check(gameLabel.font == original && gameLabel.text == "game-owned", "Game UI was modified.");
        }
        var revision = UiLocalization.Revision;
        Il2Cpp.Game_InfoProvider.LanguageUnavailable = true;
        Time.unscaledTime += 1;
        UiLocalization.Update();
        check(UiText.Language == "ja" && UiLocalization.Revision == revision, "Missing accessor must preserve last language.");
        Il2Cpp.Game_InfoProvider.LanguageUnavailable = false;
        Resources.All.Clear();
        Il2Cpp.Game_InfoProvider.Language = "kr";
        Time.unscaledTime += 1;
        UiLocalization.Update();
        check(label.font == original, "Unloaded language font must preserve the original font.");
        var korean = new TMP_FontAsset(UiText.RequiredCharacters("ko"));
        Resources.All.Add(korean);
        Time.unscaledTime += 6;
        UiLocalization.Update();
        check(label.font == korean, "Font loaded after scene transition was not retried.");
        var meshLabel = new TMP_Text { font = original };
        UiLocalization.ApplyFont(meshLabel, original);
        check(meshLabel.font == korean, "Capture marker must use the language font too.");
        UiLocalization.Clear();
        Resources.All.Clear();
        UiText.SetLanguage("en");
    }

    /// <summary>短い日本語サンプルでは見逃していた欠字、フォールバック、遅延読み込みを検証します。</summary>
    private static void CheckMissingGlyphs(Action<bool, string> check)
    {
        UiLocalization.Clear();
        Resources.All.Clear();
        UiText.SetLanguage("en");
        var required = UiText.RequiredCharacters("ja");
        const string reported = "割遠＋姿勢矢投肘膝足首";
        check(reported.All(required.Contains) && required.Contains('…'), "Coverage omitted reported Japanese glyphs or ellipsis.");
        var incomplete = new TMP_FontAsset(new string(required.Where(c => !reported.Contains(c)).ToArray()));
        var complete = new TMP_FontAsset(required);
        Resources.All.Add(new LanguageFontsPairSO { LangFontPairs = new[] {
            new LanguageFontsPairSO.LanguageFontPair { Language = LanguageFontsPairSO.SupportedLanguageString.jp, Font = incomplete }
        }});
        Resources.All.Add(complete);
        var label = new TMP_Text { font = incomplete };
        var gameLabel = new TMP_Text { font = incomplete };
        UiLocalization.Register(label);
        Il2Cpp.Game_InfoProvider.Language = "jp";
        Time.unscaledTime += 6;
        UiLocalization.Update();
        check(label.font == complete, "A partial game Japanese atlas was accepted despite reported missing glyphs.");
        check(gameLabel.font == incomplete && !incomplete.HasCharacter('割'), "Font repair changed the game's atlas or UI.");

        // An unavailable OS font must not stop the UI; retry even though a partial font was found.
        UiLocalization.Clear();
        Resources.All.Remove(complete);
        label.font = incomplete;
        UiLocalization.Register(label);
        Time.unscaledTime += 6;
        UiLocalization.Update();
        check(label.font == incomplete, "No full font should retain the best available loaded font.");
        UiFontFallback.Available = complete;
        var before = UiLocalization.Revision;
        Time.unscaledTime += 6;
        UiLocalization.Update();
        check(label.font == complete && UiLocalization.Revision > before, "Partial font never retried the dynamic font fallback.");
        check(UiFontFallback.LastRequired == required, "Dynamic atlas did not receive the full catalog.");

        // Glyphs already in the game's fallback chain are sufficient without creating a new asset.
        UiLocalization.Clear();
        incomplete.Fallback = complete;
        UiLocalization.Register(label);
        Time.unscaledTime += 6;
        UiLocalization.Update();
        check(label.font == incomplete, "Existing complete fallback chain was ignored.");
        UiLocalization.Clear();
        Resources.All.Clear();
        UiText.SetLanguage("en");
    }
}

namespace Il2CppTMPro
{
    public sealed class TMP_FontAsset
    {
        private readonly string _characters;
        public string name = "test font";
        public TMP_FontAsset? Fallback;
        public TMP_FontAsset(string characters) => _characters = characters;
        public bool HasCharacter(char character, bool searchFallbacks = false, bool tryAddCharacter = false) =>
            _characters.Contains(character) || (searchFallbacks && Fallback?.HasCharacter(character) == true);
    }
    public sealed class TMP_Text
    {
        public TMP_FontAsset? font;
        public string text = "";
    }
}

namespace SynthRidersFullBody
{
    // Native font rasterization is tested in game; this boundary simulates its availability.
    internal static class UiFontFallback
    {
        public static TMP_FontAsset? Available;
        public static string? LastRequired;
        public static TMP_FontAsset? Find(string language, IEnumerable<TMP_FontAsset> candidates, string required)
        { LastRequired = required; return Available; }
        public static void Clear() { Available = null; LastRequired = null; }
    }
}

namespace MelonLoader
{
    public static class MelonLogger
    {
        public static void Msg(string text) { }
        public static void Warning(string text) { }
    }
}

namespace Il2CppSynth.Utils
{
    public sealed class LanguageFontsPairSO
    {
        public enum SupportedLanguageString { en, kr, fr, de, ch, es, jp }
        public sealed class LanguageFontPair
        {
            public SupportedLanguageString Language;
            public TMP_FontAsset? Font;
        }
        public LanguageFontPair[]? LangFontPairs;
    }
}
