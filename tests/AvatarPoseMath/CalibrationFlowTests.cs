using SynthRidersFullBody;

/// <summary>計測・キャンセル・再試行の状態と、同じ状態から作る操作領域の回帰検証です。</summary>
internal static class CalibrationFlowTests
{
    /// <summary>未準備の開始、連打、キャンセル後の結果、成功後の再固定を防ぐことを確認します。</summary>
    public static void Run(Action<bool, string> check)
    {
        var ready = new CalibrationReadiness(true, CalibrationDeviceState.Ready, CalibrationDeviceState.Ready,
            CalibrationDeviceState.Ready, true, null);
        check(ready.Ready, "Assigned and tracked body roles should be ready without repeating assignments.");
        check(!(ready with { AvatarReady = false }).Ready, "Missing avatar can start.");
        check(!(ready with { Waist = CalibrationDeviceState.Unassigned }).Ready, "Unassigned waist can start.");
        check(!(ready with { Left = CalibrationDeviceState.Lost }).Ready, "Lost primary foot can start.");
        check(!(ready with { Right = CalibrationDeviceState.Conflict }).Ready, "Conflicting foot can start.");
        check(!(ready with { WorldPosesReady = false }).Ready, "Invalid world poses can start.");
        check(!(ready with { AdditionalIssue = "Left toes: assigned device is not tracked" }).Ready,
            "Dual-foot/additional role failure was ignored.");
        check(CalibrationReadiness.Device(false, false, false) == CalibrationDeviceState.Unassigned,
            "An unassigned foot must not be labeled tracking lost.");
        check(CalibrationReadiness.Device(true, false, false) == CalibrationDeviceState.Lost,
            "An assigned but disconnected foot must not be labeled unassigned.");
        check(CalibrationReadiness.Device(true, true, true) == CalibrationDeviceState.Conflict,
            "Conflicting assignment must not appear ready even with a stale pose.");
        check(CalibrationReadiness.Device(true, true, false) == CalibrationDeviceState.Ready, "Valid role not ready.");

        foreach (var tPose in new[] { false, true })
        foreach (var succeeded in new[] { false, true })
        {
            var flow = new CalibrationFlow();
            check(!flow.Preview && !flow.CountingDown && flow.Stage == CalibrationStage.Preparation,
                "Opening preparation must not freeze the avatar.");
            check(!flow.Start(true) && !flow.Complete("stale", true, null), "Countdown can skip pose selection.");
            check(!flow.SelectPose(false), "Missing devices allowed pose selection.");
            VerifyControls(flow, false, tPose, new[] { 5 }, check);
            VerifyControls(flow, true, tPose, new[] { 0, 5 }, check);
            check(flow.SelectPose(ready.Ready) && flow.Preview && !flow.CountingDown, "Pose selection did not start preview.");
            check(!flow.SelectPose(true), "Repeated Continue restarted preview.");
            VerifyControls(flow, true, tPose, new[] { 0, 5, PanelControls.TPose, PanelControls.Standing }, check);
            VerifyControls(flow, false, tPose, new[] { 5, PanelControls.TPose, PanelControls.Standing }, check);
            check(!flow.Start(false) && flow.Stage == CalibrationStage.Pose, "Lost devices allowed countdown.");
            check(flow.Start(true) && flow.CountingDown && flow.Preview, "Start did not enter countdown.");
            check(!flow.Start(true), "Double press restarted countdown.");
            VerifyControls(flow, true, tPose, new[] { 0 }, check);
            check(flow.Complete("result", succeeded, "Feet reversed vs FRONT; check roles / space"), "Active result was discarded.");
            check(flow.Stage == CalibrationStage.Review && !flow.Preview && !flow.CountingDown && flow.Succeeded == succeeded,
                "Success/failure must both release the fixed pose.");
            check(flow.Warning != null && flow.Result == "result", "Non-blocking warning/result was lost at review.");
            check(!flow.Complete("duplicate", !succeeded, null), "Duplicate result replaced review.");
            VerifyControls(flow, true, tPose, new[] { 0, 1 }, check);
            flow.Reset();
            check(flow.Stage == CalibrationStage.Preparation && !flow.Preview && flow.Result == null && flow.Warning == null,
                "Retry must return to preparation without auto-starting or freezing.");
            for (var attempt = 0; attempt < 3; attempt++)
            {
                check(flow.SelectPose(true) && flow.Start(true), "Repeated calibration cannot start.");
                flow.Cancel();
                check(flow.Canceled && !flow.CountingDown && !flow.Preview && !flow.Succeeded,
                    "Cancel/navigation must release countdown and pose.");
                check(!flow.Complete("late success", true, null), "Canceled countdown accepted a late success.");
                check(flow.Stage == CalibrationStage.Preparation, "Canceled countdown automatically resumed.");
            }
            check(flow.SelectPose(true), "Pose cannot be reopened after cancellation.");
            flow.Cancel();
            check(flow.Canceled && !flow.Preview, "Back from pose selection did not release preview.");
            flow.Reset();
            check(!flow.Canceled, "Reopening retained an old cancellation notice.");
        }
    }

    /// <summary>表示と入力に使う実際の部品定義で、操作できる領域を検証します。</summary>
    private static void VerifyControls(CalibrationFlow flow, bool ready, bool tPose, int[] expected,
        Action<bool, string> check)
    {
        var controls = new PanelControlState[PanelControls.Count];
        for (var i = PanelControls.Back; i <= PanelControls.Details; i++) controls[i] = new(PanelControlKind.Navigation);
        flow.ApplyControls(controls, ready, tPose);
        for (var i = 0; i < controls.Length; i++)
        {
            if (i is >= PanelControls.Back and <= PanelControls.Details) continue;
            check(controls[i].Interactive == expected.Contains(i), $"Unexpected action at {flow.Stage}/{i}.");
            if (controls[i].Kind is PanelControlKind.Hidden or PanelControlKind.Information)
                check(!controls[i].Interactive, "Hidden or information content became clickable.");
        }
        if (flow.CountingDown)
        {
            for (var i = PanelControls.Trackers; i <= PanelControls.Details; i++)
            {
                var bounds = PanelControls.Bounds(i);
                check(PanelControls.Hit(controls, bounds.X, bounds.Y) == -1, "Hidden settings tab still responds during countdown.");
            }
            check(controls[PanelControls.Back].Interactive && controls[PanelControls.Close].Interactive,
                "Countdown cannot be canceled through the fixed navigation.");
        }
        if (flow.Stage == CalibrationStage.Pose)
            check(controls[PanelControls.TPose].Selected == tPose && controls[PanelControls.Standing].Selected != tPose,
                "Selected pose indicator disagrees with the calibration mode.");
    }
}
