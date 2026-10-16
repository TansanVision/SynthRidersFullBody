using System.Numerics;
using System.Text.Json;
using SynthRidersFullBody;

/// <summary>距離と拡大後の操作範囲、設定の復元、フィードバックの入力元を検証します。</summary>
internal static class UiAppearanceTests
{
    /// <summary>近い・遠い・低い入口でも、固定した基準から操作可能なパネルを配置します。</summary>
    public static void Run(Action<bool, string> check)
    {
        var eye = new Vector3(0, 1.65f, 0);
        foreach (var distance in new[] { 1.1f, 1.4f, 1.8f })
        foreach (var large in new[] { false, true })
        foreach (var entryDistance in new[] { .6f, 1.4f, 4.5f })
        foreach (var height in new[] { .7f, 1.3f, 1.65f, 2f })
        foreach (var yaw in new[] { 0f, .7f, 2f })
        {
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
            var direction = Vector3.Transform(Vector3.UnitZ, rotation);
            var entry = eye + direction * entryDistance;
            entry.Y = height;
            var reference = new PanelPlacement(eye, entry, VrPanelLayout.EntryScale(Vector3.Distance(eye, entry)));
            var position = reference.Position(distance, large);
            var scale = VrPanelLayout.Scale(large);
            check(Math.Abs(Vector3.Dot(position - eye, direction) - distance) < .0001f, "Requested UI distance changed with yaw.");
            check(position.Y >= eye.Y - .8501f, "Moving a nearby low panel farther placed it below the user's feet.");
            var projectedY = eye.Y + (entry.Y - eye.Y) * distance / entryDistance;
            check(Math.Abs(position.Y - projectedY) >= 400 * scale + 130 * reference.EntryScale * distance / entryDistance + .119f,
                "Enlarged panel obscures the permanent entry controls.");
            foreach (var control in new[] { PanelControls.Close, PanelControls.Back, PanelControls.TextLarge, PanelControls.Farther })
            {
                var bounds = PanelControls.Bounds(control);
                var local = new Vector3(bounds.X, bounds.Y, 0);
                var world = position + Vector3.Transform(local * scale, rotation);
                var restored = Vector3.Transform(world - position, Quaternion.Inverse(rotation)) / scale;
                check(bounds.Contains(restored.X, restored.Y), "Scaling broke the displayed control / local hit-coordinate correspondence.");
                check(Vector3.Distance(eye, world) < 5f, "Close or size controls exceed the interaction range.");
            }
            // Repeated application uses the captured reference, not the current HMD.
            check(reference.Position(distance, large) == position, "Fixed placement accumulated a transform.");
        }

        var config = new ModConfig();
        foreach (var value in new[] { float.NaN, float.NegativeInfinity, float.PositiveInfinity, -10f, 80f, 1.6f })
        {
            config.UiPanelDistance = value;
            check(config.UiPanelDistance == VrPanelLayout.ClampDistance(value), "Saved and applied distance ranges disagree.");
            var json = JsonSerializer.Serialize(config);
            check(JsonSerializer.Deserialize<ModConfig>(json)!.UiPanelDistance == config.UiPanelDistance,
                "Normalized UI distance cannot be serialized or restored.");
        }
        var legacy = JsonSerializer.Deserialize<ModConfig>("{\"Enabled\":true}")!;
        check(!legacy.UiSound && legacy.UiHaptics && !legacy.UiLargeText && !legacy.UiTrackerDetails && legacy.UiPanelDistance == 1.4f,
            "Old config does not get the documented UI defaults.");

        var states = new PanelControlState[PanelControls.Count];
        PanelControls.Appearance(states, VrPanelLayout.MinDistance, true, false, true);
        check(!states[PanelControls.Nearer].Interactive && states[PanelControls.Farther].Interactive,
            "Distance lower bound still accepts a closer action.");
        check(!states[PanelControls.TextLarge].Interactive && states[PanelControls.TextLarge].Selected && states[PanelControls.TextStandard].Interactive,
            "Large size selection cannot return to Standard.");
        check(!states[3].Selected && states[4].Selected, "Sound and haptic switch states were mixed.");
        PanelControls.Appearance(states, VrPanelLayout.MaxDistance, false, true, false);
        check(!states[PanelControls.Farther].Interactive && states[PanelControls.Nearer].Interactive,
            "Distance upper bound still accepts a farther action.");
        foreach (var pages in new[] { 1, 2, 4 })
        for (var page = 0; page < pages; page++)
        {
            PanelControls.Paging(states, PanelControls.PagePrevious, PanelControls.PageNext, page, pages);
            check(states[PanelControls.PagePrevious].Interactive == (page > 0) &&
                states[PanelControls.PageNext].Interactive == (page + 1 < pages), "Paging wrapped around an endpoint.");
            if (pages == 1) check(states[PanelControls.PagePrevious].Kind == PanelControlKind.Hidden &&
                states[PanelControls.PageNext].Kind == PanelControlKind.Hidden, "One-page list still shows unusable paging buttons.");
        }

        check(PanelControls.Activation(new(true, false, false), 2, 7, -1) == new PanelActivation(2, UiHand.Left),
            "Feedback targeted an idle hand rather than the pressed pointer.");
        check(PanelControls.Activation(new(true, true, false), 2, 7, -1) == new PanelActivation(7, UiHand.Right),
            "Simultaneous presses emit more than one feedback target.");
        check(PanelControls.Activation(new(false, true, false), 2, -1, -1).Target == -1, "Off-panel press caused feedback.");
        check(PanelControls.Activation(new(false, false, true), 2, 7, 6) == new PanelActivation(6, UiHand.None),
            "Gaze operation vibrates an unrelated controller.");
        var gate = new PanelInputGate();
        gate.Update(false, false, false, true, true);
        var count = 0;
        for (var frame = 0; frame < 100; frame++)
        {
            var press = gate.Update(true, true, frame > 5, true, true);
            if (PanelControls.Activation(press, 3, 4, -1).Target < 0) continue;
            count++;
            gate.Block();
        }
        check(count == 1, "Holding a trigger or adding the other hand repeats confirmation feedback.");
    }
}
