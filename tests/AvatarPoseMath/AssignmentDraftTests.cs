using System.Text.Json;
using SynthRidersFullBody;

/// <summary>実設定の保持、移動・解除の一括適用、切断後の誤割り当てを検証します。</summary>
internal static class AssignmentDraftTests
{
    /// <summary>現場の再接続・自動提案編集・キャンセル相当の破棄を再現します。</summary>
    public static void Run(Action<bool, string> check)
    {
        var registry = new AssignmentDevices();
        var a = Device("A", "1");
        var b = Device("B", "2");
        registry.Update(new[] { b, a });
        var selectedA = registry.Find(a.Key)!;
        var selectedB = registry.Find(b.Key)!;
        check(registry.Items.Select(d => d.Key).SequenceEqual(new[] { a.Key, b.Key }), "Device ordering depends on API enumeration.");
        registry.Update(new[] { a, b });
        check(registry.Contains(selectedA) && registry.Contains(selectedB), "Enumeration reordered connection identities.");

        var settings = new Dictionary<string, TrackerBinding> { ["Waist"] = selectedA.Binding };
        var before = JsonSerializer.Serialize(settings);
        var canceled = new AssignmentDraft(settings);
        canceled.Set(TrackerRole.LeftFoot, selectedA);
        check(JsonSerializer.Serialize(settings) == before, "Selecting a candidate changed real settings before confirmation.");
        check(canceled.ChangedRoles.Contains(TrackerRole.Waist) && canceled.ChangedRoles.Contains(TrackerRole.LeftFoot),
            "Moving a device must show both source removal and destination assignment.");
        check(!canceled.Proposed.ContainsKey("Waist") && canceled.Proposed["LeftFoot"].SameDevice(selectedA.Binding), "Move invented a swap.");
        check(new AssignmentDraft(settings).ChangedRoles.Count == 0, "Discarded selection leaked into a new draft.");
        check(canceled.Apply(settings, registry.Contains) == AssignmentApplyResult.Applied && !settings.ContainsKey("Waist"), "Confirmed move did not apply atomically.");
        check(canceled.Apply(settings, registry.Contains) == AssignmentApplyResult.Invalid, "Consumed move applied a second time.");

        var same = new AssignmentDraft(settings);
        same.Set(TrackerRole.LeftFoot, selectedA);
        check(!same.HasChanges && same.Apply(settings, registry.Contains) == AssignmentApplyResult.Unchanged, "Choosing current device needlessly invalidates calibration.");
        var clear = new AssignmentDraft(settings);
        clear.Set(TrackerRole.LeftFoot, null);
        check(settings.ContainsKey("LeftFoot"), "Clear changed configuration before confirmation.");
        check(clear.Apply(settings, registry.Contains) == AssignmentApplyResult.Applied && settings.Count == 0, "Clear was not applied after confirmation.");

        var proposal = new AssignmentDraft(settings);
        proposal.Set(TrackerRole.Waist, selectedA);
        proposal.Set(TrackerRole.LeftFoot, selectedB);
        var edited = proposal.Copy();
        edited.Set(TrackerRole.RightFoot, selectedB);
        check(settings.Count == 0 && proposal.Proposed.ContainsKey("LeftFoot") && !proposal.Proposed.ContainsKey("RightFoot"),
            "Editing a preview mutated real settings or its cancel snapshot.");
        registry.Update(new[] { a });
        check(edited.Apply(settings, registry.Contains) == AssignmentApplyResult.Invalid && settings.Count == 0,
            "Partial AUTO application occurred after one candidate disconnected.");
        registry.Update(new[] { a, b });
        check(!registry.Contains(selectedB) && edited.Apply(settings, registry.Contains) == AssignmentApplyResult.Invalid,
            "A reconnected candidate silently replaced the confirmed identity.");
        edited.Set(TrackerRole.RightFoot, registry.Find(b.Key));
        check(edited.Apply(settings, registry.Contains) == AssignmentApplyResult.Applied && settings.Count == 2,
            "Explicitly reselecting the reconnected device did not recover the draft.");

        var stale = new AssignmentDraft(settings);
        stale.Set(TrackerRole.Waist, null);
        settings.Remove("RightFoot");
        check(stale.Apply(settings, registry.Contains) == AssignmentApplyResult.Invalid && settings.ContainsKey("Waist"),
            "Stale draft overwrote independently changed assignments.");
        var lostPose = new AssignmentDraft(settings);
        lostPose.Set(TrackerRole.RightFoot, registry.Find(b.Key));
        check(lostPose.Apply(settings, _ => false) == AssignmentApplyResult.Invalid && settings.Count == 1,
            "Untracked device was applied despite failed validation.");

        var noSerial = Device("", "8");
        registry.Update(new[] { noSerial });
        var temporary = registry.Find(noSerial.Key)!;
        check(temporary.Binding.Matches("SteamVR", "", "8", temporary.Session), "Current session-only assignment cannot track.");
        registry.Update(Array.Empty<AssignmentDevice>());
        registry.Update(new[] { noSerial }); // Same model and runtime ID reused by another physical device.
        var replacement = registry.Find(noSerial.Key)!;
        check(!registry.Contains(temporary) && !temporary.Binding.Matches("SteamVR", "", "8", replacement.Session),
            "Session-only assignment followed a reused runtime ID after disconnect.");
        var json = JsonSerializer.Serialize(temporary.Binding);
        check(!json.Contains("DeviceId") && !json.Contains("SessionId"), "Temporary connection identity was persisted.");
        registry.Update(new[] { noSerial, noSerial });
        check(registry.Items.Count == 0 && !registry.Contains(replacement), "Duplicate identity was silently accepted.");
        registry.Update(new[] { a });
        var oldIndex = registry.Find(a.Key)!;
        registry.Update(new[] { a with { Binding = a.Binding with { DeviceId = "9" } } });
        check(!registry.Contains(oldIndex), "Changing the runtime endpoint retained an in-flight selection.");
        check(oldIndex.Binding.Matches("SteamVR", "A", "9", 999), "Saved serial assignments stopped restoring after reconnect.");
    }

    /// <summary>シリアルの有無を含む試験用の個体を作成します。</summary>
    private static AssignmentDevice Device(string serial, string id) =>
        new("SteamVR:" + (serial.Length > 0 ? serial : id + ":Tracker"), "Tracker",
            new TrackerBinding { Source = "SteamVR", Serial = serial, DeviceId = id });
}
