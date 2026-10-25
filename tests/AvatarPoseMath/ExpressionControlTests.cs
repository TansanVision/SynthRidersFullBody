using SynthRidersFullBody;

internal static class ExpressionControlTests
{
    /// <summary>保持・切り替え・同時操作・入力喪失・再接続・VRMプリセット互換性を検証します。</summary>
    public static void Run(Action<bool, string> check)
    {
        var controls = new ExpressionControls();
        var bindings = new Dictionary<string, ExpressionBinding>();
        var valid = new HashSet<string> { "preset:happy", "preset:sad" };
        string Step(int pressed, int available = 63, string source = "left|right", bool enabled = true) =>
            controls.Update(new(pressed, available, source), bindings, enabled, valid.Contains);
        foreach (var button in Enum.GetValues<ExpressionButton>())
        {
            var mask = 1 << (int)button;
            foreach (var toggle in new[] { false, true })
            {
                bindings.Clear();
                bindings[button.ToString()] = new() { Expression = "preset:happy", Toggle = toggle };
                controls.Reset();
                check(Step(mask) == "", "Held button fired on startup.");
                check(Step(0) == "", "Release selected an expression.");
                check(Step(mask) == "preset:happy", $"{button} did not activate its expression.");
                check(Step(mask) == "preset:happy", "Holding a toggle repeatedly switched it.");
                check(Step(0) == (toggle ? "preset:happy" : ""), "Hold/toggle release behavior is incorrect.");
                check(Step(mask) == (toggle ? "" : "preset:happy"), "Second press did not toggle/retrigger.");
                Step(0); Step(mask);
                check(Step(0, 63 & ~mask) == "", "Lost input left an expression active.");
                check(Step(mask) == "", "Held button fired after input recovery.");
                Step(0);
                check(Step(mask) == "preset:happy", "Released and re-pressed button could not recover.");
                check(Step(mask, source: "replacement") == "", "Replacement controller retained the old expression.");
                Step(0); Step(mask);
                check(Step(mask, enabled: false) == "", "Disabled expression control retained a selection.");
                check(Step(mask) == "", "Button held through calibration/menu re-triggered.");
            }
        }

        controls.Reset(); bindings.Clear();
        bindings["LeftPrimary"] = new() { Expression = "preset:happy", Toggle = true };
        bindings["RightPrimary"] = new() { Expression = "preset:sad" };
        bindings["RightGrip"] = new() { Expression = "$reset" };
        Step(0); Step(1); Step(0);
        check(Step(8) == "preset:sad", "Hold did not override a latched expression.");
        check(Step(0) == "preset:happy", "Latched expression did not resume after a temporary hold.");
        check(Step(32) == "", "Reset did not clear a latched expression.");
        Step(0); Step(8);
        check(Step(40) == "", "Reset did not clear a held expression.");
        check(Step(8) == "", "Still-held button reactivated after reset.");
        Step(0);
        check(Step(8) == "preset:sad", "Reset prevented subsequent intentional input.");

        controls.Reset();
        bindings["LeftPrimary"] = new() { Expression = "preset:happy" };
        Step(0); Step(1);
        check(Step(9) == "preset:sad", "Last pressed hold did not take priority.");
        check(Step(1) == "preset:happy", "Previous hold did not resume when last hold was released.");
        Step(0);
        bindings["LeftPrimary"] = new() { Expression = "custom:missing" };
        check(Step(1) == "", "Missing expression was selected on another avatar.");
        Step(0); bindings["LeftPrimary"] = null!;
        check(Step(1) == "", "Null binding was not ignored.");
        Step(0); bindings["LeftPrimary"] = new() { Expression = null! };
        check(Step(1) == "", "Null expression was not ignored.");

        controls.Reset(); bindings.Clear();
        bindings["RightPrimary"] = new() { Expression = "preset:happy" };
        string Hands(int pressed, int available, string left, string right) => controls.Update(
            new(pressed, available, left + "|" + right, left, right), bindings, true, valid.Contains);
        Hands(0, 63, "xr-left", "xr-right");
        check(Hands(8, 63, "xr-left", "xr-right") == "preset:happy", "Right button could not activate.");
        check(Hands(8, 56, "unavailable", "xr-right") == "preset:happy", "Losing left input reset a held right expression.");
        check(Hands(8, 63, "steam-left", "xr-right") == "preset:happy", "Left source fallback reset right input.");
        check(Hands(8, 63, "steam-left", "replacement-right") == "", "Replacement right input fired while held.");
        Hands(0, 63, "steam-left", "replacement-right");
        check(Hands(8, 63, "steam-left", "replacement-right") == "preset:happy", "Right replacement did not re-arm after release.");

        foreach (var (oldName, newName) in new[] { ("Joy", "happy"), ("Sorrow", "sad"),
                     ("Fun", "relaxed"), ("A", "aa"), ("I", "ih"), ("U", "ou"), ("E", "ee"),
                     ("O", "oh"), ("Blink_L", "blinkLeft"), ("Blink_R", "blinkRight"), ("Angry", "angry") })
            check(ExpressionControls.PresetId(oldName) == ExpressionControls.PresetId(newName),
                $"VRM 0/1 preset mismatch: {oldName} / {newName}.");
        check(ExpressionControls.PresetId("Happy") == "preset:happy", "Preset prefix was duplicated.");
    }
}
