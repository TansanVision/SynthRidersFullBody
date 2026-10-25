using UnityEngine;

namespace SynthRidersFullBody;

/// <summary>割り当ての候補・確認画面・ページ操作を管理します。確認前には実設定を変更しません。</summary>
internal sealed partial class SettingsPanel
{
    private AssignmentDraft? _assignmentDraft;
    private AssignmentDraft? _automaticDraft;
    private bool _confirmingAutomatic;
    private bool? _ultimateBefore;
    private readonly List<AssignmentDevice> _deviceChoices = new();
    private readonly AssignmentDevice?[] _visibleDevices = new AssignmentDevice?[3];
    private int _confirmationPage;
    private bool _recalibrationRequired;
    private string? _assignmentNotice;
    private TrackerMountGuide? _mountGuide;

    /// <summary>実際のキャリブレーション状態を受け取り、変更後の操作案内へ反映します。</summary>
    public void SetCalibrationRequired(bool required)
    {
        if (_recalibrationRequired != required) _nextTextUpdate = 0f;
        _recalibrationRequired = required;
        if (!required) _assignmentNotice = null;
    }

    /// <summary>表示した候補を保持し、切断によって別の個体が同じ行へ繰り上がらないようにします。</summary>
    private void OpenDeviceChoices(TrackerRole role, Tracking tracking)
    {
        _selectedRole = role;
        _devicePage = 0;
        _deviceChoices.Clear();
        foreach (var device in tracking.Devices)
            if (tracking.AssignmentDevice(device.Key) is { } candidate) _deviceChoices.Add(candidate);
        Array.Clear(_visibleDevices);
        _assignmentNotice = null;
    }

    /// <summary>個体選択・解除・自動提案を確認用の変更案へ変換します。</summary>
    private PanelCommand HandleAssignmentRow(int row, Tracking tracking, Camera camera)
    {
        if (row == 5)
        {
            if (_assignmentDraft != null || _automaticDraft != null || _selectedRole != null) DiscardSelection();
            else if (_recalibrationRequired) return PanelCommand.Recalibrate;
            return PanelCommand.None;
        }
        if (_assignmentDraft is { } pending)
        {
            if (row != 3 || tracking.AssignmentError(pending) != null ||
                !pending.HasChanges && (_automaticDraft == null || _confirmingAutomatic)) return PanelCommand.None;
            if (_automaticDraft != null && !_confirmingAutomatic)
            {
                _automaticDraft = pending;
                _assignmentDraft = null;
                _selectedRole = null;
                return PanelCommand.None;
            }
            var result = tracking.ApplyAssignments(pending);
            if (result == AssignmentApplyResult.Invalid) return PanelCommand.None;
            DiscardSelection();
            _assignmentNotice = result == AssignmentApplyResult.Applied
                ? "Assignments applied. Recalibration required." : "No assignment changes.";
            return result == AssignmentApplyResult.Applied ? PanelCommand.AssignmentChanged : PanelCommand.None;
        }
        if (_selectedRole is { } role)
        {
            if (row is >= 0 and < 3 && _visibleDevices[row] is { } device)
            {
                if (!tracking.AssignmentDeviceAvailable(device)) return PanelCommand.None;
                _assignmentDraft = _automaticDraft?.Copy() ?? tracking.CreateAssignmentDraft();
                _assignmentDraft.Set(role, device);
            }
            else if (row == 3)
            {
                _assignmentDraft = _automaticDraft?.Copy() ?? tracking.CreateAssignmentDraft();
                _assignmentDraft.Set(role, null);
            }
            _confirmationPage = 0;
            return PanelCommand.None;
        }
        if (row is >= 0 and < 3) OpenDeviceChoices(TrackerRoles.All[_rolePage * 3 + row], tracking);
        else if (row == 3)
        {
            if (_automaticDraft == null)
            {
                _automaticDraft = tracking.ProposeAutoAssignment(camera);
                _rolePage = 0;
            }
            else
            {
                _assignmentDraft = _automaticDraft.Copy();
                _confirmingAutomatic = true;
                _confirmationPage = 0;
            }
        }
        return PanelCommand.None;
    }

    /// <summary>確認画面の操作を制限し、欠測・切断した候補を確定できないようにします。</summary>
    private void UpdateAssignmentControls(Tracking tracking)
    {
        if (_assignmentDraft is { } draft)
        {
            for (var i = 0; i < 3; i++) _controls[i] = new(PanelControlKind.Information);
            _controls[3] = new(PanelControlKind.Primary, (draft.HasChanges || _automaticDraft != null && !_confirmingAutomatic) &&
                tracking.AssignmentError(draft) == null);
            SetPaging(_confirmationPage, PageCount(draft.ChangedRoles.Count));
            _controls[5] = new(PanelControlKind.Navigation);
            return;
        }
        for (var i = 0; i < 3; i++)
            _controls[i] = _selectedRole != null
                ? new(_visibleDevices[i] == null ? PanelControlKind.Hidden : PanelControlKind.Choice,
                    _visibleDevices[i] is { } d && tracking.AssignmentDeviceAvailable(d))
                : new(PanelControlKind.Navigation, _rolePage * 3 + i < TrackerRoles.All.Length);
        _controls[3] = new(PanelControlKind.Primary, _selectedRole is { } role
            ? (_automaticDraft?.Proposed.ContainsKey(role.ToString()) ?? tracking.HasAssignment(role))
            : _automaticDraft?.HasChanges ?? tracking.Devices.Count > 0);
        SetPaging(_selectedRole != null ? _devicePage : _rolePage,
            PageCount(_selectedRole != null ? _deviceChoices.Count : TrackerRoles.All.Length));
        if (_selectedRole != null || _automaticDraft != null || _recalibrationRequired)
            _controls[5] = new(PanelControlKind.Navigation);
    }

    /// <summary>部位・個体・変更内容を表示します。左右は装着者基準で、候補には推定役割を付けません。</summary>
    private void UpdateRoleText(Tracking tracking)
    {
        foreach (var row in _rows) if (row != null) VrUi.SetFontSize(row, 26f);
        if (_assignmentDraft is { } pending)
        {
            _title!.text = UiText.Get("Confirm assignment changes");
            var roles = pending.ChangedRoles;
            _confirmationPage = Math.Min(_confirmationPage, PageCount(roles.Count) - 1);
            _status!.text = PageLabel(_confirmationPage, PageCount(roles.Count));
            for (var i = 0; i < 3; i++)
            {
                var index = _confirmationPage * 3 + i;
                if (index < roles.Count)
                {
                    var role = roles[index];
                    _rows[i]!.text = UiText.Get(TrackerRoles.Label(role)) + ": " +
                        UiText.Status(tracking.AssignmentUiLabel(role)) + "\n→ " + BindingLabel(pending.Proposed, role);
                    VrUi.SetFontSize(_rows[i]!, 23f);
                }
            }
            _rows[3]!.text = UiText.Get(_automaticDraft != null && !_confirmingAutomatic ? "Keep in preview" : "Apply changes");
            _rows[5]!.text = UiText.Get("Cancel changes");
            _footer!.text = UiText.Get(tracking.AssignmentError(pending) ?? (!pending.HasChanges ? "No assignment changes." :
                _automaticDraft != null && !_confirmingAutomatic ? "Preview only. Apply the full proposal when ready." :
                "Applying these changes requires recalibration."));
            if (pending.Proposed.Values.Any(b => b.Serial.Length == 0))
                _footer.text += "\n" + UiText.Get("No serial: this connection only. Assign again after reconnecting.");
            return;
        }
        if (_selectedRole is { } selected)
        {
            _title!.text = UiText.Format("ASSIGN: {0}", UiText.Get(TrackerRoles.Label(selected)));
            // Append newly connected devices without moving rows already being inspected.
            foreach (var device in tracking.Devices)
                if (tracking.AssignmentDevice(device.Key) is { } candidate &&
                    !_deviceChoices.Any(d => d.Key == candidate.Key && d.Session == candidate.Session)) _deviceChoices.Add(candidate);
            _devicePage = Math.Min(_devicePage, PageCount(_deviceChoices.Count) - 1);
            _status!.text = UiText.Format("Move the device to identify it | Page {0}/{1}", _devicePage + 1, PageCount(_deviceChoices.Count));
            var telemetry = tracking.GetTelemetry();
            for (var i = 0; i < 3; i++)
            {
                var index = _devicePage * 3 + i;
                var d = _visibleDevices[i] = index < _deviceChoices.Count ? _deviceChoices[index] : null;
                if (d == null) continue;
                var sample = telemetry.FirstOrDefault(t => t.Device.Key == d.Key && t.Device.SessionId == d.Session);
                var state = sample.Device == null ? "LOST" : MotionLabel(sample);
                _rows[i]!.text = DeviceLabel(d.Binding) + "  " + UiText.Get(state) + "\n" + d.Name;
                VrUi.SetFontSize(_rows[i]!, 23f);
            }
            _rows[3]!.text = UiText.Get("CLEAR / USE GAME DEFAULT");
            _rows[5]!.text = UiText.Get("Cancel changes");
            _footer!.text = UiText.Get(_deviceChoices.Count == 0
                ? "No devices available: check SteamVR / controller connections"
                : "Select a device, review the change, then Apply. Left/right are your own.");
        }
        else
        {
            _title!.text = UiText.Get(_automaticDraft != null ? "AUTO proposal (not applied)" : "TRACKER ROLES");
            _status!.text = UiText.Format("Choose a body part | Page {0}/{1}", _rolePage + 1, PageCount(TrackerRoles.All.Length));
            for (var i = 0; i < 3; i++)
            {
                var role = TrackerRoles.All[_rolePage * 3 + i];
                var label = _automaticDraft != null ? BindingLabel(_automaticDraft.Proposed, role) : UiText.Status(tracking.AssignmentUiLabel(role));
                _rows[i]!.text = UiText.Get(TrackerRoles.Label(role)) + "\n" + label;
            }
            _rows[3]!.text = UiText.Get(_automaticDraft != null ? "Review proposal" : "AUTO: preview suggestions");
            _rows[5]!.text = UiText.Get(_automaticDraft != null ? "Cancel changes" : _recalibrationRequired ? "Go to calibration" : "");
            _footer!.text = UiText.Get(_automaticDraft != null
                ? (_automaticDraft.HasChanges ? "Select a body part to edit this proposal. Existing roles are preserved by AUTO."
                    : "No clear empty waist/foot candidates. Face mirror, feet apart; or assign manually.")
                : _assignmentNotice ?? (_recalibrationRequired ? "Recalibration required." : "Left/right are your own. Move a device to identify it."));
        }
    }

    /// <summary>補正の変更内容と適用条件を表示します。</summary>
    private void UpdateUltimateText()
    {
        _title!.text = UiText.Get("Confirm Ultimate correction");
        _status!.text = UiText.Get("Applying these changes requires recalibration.");
        _rows[0]!.text = UiText.Format("Ultimate 180 correction: {0}", UiText.OnOff(_ultimateBefore == true)) + " → " + UiText.OnOff(_ultimateBefore != true);
        _rows[1]!.text = UiText.Get("Use only when forward AND sideways motion are reversed.");
        _rows[3]!.text = UiText.Get("Apply changes");
        _rows[5]!.text = UiText.Get("Cancel changes");
        _footer!.text = UiText.Get("Cancel keeps settings and calibration unchanged.");
    }

    /// <summary>共通の前へ・次へを有効化し、先頭・末尾では操作を無効にします。</summary>
    private void SetPaging(int page, int pages)
    {
        _controls[4] = default;
        PanelControls.Paging(_controls, PanelControls.PagePrevious, PanelControls.PageNext, page, pages);
        _views[PanelControls.PagePrevious]!.Label.text = UiText.Get("Previous page");
        _views[PanelControls.PageNext]!.Label.text = UiText.Get("Next page");
    }

    /// <summary>表示中の一覧だけを移動し、端から反対側へ循環させません。</summary>
    private void ChangePage(int direction)
    {
        _nextTextUpdate = 0f;
        if (_visualScreen && !_appearanceScreen) _visualPage = Math.Clamp(_visualPage + direction, 0, 1);
        if (_roleScreen)
        {
            if (_assignmentDraft != null) _confirmationPage = MovePage(_confirmationPage, _assignmentDraft.ChangedRoles.Count, direction);
            else if (_selectedRole != null) _devicePage = MovePage(_devicePage, _deviceChoices.Count, direction);
            else _rolePage = MovePage(_rolePage, TrackerRoles.All.Length, direction);
        }
        else if (_expressionScreen && _expressionButton != null) _expressionPage = MovePage(_expressionPage, ExpressionOptions().Count, direction);
    }

    /// <summary>3件ずつ表示した場合の、空一覧を含むページ数です。</summary>
    private static int PageCount(int count) => Math.Max(1, (count + 2) / 3);
    /// <summary>ページを有効範囲内へ移動します。</summary>
    private static int MovePage(int page, int count, int direction) => Math.Clamp(page + direction, 0, PageCount(count) - 1);
    /// <summary>現在のページと総数を表示します。</summary>
    private static string PageLabel(int page, int pages) => UiText.Format("Page {0}/{1}", page + 1, pages);
    /// <summary>割り当て案の個体、未設定、ゲーム標準を表示します。</summary>
    private static string BindingLabel(IReadOnlyDictionary<string, TrackerBinding> bindings, TrackerRole role) =>
        bindings.TryGetValue(role.ToString(), out var binding) ? DeviceLabel(binding) :
            UiText.Get(role is TrackerRole.Head or TrackerRole.LeftWrist or TrackerRole.RightWrist ? "Game default" : "Unassigned");
    /// <summary>翻訳せずに取得元と個体番号を表示し、番号なしの個体を明示します。</summary>
    private static string DeviceLabel(TrackerBinding binding) =>
        $"{binding.Source} #{binding.DeviceId} {(binding.Serial.Length > 0 ? binding.Serial : UiText.Get("(no serial)"))}";
}
