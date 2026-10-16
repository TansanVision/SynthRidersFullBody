using SynthRidersFullBody;

/// <summary>VR実行環境に依存しない、操作の持ち越し・当たり判定・画面移動の回帰検証です。</summary>
internal static class PanelInteractionTests
{
    /// <summary>画面の誤閉鎖、別の手による誤操作、戻り先の消失を検証します。</summary>
    public static void Run(Action<bool, string> check)
    {
        var gate = new PanelInputGate();
        check(!gate.Update(true, true, false, true, true).Any, "Opening press must be consumed.");
        gate.Update(false, false, false, true, true);
        check(gate.Armed, "Releasing both triggers must rearm the panel.");
        var left = gate.Update(true, true, false, true, true);
        check(left.Left && !left.Right && !left.Gaze, "Left trigger activated another input source.");
        // A held press that started over blank space must never turn into a Close press.
        for (var frame = 0; frame < 240; frame++)
            check(!gate.Update(true, true, false, true, true).Any, "Holding/moving onto Close generated a new press.");
        gate.Block();
        check(!gate.Update(true, true, true, true, true).Any, "Second hand leaked through a page transition.");
        gate.Update(true, false, true, true, true);
        check(!gate.Armed, "One hand still held must keep the close/reopen gate blocked.");
        gate.Update(false, false, false, true, true);
        check(gate.Update(true, false, true, true, true).Right, "Right trigger did not work after release.");
        gate.Block();
        gate.Update(false, false, false, false, false);
        check(gate.Update(true, false, false, false, false).Gaze, "Gaze fallback cannot explicitly close.");
        gate.Block();
        check(!gate.Update(true, true, false, true, false).Any, "Acquiring a controller pose created a held press.");
        // Opening again while held must also require release.
        gate.Block();
        check(!gate.Update(true, true, false, true, false).Any && !gate.Armed, "Reopening reused the previous press.");
        gate.Update(false, false, false, true, false);
        check(gate.Update(true, true, false, true, false).Left, "Reopened panel stayed unresponsive.");

        check(PanelControls.PressedTarget(new(true, false, false), 0, PanelControls.Close, -1) == 0,
            "The idle right hand stole the left hand's click.");
        check(PanelControls.PressedTarget(new(false, true, false), PanelControls.Close, -1, -1) == -1,
            "A press outside the panel closed through the idle hand.");
        check(PanelControls.PressedTarget(new(true, true, false), 0, PanelControls.Close, -1) == PanelControls.Close,
            "Simultaneous triggers must choose a single action deterministically.");
        check(PanelControls.PressedTarget(new(true, true, false), 0, -1, -1) == 0,
            "The valid left click was lost when the other hand missed.");
        check(PanelControls.PressedTarget(new(false, false, true), -1, -1, PanelControls.Close) == PanelControls.Close,
            "Gaze fallback lost the close target.");

        var states = new PanelControlState[PanelControls.Count];
        foreach (var kind in Enum.GetValues<PanelControlKind>())
        foreach (var enabled in new[] { false, true })
        {
            states[0] = new(kind, enabled);
            var bounds = PanelControls.Bounds(0);
            var expected = enabled && kind is not (PanelControlKind.Hidden or PanelControlKind.Information) ? 0 : -1;
            check(PanelControls.Hit(states, bounds.X, bounds.Y) == expected, "Information/disabled/hidden row is clickable.");
            check(PanelControls.Hit(states, bounds.X + bounds.Width / 2 + 1, bounds.Y) == -1, "Hit area exceeds the visible background.");
        }
        // The row variants are mutually exclusive. Verify every actual layout,
        // including the new size and distance pairs, against shared hit geometry.
        foreach (var profile in new[] { "normal", "pose", "pages", "appearance" })
        {
            Array.Clear(states);
            for (var i = 0; i <= PanelControls.DashboardNext; i++) states[i] = new(PanelControlKind.Navigation);
            states[PanelControls.DashboardPrevious] = new(PanelControlKind.Navigation);
            states[PanelControls.DashboardDetails] = new(PanelControlKind.Toggle);
            if (profile == "pose")
            {
                states[1] = default;
                states[PanelControls.TPose] = states[PanelControls.Standing] = new(PanelControlKind.Choice);
            }
            if (profile == "pages")
            {
                states[4] = default;
                PanelControls.Paging(states, PanelControls.PagePrevious, PanelControls.PageNext, 1, 3);
            }
            if (profile == "appearance") PanelControls.Appearance(states, 1.4f, false, true, false);
            for (var i = 0; i < states.Length; i++)
            {
                if (states[i].Kind == PanelControlKind.Hidden) continue;
                var bounds = PanelControls.Bounds(i);
                check(MathF.Abs(bounds.X) + bounds.Width / 2 <= 800 && MathF.Abs(bounds.Y) + bounds.Height / 2 <= 400,
                    "A control is outside the panel's ray/occlusion bounds.");
                check(PanelControls.Hit(states, bounds.X, bounds.Y) == (states[i].Interactive ? i : -1),
                    $"Overlapping or unreachable control: {profile}/{i}.");
                for (var j = i + 1; j < states.Length; j++)
                {
                    if (states[j].Kind == PanelControlKind.Hidden) continue;
                    var other = PanelControls.Bounds(j);
                    check(MathF.Abs(bounds.X - other.X) >= (bounds.Width + other.Width) / 2 ||
                        MathF.Abs(bounds.Y - other.Y) >= (bounds.Height + other.Height) / 2,
                        "Two simultaneously visible controls overlap.");
                }
            }
            if (profile is "pose" or "appearance")
                check(PanelControls.Hit(states, -340, 80) == -1, "The gap between paired choices is clickable.");
            check(PanelControls.Hit(states, float.NaN, 350) == -1, "NaN ray accepted.");
            check(PanelControls.Hit(states, 0, float.PositiveInfinity) == -1, "Infinite ray accepted.");
        }

        foreach (var calibration in new[] { false, true })
        foreach (var page in new[] { PanelPage.Trackers, PanelPage.Display, PanelPage.Expressions })
        {
            var navigation = new PanelNavigation();
            navigation.Open(calibration);
            navigation.SelectTab(page);
            navigation.SelectTab(PanelPage.Display);
            navigation.SelectTab(PanelPage.Trackers);
            check(navigation.CanGoBack, "Subpage has no Back action.");
            navigation.Back();
            check(navigation.Current == (calibration ? PanelPage.Calibration : PanelPage.Details),
                "Switching tabs changed the parent screen.");
            navigation.Back();
            check(navigation.Current == PanelPage.Details && !navigation.CanGoBack, "Back did not stop at the settings root.");
            navigation.Open(true);
            navigation.SelectTab(page);
            navigation.SelectTab(PanelPage.Details);
            check(!navigation.CanGoBack, "Details tab must reset to the settings root.");
            navigation.Open(false);
            check(navigation.Current == PanelPage.Details, "Reopening retained an old subpage.");
        }
    }
}
