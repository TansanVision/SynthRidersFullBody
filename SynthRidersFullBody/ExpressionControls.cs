namespace SynthRidersFullBody;

/// <summary>表情を割り当てられるコントローラーのボタンです。値は入力ビットの位置と対応します。</summary>
public enum ExpressionButton
{
    /// <summary>左手の主ボタン。QuestではXです。</summary>
    LeftPrimary,
    /// <summary>左手の副ボタン。QuestではYです。</summary>
    LeftSecondary,
    /// <summary>左手のグリップボタン。</summary>
    LeftGrip,
    /// <summary>右手の主ボタン。QuestではAです。</summary>
    RightPrimary,
    /// <summary>右手の副ボタン。QuestではBです。</summary>
    RightSecondary,
    /// <summary>右手のグリップボタン。</summary>
    RightGrip
}

/// <summary>1個のボタンに割り当てる表情と押下動作です。</summary>
public sealed record ExpressionBinding
{
    /// <summary>表情の識別子。空文字列は未割り当て、$resetはMODの表情を解除します。</summary>
    public string Expression { get; init; } = "";
    /// <summary>trueなら押すたびに切り替え、falseなら押している間だけ反映します。</summary>
    public bool Toggle { get; init; }
}

/// <summary>読み取れたボタン、押下中のボタン、入力元の識別子です。</summary>
internal readonly record struct ExpressionInput(int Pressed, int Available, string Source);

/// <summary>アバターが公開している表情の保存用IDと表示名です。</summary>
internal readonly record struct ExpressionChoice(string Id, string Label);

/// <summary>押下境界と追跡復帰を管理し、同時入力では最後に押した表情を選びます。</summary>
internal sealed class ExpressionControls
{
    private static readonly ExpressionButton[] Buttons = Enum.GetValues<ExpressionButton>();
    private int _previous;
    private int _armed;
    private string? _source;
    private readonly List<ExpressionButton> _held = new();
    private ExpressionButton? _latched;

    /// <summary>シーン変更・設定変更時に表情と押下履歴を破棄し、ボタンを離すまで再発火を防ぎます。</summary>
    public void Reset()
    {
        _previous = _armed = 0;
        _source = null;
        _held.Clear();
        _latched = null;
    }

    /// <summary>今回の入力から、適用する表情IDまたは未選択を表す空文字列を返します。</summary>
    /// <param name="input">左右コントローラーの同一フレームの入力。</param>
    /// <param name="bindings">ボタン名をキーとする共有設定。</param>
    /// <param name="enabled">現在の画面とアバターで表情操作を許可するか。</param>
    /// <param name="exists">現在のアバターに表情があるかを確認する関数。</param>
    public string Update(ExpressionInput input, IReadOnlyDictionary<string, ExpressionBinding> bindings,
        bool enabled, Func<string, bool> exists)
    {
        if (!enabled) { Reset(); return ""; }
        if (_source != input.Source) { Reset(); _source = input.Source; }
        // A button must first be observed released on a valid device. This also
        // prevents a held button from firing after calibration or reconnect.
        _armed = (_armed | (input.Available & ~input.Pressed)) & input.Available;
        var pressed = input.Pressed & input.Available & _armed;
        var rising = pressed & ~_previous;
        _previous = pressed;
        _held.RemoveAll(b => (pressed & (1 << (int)b)) == 0);
        if (_latched is { } old && (input.Available & (1 << (int)old)) == 0) _latched = null;
        foreach (var button in Buttons)
        {
            if ((rising & (1 << (int)button)) == 0 ||
                !bindings.TryGetValue(button.ToString(), out var binding) || binding == null) continue;
            if (binding.Expression == "$reset") { _latched = null; _held.Clear(); break; }
            if (string.IsNullOrEmpty(binding.Expression) || !exists(binding.Expression)) continue;
            if (binding.Toggle) _latched = _latched == button ? null : button;
            else { _held.Remove(button); _held.Add(button); }
        }
        var selected = _held.Count > 0 ? _held[^1] : _latched;
        return selected is { } key && bindings.TryGetValue(key.ToString(), out var value) &&
            value != null && !string.IsNullOrEmpty(value.Expression) && exists(value.Expression) ? value.Expression : "";
    }

    /// <summary>VRM 0.xと1.0の標準プリセット名を同じ保存用IDへ対応付けます。</summary>
    public static string PresetId(string preset) => ("preset:" + preset.ToLowerInvariant()) switch
    {
        "preset:joy" => "preset:happy", "preset:sorrow" => "preset:sad",
        "preset:fun" => "preset:relaxed", "preset:a" => "preset:aa",
        "preset:i" => "preset:ih", "preset:u" => "preset:ou",
        "preset:e" => "preset:ee", "preset:o" => "preset:oh",
        "preset:blink_l" => "preset:blinkleft", "preset:blink_r" => "preset:blinkright",
        var id => id
    };

    /// <summary>機種によって異なるラベルを併記したボタン名を返します。</summary>
    public static string Label(ExpressionButton button) => button switch
    {
        ExpressionButton.LeftPrimary => "Left primary (X / pad)",
        ExpressionButton.LeftSecondary => "Left secondary (Y / menu)",
        ExpressionButton.LeftGrip => "Left grip",
        ExpressionButton.RightPrimary => "Right primary (A / pad)",
        ExpressionButton.RightSecondary => "Right secondary (B / menu)",
        _ => "Right grip"
    };
}
