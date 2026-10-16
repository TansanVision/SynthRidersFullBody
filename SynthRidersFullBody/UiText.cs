using System.Globalization;
using System.Text.RegularExpressions;

namespace SynthRidersFullBody;

/// <summary>ゲームの7言語に対応するUI文言です。保存キー・機器名・ログの原文は変更しません。</summary>
internal static class UiText
{
    /// <summary>現在の表示言語の標準コードです。未対応の言語ではenを使用します。</summary>
    public static string Language { get; private set; } = "en";

    /// <summary>ゲーム独自のjp/kr/chを含む言語コードと、標準的なロケール名を正規化します。</summary>
    internal static string NormalizeLanguage(string? language)
    {
        var code = language?.Trim().Replace('_', '-').ToLowerInvariant();
        return code?.Split('-')[0] switch
        {
            "jp" or "ja" or "japanese" or "日本語" => "ja",
            "fr" or "french" or "français" => "fr",
            "de" or "german" or "deutsch" => "de",
            "es" or "spanish" or "español" => "es",
            "kr" or "ko" or "korean" or "한국어" => "ko",
            "ch" or "zh" or "chinese" or "简体中文" or "简体字" or "中文" => "zh",
            _ => "en"
        };
    }

    /// <summary>ゲームの言語を反映します。起動・シーン移動中の未取得では直前の表示言語を保持します。</summary>
    public static bool SetLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return false;
        var selected = NormalizeLanguage(language);
        if (selected == Language) return false;
        Language = selected;
        return true;
    }

    /// <summary>固定文言を取得します。未登録の文言は原文を表示します。</summary>
    public static string Get(string english) => Catalogs.TryGetValue(Language, out var catalog) &&
        catalog.TryGetValue(english, out var text) ? text : english;

    /// <summary>翻訳した書式へ値を挿入します。機器名・シリアル・モデル独自の表情名などの引数は翻訳しません。</summary>
    public static string Format(string english, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(english), args);

    /// <summary>有効・無効を表示言語に合わせます。</summary>
    public static string OnOff(bool enabled) => Get(enabled ? "ON" : "OFF");

    /// <summary>翻訳全文・英語の代替文言・数値・省略記号に必要な文字を重複なく返します。</summary>
    internal static string RequiredCharacters(string language)
    {
        var text = new string(Enumerable.Range(32, 95).Select(c => (char)c).ToArray()) + "…";
        if (Catalogs.TryGetValue(language, out var catalog))
            text += string.Concat(catalog.Keys) + string.Concat(catalog.Values);
        return new string(text.Where(c => !char.IsControl(c)).Distinct().OrderBy(c => c).ToArray());
    }

    /// <summary>診断処理が保持する英語の状態文を、表示するときだけ翻訳します。</summary>
    public static string Status(string english)
    {
        if (Language == "en" || english.Length == 0) return english;
        if (Catalogs.TryGetValue(Language, out var catalog) && catalog.TryGetValue(english, out var text)) return text;
        const string failed = "Calibration failed: ";
        if (english.StartsWith(failed, StringComparison.Ordinal))
            return Format("Calibration failed: {0}", Status(english[failed.Length..]));
        foreach (var rule in StatusRules)
        {
            var match = rule.Pattern.Match(english);
            if (!match.Success) continue;
            var args = match.Groups.Cast<Group>().Skip(1).Select(g => (object)g.Value).ToArray();
            foreach (var index in rule.TranslatedArguments) args[index] = Get((string)args[index]);
            return Format(rule.Format, args);
        }
        // Only known role/mode prefixes are translated. The device identity or native error stays intact.
        foreach (var prefix in NamedPrefixes)
            if (english.StartsWith(prefix + ": ", StringComparison.Ordinal))
                return Get(prefix) + ": " + Status(english[(prefix.Length + 2)..]);
        return english;
    }

    /// <summary>原文の書式と、その引数のうち部位・状態名として翻訳する位置を保持します。</summary>
    private sealed record StatusRule(Regex Pattern, string Format, int[] TranslatedArguments);

    /// <summary>既知の状態文だけに一致するパターンを生成します。値は文字列のまま保持します。</summary>
    private static StatusRule Rule(string format, params int[] translatedArguments)
    {
        var escaped = Regex.Escape(format);
        var pattern = Regex.Replace(escaped, @"\\\{\d+}", "(.+?)");
        return new(new Regex("\\A" + pattern + "\\z", RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(50)), format, translatedArguments);
    }

    private static readonly string[] NamedPrefixes =
    {
        "Waist", "Head", "Left ankle", "Right ankle", "Left toes", "Right toes", "Left wrist", "Right wrist",
        "Left elbow", "Right elbow", "Left knee", "Right knee", "ThirdPerson", "ThirdPersonSwing",
        "ThirdPersonCustom", "Spectator", "Cinematic", "Third-person", "Custom", "Screen camera"
    };

    private static readonly StatusRule[] StatusRules =
    {
        Rule("T-pose: spread arms ({0} m)"), Rule("T-pose: keep hands level ({0} m)"),
        Rule("Waist is only {0} m above feet: check tracker choice"), Rule("Waist is {0} m above feet"),
        Rule("Body IK: waist | pelvis error {0} m"),
        Rule("Waist: {0}m {1}deg -> target: {2}m {3}deg"),
        Rule("{0} cleared", 0), Rule("{0} = {1} | No serial: this session only", 0), Rule("{0} = {1}", 0),
        Rule("AUTO filled {0} empty roles. Check each device; correct manually."),
        Rule("MISSING {0} (select device)"),
        Rule("{0}: tracking changed during calibration", 0), Rule("{0} moved; hold still and retry", 0),
        Rule("{0} expressions available"), Rule("{0} expressions | {1}", 1), Rule("Active: {0}"),
        Rule("Cyan sphere: {0}, {1} m", 0), Rule("Waiting for {0} camera", 0),
        Rule("Waiting for selected camera ({0}; {1} candidates)", 0),
        Rule("VRIK target setup failed: {0}"), Rule("VRIK root controller unavailable: {0}"),
        Rule("VRM has no bone for {0}", 0), Rule("VRIK needs the VRM {0} bone for dual foot tracking", 0)
    };

    /// <summary>DLLへ埋め込んだ翻訳を読み込みます。外部ファイルの配置は不要です。</summary>
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> LoadCatalogs()
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        foreach (var language in new[] { "ja", "fr", "de", "es", "ko", "zh" })
        {
            using var stream = typeof(UiText).Assembly.GetManifestResourceStream(
                $"SynthRidersFullBody.Localization.{language}.json");
            if (stream == null) continue;
            result[language] = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                ?? new Dictionary<string, string>();
        }
        return result;
    }

    /// <summary>各言語の翻訳一覧。テストでキーと書式引数の整合性も検証します。</summary>
    internal static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Catalogs = LoadCatalogs();
}
