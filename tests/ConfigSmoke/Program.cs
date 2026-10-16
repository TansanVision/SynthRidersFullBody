using SynthRidersFullBody;

var directory = Path.Combine(Directory.GetCurrentDirectory(), "tests", "ConfigSmoke", ".runtime",
    "SynthRidersFullBody-ConfigSmoke-" + Guid.NewGuid().ToString("N"));
var path = Path.Combine(directory, "config.json");
try
{
    var config = ModConfig.Load(path);
    Check(File.Exists(path), "Initial configuration was not created.");
    Check(config.CorrectionVersion == 4 && config.PelvisRotationWeight == 1f,
        "New configuration must prioritize the tracked waist rotation.");
    Check(!config.ShowCaptureCameraGuide && !config.DisableAvatarShadows &&
          !config.DisableAvatarLights, "Visual options must start disabled.");
    Check(!config.UltimateTrackerRotate180, "Ultimate rotation must be opt-in for existing aligned environments.");
    Check(!config.ExpressionsEnabled && config.ExpressionBindings.Count == 0,
        "Expression buttons must be opt-in and initially unassigned.");
    Check(!config.UiLargeText && config.UiPanelDistance == 1.4f && config.UiHaptics && !config.UiSound && !config.UiTrackerDetails,
        "UI defaults changed unexpectedly.");
    config.UiLargeText = true;
    config.UiPanelDistance = 1.7f;
    config.UiSound = true;
    config.UiHaptics = false;
    config.UiTrackerDetails = true;
    config.ExpressionsEnabled = true;
    config.ExpressionBindings["LeftPrimary"] = new() { Expression = "preset:happy" };
    config.ExpressionBindings["RightSecondary"] = new() { Expression = "custom:Smile", Toggle = true };
    config.WaistSerial = "waist-123";
    config.ShowCaptureCameraGuide = true;
    config.DisableAvatarShadows = true;
    config.DisableAvatarLights = true;
    config.UltimateTrackerRotate180 = true;
    config.AvatarVisibleInHeadset["file:example.vrm"] = false;
    config.Save(path);
    var reloaded = ModConfig.Load(path);
    Check(reloaded.UiLargeText && reloaded.UiPanelDistance == 1.7f && reloaded.UiSound && !reloaded.UiHaptics && reloaded.UiTrackerDetails,
        "Shared UI preferences did not survive saving and reloading.");
    Check(reloaded.ExpressionsEnabled && reloaded.ExpressionBindings["LeftPrimary"].Expression == "preset:happy" &&
        !reloaded.ExpressionBindings["LeftPrimary"].Toggle &&
        reloaded.ExpressionBindings["RightSecondary"] == config.ExpressionBindings["RightSecondary"],
        "Expression enable flag, custom ID or hold/toggle mode did not round trip.");
    Check(reloaded.WaistSerial == "waist-123" &&
          reloaded.ShowCaptureCameraGuide && reloaded.DisableAvatarShadows &&
          reloaded.DisableAvatarLights &&
          reloaded.UltimateTrackerRotate180 &&
          reloaded.AvatarVisibleInHeadset.TryGetValue("file:example.vrm", out var visible) &&
          !visible,
        "A saved tracker serial or visual option did not round trip.");
    Check(!File.Exists(path + ".tmp"), "Save left a temporary file behind.");

    File.WriteAllText(path,
        "{\"CorrectionVersion\":1,\"PelvisMaxTiltDegrees\":30," +
        "\"PelvisRotationWeight\":0.7,\"WaistSerial\":\"waist-123\"}");
    var migrated = ModConfig.Load(path);
    Check(!migrated.ExpressionsEnabled && migrated.ExpressionBindings.Count == 0,
        "Legacy configuration unexpectedly assigned controller buttons.");
    Check(!migrated.UltimateTrackerRotate180, "Legacy settings unexpectedly enabled Ultimate rotation.");
    Check(migrated.CorrectionVersion == 4 && migrated.PelvisMaxTiltDegrees == 20f &&
          migrated.PelvisRotationWeight == 1f && migrated.WaistSerial == "waist-123",
        "Configuration migration lost a tracker serial or a corrected default.");

    File.WriteAllText(path,
        "{\"CorrectionVersion\":2,\"PelvisPositionWeight\":0.8," +
        "\"WaistSerial\":\"waist-123\"}");
    migrated = ModConfig.Load(path);
    Check(migrated.CorrectionVersion == 4 && migrated.PelvisPositionWeight == 1f &&
          migrated.WaistSerial == "waist-123",
        "Version 2 pelvis position default was not updated.");
    File.WriteAllText(path,
        "{\"CorrectionVersion\":2,\"PelvisPositionWeight\":0.65}");
    migrated = ModConfig.Load(path);
    Check(migrated.PelvisPositionWeight == 0.65f,
        "A custom pelvis position weight was overwritten.");

    File.WriteAllText(path, "{\"CorrectionVersion\":3,\"PelvisRotationWeight\":0.35}");
    migrated = ModConfig.Load(path);
    Check(migrated.CorrectionVersion == 4 && migrated.PelvisRotationWeight == 1f,
        "Version 3 pelvis rotation default was not updated.");
    migrated.Save(path);
    Check(ModConfig.Load(path).PelvisRotationWeight == 1f, "Migration was not stable after saving.");
    foreach (var version in new[] { 3, 4 })
    {
        File.WriteAllText(path, $"{{\"CorrectionVersion\":{version},\"PelvisRotationWeight\":0.6}}");
        Check(ModConfig.Load(path).PelvisRotationWeight == 0.6f,
            "A custom pelvis rotation weight was overwritten.");
    }
    File.WriteAllText(path, "{\"CorrectionVersion\":4,\"PelvisRotationWeight\":0.35}");
    Check(ModConfig.Load(path).PelvisRotationWeight == 0.35f,
        "Migration was reapplied to a new-version custom value.");

    // Legacy serials migrate only when the new role dictionary is absent.
    File.WriteAllText(path, "{\"WaistSerial\":\"old-waist\",\"LeftFootSerial\":\"old-left\",\"RightFootSerial\":\"old-right\"}");
    var roles = ModConfig.Load(path);
    Check(roles.RoleBindings.Count == 3 && roles.RoleBindings["Waist"].Serial == "old-waist",
        "Legacy body roles did not migrate.");
    TrackerRoles.Assign(roles.RoleBindings, TrackerRole.Waist, null);
    roles.Save(path);
    Check(!ModConfig.Load(path).RoleBindings.ContainsKey("Waist"), "Cleared role was resurrected from legacy serials.");
    roles.RoleBindings.Clear();
    foreach (var role in TrackerRoles.All)
        TrackerRoles.Assign(roles.RoleBindings, role, new TrackerBinding { Source = "SteamVR", Serial = "serial-" + role, DeviceId = "8" });
    roles.Save(path);
    var allRoles = ModConfig.Load(path).RoleBindings;
    Check(allRoles.Count == 12 && !File.ReadAllText(path).Contains("DeviceId"), "Role persistence saved unstable runtime indices.");
    Check(allRoles["LeftToes"].Matches("SteamVR", "SERIAL-LeftToes", "14"), "Reconnected tracker with a different index was not resolved by serial.");
    Check(!allRoles["LeftToes"].Matches("Unity XR", "serial-LeftToes", "8"), "A binding ignored its provider.");
    var moved = allRoles["LeftToes"] with { Source = "Unity XR" };
    TrackerRoles.Assign(allRoles, TrackerRole.Head, moved);
    Check(!allRoles.ContainsKey("LeftToes") && allRoles["Head"].SameDevice(moved), "Device move left a duplicate physical role across providers.");
    allRoles["Waist"] = moved;
    Check(TrackerRoles.IsConflicted(allRoles, TrackerRole.Head) && TrackerRoles.IsConflicted(allRoles, TrackerRole.Waist) &&
        !TrackerRoles.IsConflicted(allRoles, TrackerRole.RightFoot), "Duplicate role conflict did not isolate only affected roles.");
    TrackerRoles.Assign(allRoles, TrackerRole.Head, moved);
    Check(!allRoles.ContainsKey("Waist") && !TrackerRoles.IsConflicted(allRoles, TrackerRole.Head), "Manual selection did not repair duplicate bindings.");
    roles.RoleBindings = allRoles;
    TrackerRoles.Assign(allRoles, TrackerRole.LeftWrist, new TrackerBinding { Source = "Unity XR", DeviceId = "5" });
    Check(allRoles["LeftWrist"].Matches("Unity XR", "", "5") && !allRoles["LeftWrist"].Matches("SteamVR", "", "5"),
        "Session device identity collided across providers.");
    roles.Save(path);
    Check(!ModConfig.Load(path).RoleBindings["LeftWrist"].Matches("Unity XR", "", "5"), "Session-only device reused a stale runtime index after relaunch.");
    File.WriteAllText(path, "{\"RoleBindings\":{\"Waist\":null,\"Head\":{\"Source\":null,\"Serial\":null}}}");
    var sanitized = ModConfig.Load(path);
    Check(!sanitized.RoleBindings.ContainsKey("Waist") && sanitized.RoleBindings["Head"].Serial == "", "Null role fields were not handled.");

    File.WriteAllText(path, "{\"ExpressionBindings\":null}");
    Check(ModConfig.Load(path).ExpressionBindings.Count == 0, "Null expression dictionary was not repaired.");
    File.WriteAllText(path, "{\"ExpressionBindings\":{\"LeftPrimary\":null,\"RightGrip\":{\"Expression\":null},\"999\":{},\"Unknown\":{}}}");
    var expressions = ModConfig.Load(path).ExpressionBindings;
    Check(expressions.Count == 1 && expressions["RightGrip"].Expression == "",
        "Null or invalid expression binding was not sanitized.");

    const string damaged = "{invalid json";
    File.WriteAllText(path, damaged);
    try
    {
        ModConfig.Load(path);
        throw new Exception("A damaged configuration was accepted.");
    }
    catch (System.Text.Json.JsonException) { }
    Check(File.ReadAllText(path) == damaged,
        "Loading a damaged configuration modified the original file.");
    Console.WriteLine("Config smoke test passed.");
}
finally
{
    if (Directory.Exists(directory)) Directory.Delete(directory, true);
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
