using UnityEngine;
using UnityEngine.XR;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

namespace SynthRidersFullBody;

/// <summary>
/// 位置と回転を組にした姿勢値です。座標系は各取得・変換処理の契約に従います。
/// </summary>
/// <param name="Position">使用する座標系で表した位置。</param>
/// <param name="Rotation">使用する座標系で表した姿勢の回転。</param>
internal readonly record struct Pose(Vector3 Position, Quaternion Rotation)
{
    /// <summary>
    /// 全成分が有限で、回転のノルムが有効とみなせる範囲かを示します。
    /// </summary>
    public bool IsFinite =>
        float.IsFinite(Position.x) && float.IsFinite(Position.y) &&
        float.IsFinite(Position.z) &&
        float.IsFinite(Rotation.x) && float.IsFinite(Rotation.y) &&
        float.IsFinite(Rotation.z) && float.IsFinite(Rotation.w) &&
        Quaternion.Dot(Rotation, Rotation) is > 0.25f and < 4f;
}
/// <summary>
/// 同一コントローラーに対応するポインター姿勢・追跡可否・トリガー入力です。
/// </summary>
/// <param name="Pose">計測した姿勢。HasPoseがfalseの場合は使用しません。</param>
/// <param name="HasPose">対応する姿勢を現在使用できるか。</param>
/// <param name="Trigger">このポインターと同じ個体のトリガーが押されているか。</param>
internal readonly record struct ControllerPointer(Pose Pose, bool HasPose, bool Trigger);
/// <summary>
/// 腰と左右の主足入力を、ワールド姿勢と部位ごとの追跡可否にまとめたフレームです。
/// </summary>
/// <param name="Waist">腰のワールド姿勢。</param>
/// <param name="Left">左足のワールド姿勢。</param>
/// <param name="Right">右足のワールド姿勢。</param>
/// <param name="HasWaist">腰の追跡姿勢が有効か。</param>
/// <param name="HasLeft">左足の姿勢が有効か。</param>
/// <param name="HasRight">右足の姿勢が有効か。</param>
internal readonly record struct TrackerFrame(Pose Waist, Pose Left, Pose Right,
    bool HasWaist, bool HasLeft, bool HasRight)
{
    /// <summary>
    /// 腰と左右足のすべてに有効な姿勢があるかを示します。
    /// </summary>
    public bool Complete => HasWaist && HasLeft && HasRight;
}

// Keep the tracking origin established at calibration. Rebuilding it from the
// HMD on every frame can move stationary trackers during a quick head turn.
/// <summary>
/// 各APIの追跡空間からゲームのワールド空間への固定剛体変換です。
/// </summary>
/// <param name="Translation">追跡原点のゲーム内ワールド位置。</param>
/// <param name="Rotation">追跡空間からワールド空間への回転。</param>
internal readonly record struct TrackingSpaceAnchor(Vector3 Translation, Quaternion Rotation)
{
    /// <summary>
    /// 同時刻の生HMD姿勢とゲーム内カメラ姿勢を一致させる変換を作成します。
    /// </summary>
    /// <param name="head">取得元APIの追跡空間で計測したHMDの生姿勢。</param>
    /// <param name="camera">ゲーム内のHMD視点として使用するカメラ。</param>
    /// <returns>追跡空間をゲーム空間へ合わせる平行移動と回転。</returns>
    public static TrackingSpaceAnchor FromHead(Pose head, Camera camera)
    {
        var rotation = camera.transform.rotation * Quaternion.Inverse(head.Rotation);
        return new TrackingSpaceAnchor(camera.transform.position - rotation * head.Position,
            rotation);
    }

    /// <summary>
    /// 追跡空間の姿勢へ固定した原点変換を適用します。
    /// </summary>
    /// <param name="raw">取得元APIの追跡空間で表した生姿勢。</param>
    /// <returns>ゲームのワールド空間の姿勢。</returns>
    public Pose ToWorld(Pose raw) =>
        new(Translation + Rotation * raw.Position, Rotation * raw.Rotation);
}

/// <summary>
/// 接続中のデバイスを識別する取得元・番号・名称・シリアル・種別・左右情報です。
/// </summary>
/// <param name="Source">姿勢を取得したAPIの識別名。</param>
/// <param name="Id">取得元API内のデバイス番号。</param>
/// <param name="Name">デバイスのモデル名・表示名。</param>
/// <param name="Serial">実機のシリアル番号。取得できない場合は空文字列。</param>
/// <param name="Kind">HMD、Controller、Trackerなどの種別。</param>
/// <param name="Side">取得元が報告する左右のコントローラー識別情報。身体部位の割り当てとは別です。</param>
internal sealed record TrackingDevice(string Source, string Id, string Name, string Serial, string Kind, string Side)
{
    /// <summary>
    /// 取得元とシリアル、または番号と名称から作る一覧内の識別キーです。
    /// </summary>
    public string Key => $"{Source}:{(Serial.Length > 0 ? Serial : Id + ":" + Name)}";
    /// <summary>
    /// この個体を役割へ割り当てるための設定値を生成します。
    /// </summary>
    public TrackerBinding Binding => new() { Source = Source, Serial = Serial, DeviceId = Id };
}
/// <summary>
/// デバイス情報と取得元の追跡空間における生姿勢・追跡可否をまとめた表示用データです。
/// </summary>
/// <param name="Device">この計測値に対応するデバイス情報。</param>
/// <param name="Pose">計測した姿勢。HasPoseがfalseの場合は使用しません。</param>
/// <param name="HasPose">対応する姿勢を現在使用できるか。</param>
internal readonly record struct TrackerTelemetry(TrackingDevice Device, Pose Pose, bool HasPose)
{
    /// <summary>
    /// 取得元API内のデバイス番号です。
    /// </summary>
    public string Id => Device.Id;
    /// <summary>
    /// デバイスのモデル名・表示名です。
    /// </summary>
    public string Name => Device.Name;
    /// <summary>
    /// 実機のシリアル番号です。取得できない場合は空です。
    /// </summary>
    public string Serial => Device.Serial;
    /// <summary>
    /// 姿勢を取得したAPIの名前です。
    /// </summary>
    public string Source => Device.Source;
    /// <summary>
    /// 取得元の追跡空間での生位置です。ゲームのワールド位置ではありません。
    /// </summary>
    public Vector3 Position => Pose.Position;
}
/// <summary>
/// SteamVRとUnity XRを統合し、手動割り当て・固定原点・ワールド姿勢・UI入力を提供します。
/// </summary>
internal sealed class Tracking
{
    private readonly List<InputDevice> _xrDevices = new();
    private readonly List<TrackingDevice> _devices = new();
    private readonly Dictionary<string, TrackingSpaceAnchor> _anchors = new();
    private readonly Dictionary<string, UltimateTrackerCorrection> _ultimateCorrections = new();
    private readonly Dictionary<TrackerRole, Pose> _rolePoses = new();
    private InputDevice _hmd;
    private readonly ModConfig _config;
    private readonly SteamVrTracking _steamVr = new();
    private TrackingDevice? _uiLeft, _uiRight;
    private int _controllerFrame = -1;
    /// <summary>
    /// 直近の探索で識別したHMD・コントローラー・トラッカーの一覧です。
    /// </summary>
    public IReadOnlyList<TrackingDevice> Devices => _devices;
    /// <summary>
    /// 直近のワールド姿勢読み取りで取得できた部位の姿勢です。欠測部位は含みません。
    /// </summary>
    public IReadOnlyDictionary<TrackerRole, Pose> RolePoses => _rolePoses;
    /// <summary>
    /// 最後の割り当て操作の結果や、ユーザーへ提示する案内です。
    /// </summary>
    public string AssignmentStatus { get; private set; } = "Choose a role, then a device; AUTO fills empty body roles";
    /// <summary>
    /// Unity XRとSteamVRのデバイス探索結果をまとめた診断文です。
    /// </summary>
    public string DeviceSummary { get; private set; } = "";
    /// <summary>
    /// 現在の一覧で種別がTrackerになっている項目数です。
    /// </summary>
    public int TrackerCount => _devices.Count(x => x.Kind == "Tracker");
    /// <summary>
    /// Unity XRのHMDが有効、またはSteamVRのHMD姿勢が利用可能かを示します。
    /// </summary>
    public bool HmdPresent => _hmd.isValid || _steamVr.HmdPresent;
    /// <summary>
    /// HMDの検出状況に応じた表示用取得元名です。校正の有効性を判定する値ではありません。
    /// </summary>
    public string SourceLabel => _steamVr.HmdPresent ? "SteamVR" : "Unity XR";
    /// <summary>
    /// Ultimate Tracker専用の180度補正を有効にする設定です。
    /// </summary>
    public bool UltimateCorrectionEnabled => _config.UltimateTrackerRotate180;
    /// <summary>
    /// 共有設定を保持して、デバイス統合と割り当て管理を初期化します。
    /// </summary>
    /// <param name="config">使用する共有設定。保存処理がないメソッドではファイルへの書き込みは行いません。</param>
    public Tracking(ModConfig config) => _config = config;
    /// <summary>
    /// 指定部位の割り当てキーが設定に存在するかを調べます。
    /// </summary>
    /// <param name="role">対象の身体部位。</param>
    /// <returns>設定が存在する場合はtrue。接続・追跡の成功は意味しません。</returns>
    public bool HasAssignment(TrackerRole role) => _config.RoleBindings.ContainsKey(role.ToString());
    /// <summary>
    /// 重複を除外し、保存した割り当てに一致する現在のデバイスを取得します。
    /// </summary>
    /// <param name="role">対象の身体部位。</param>
    /// <returns>接続中の対応デバイス。未設定・未接続・重複時はnull。</returns>
    private TrackingDevice? Assigned(TrackerRole role)
    {
        if (TrackerRoles.IsConflicted(_config.RoleBindings, role)) return null;
        if (!_config.RoleBindings.TryGetValue(role.ToString(), out var binding)) return null;
        return _devices.FirstOrDefault(d => binding.Matches(d.Source, d.Serial, d.Id));
    }
    /// <summary>
    /// 選択した個体を部位へ割り当てるか解除し、旧形式のシリアルと入力選択キャッシュを更新します。
    /// </summary>
    /// <param name="role">対象の身体部位。</param>
    /// <param name="deviceKey">選択した個体の識別キー。nullなら部位の割り当てを解除します。</param>
    public void Assign(TrackerRole role, string? deviceKey)
    {
        var device = deviceKey == null ? null : _devices.FirstOrDefault(d => d.Key == deviceKey);
        if (deviceKey != null && device == null) { AssignmentStatus = "Device disconnected; select it again"; return; }
        TrackerRoles.Assign(_config.RoleBindings, role, device?.Binding);
        SyncLegacySerials();
        _controllerFrame = -1;
        AssignmentStatus = device == null ? $"{TrackerRoles.Label(role)} cleared" :
            $"{TrackerRoles.Label(role)} = {device.Serial} ({device.Source} #{device.Id})" +
            (device.Serial.Length == 0 ? " | No serial: this session only" : "");
    }
    /// <summary>
    /// 現在の腰・左右足首の割り当てを、互換用の旧シリアル設定へ反映します。
    /// </summary>
    private void SyncLegacySerials()
    {
        string Serial(TrackerRole role) => _config.RoleBindings.TryGetValue(role.ToString(), out var b) ? b.Serial : "";
        _config.WaistSerial = Serial(TrackerRole.Waist);
        _config.LeftFootSerial = Serial(TrackerRole.LeftFoot);
        _config.RightFootSerial = Serial(TrackerRole.RightFoot);
    }
    /// <summary>
    /// 校正確定時の割り当てを互換用の旧シリアル設定へ同期します。
    /// </summary>
    public void ConfirmAssignments() => SyncLegacySerials();
    /// <summary>
    /// 部位の割り当てをログ表示用に整形します。
    /// </summary>
    /// <param name="role">対象の身体部位。</param>
    /// <returns>取得元と個体情報、または未設定・未接続・重複の説明。</returns>
    public string AssignmentLabel(TrackerRole role) => AssignmentUiLabel(role);
    /// <summary>
    /// 部位の現在の割り当てと接続状態をUI向けの文字列にします。
    /// </summary>
    /// <param name="role">対象の身体部位。</param>
    /// <returns>個体情報、ゲーム標準、未設定、未接続、重複のいずれかの表示。</returns>
    public string AssignmentUiLabel(TrackerRole role)
    {
        if (TrackerRoles.IsConflicted(_config.RoleBindings, role)) return "CONFLICT: device assigned twice";
        var device = Assigned(role);
        if (device != null) return $"{device.Source} #{device.Id} {(device.Serial.Length > 0 ? device.Serial : device.Name)}";
        if (_config.RoleBindings.TryGetValue(role.ToString(), out var missing)) return $"MISSING {missing.Serial} (select device)";
        return role is TrackerRole.Head or TrackerRole.LeftWrist or TrackerRole.RightWrist ? "Game default" : "Unassigned";
    }
    /// <summary>
    /// 両APIのデバイスを再列挙して識別情報と診断文を更新します。
    /// </summary>
    /// <returns>現在割り当てられた腰と左右主足入力の生姿勢をすべて取得できればtrue。</returns>
    public bool Refresh()
    {
        _xrDevices.Clear(); _devices.Clear();
        var xr = new Il2CppSystem.Collections.Generic.List<InputDevice>();
        InputDevices.GetDevices(xr);
        _hmd = default;
        var lines = new List<string>();
        for (var i = 0; i < xr.Count; i++)
        {
            var device = xr[i];
            if (!device.isValid) continue;
            _xrDevices.Add(device);
            var flags = device.characteristics;
            var kind = (flags & InputDeviceCharacteristics.HeadMounted) != 0 ? "HMD" :
                (flags & InputDeviceCharacteristics.Controller) != 0 ? "Controller" :
                (flags & InputDeviceCharacteristics.TrackingReference) != 0 ? "" :
                (flags & InputDeviceCharacteristics.TrackedDevice) != 0 ||
                    (device.name?.Contains("tracker", StringComparison.OrdinalIgnoreCase) ?? false) ? "Tracker" : "";
            if (kind == "HMD") _hmd = device;
            var valid = TryRead(device, out _);
            lines.Add($"{device.name} | id={device.deviceId} | serial={device.serialNumber} | flags={flags} | pose={(valid ? "yes" : "no")}");
            if (kind.Length > 0)
                _devices.Add(new TrackingDevice("Unity XR", device.deviceId.ToString(), device.name ?? "",
                    device.serialNumber ?? "", kind, (flags & InputDeviceCharacteristics.Left) != 0 ? "LeftHand" :
                    (flags & InputDeviceCharacteristics.Right) != 0 ? "RightHand" : ""));
        }
        try { _steamVr.Refresh(); _devices.InsertRange(0, _steamVr.Devices); }
        catch (Exception exception) { lines.Add($"SteamVR unavailable: {exception.Message}"); }
        DeviceSummary = $"Unity XR devices:\n{string.Join(Environment.NewLine, lines)}\nSteamVR devices:\n{_steamVr.DeviceSummary}";
        _controllerFrame = -1;
        return HasRawPose(Assigned(TrackerRole.Waist)) &&
            HasRawPose(Assigned(PrimaryFoot(true))) && HasRawPose(Assigned(PrimaryFoot(false)));
    }
    /// <summary>
    /// 鏡方向と現在位置から空き部位の候補を提案し、曖昧でない未使用個体だけを割り当てます。
    /// </summary>
    /// <param name="camera">ゲーム内のHMD視点として使用するカメラ。</param>
    /// <remarks>明示的なAUTO操作から呼び出します。既存の割り当てと使用中の個体を保ち、追跡中に自動で役割を入れ替えません。</remarks>
    public void AutoAssign(Camera camera)
    {
        // Explicit user action only. Existing choices are never overwritten.
        var forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
        try
        {
            var mirror = Il2CppSynth.Avatars.AvatarLoader.s_instance?.avatarMirror;
            if (mirror != null)
            {
                var toward = Vector3.ProjectOnPlane(mirror.transform.position - camera.transform.position, Vector3.up);
                if (toward.sqrMagnitude > 0.04f) forward = toward.normalized;
            }
        }
        catch { /* Without a native mirror, the user must face forward for AUTO. */ }
        var count = 0;
        foreach (var source in new[] { "SteamVR", "Unity XR" })
        {
            if (!RawHead(source, out var head)) continue;
            var anchor = TrackingSpaceAnchor.FromHead(head, camera);
            var rotation = anchor.Rotation;
            var rawForward = Quaternion.Inverse(rotation) * forward;
            var candidates = _devices.Where(d => d.Source == source && d.Kind == "Tracker" &&
                HasRawPose(d)).Select(d =>
            {
                TryRaw(d, out var pose);
                pose = CorrectUltimatePose(d, pose, anchor, camera);
                return new BodyCandidate(d.Key, new(pose.Position.x, pose.Position.y, pose.Position.z));
            });
            var suggested = AutoBodyAssignment.Suggest(candidates,
                new(head.Position.x, head.Position.y, head.Position.z), new(rawForward.x, rawForward.y, rawForward.z));
            foreach (var entry in suggested)
            {
                var device = _devices.First(d => d.Key == entry.Value);
                if (HasAssignment(entry.Key) || _config.RoleBindings.Values.Any(b => b.SameDevice(device.Binding))) continue;
                // A single remaining sensor is not enough evidence to rename an
                // explicitly configured toe-only foot to an ankle.
                if (entry.Key == TrackerRole.LeftFoot && HasAssignment(TrackerRole.LeftToes) &&
                    !suggested.ContainsKey(TrackerRole.LeftToes) ||
                    entry.Key == TrackerRole.RightFoot && HasAssignment(TrackerRole.RightToes) &&
                    !suggested.ContainsKey(TrackerRole.RightToes)) continue;
                Assign(entry.Key, device.Key);
                count++;
            }
        }
        AssignmentStatus = count > 0 ? $"AUTO filled {count} empty roles. Check each device; correct manually." :
            "No clear empty waist/foot candidates. Face mirror, feet apart; or assign manually.";
    }
    /// <summary>
    /// 現在の一覧から、識別情報と各APIの生姿勢を取得します。
    /// </summary>
    /// <returns>未追跡の接続個体も含む表示用データの配列。</returns>
    /// <remarks>表示座標はAPIごとの生座標です。取得元が違う個体どうしの座標を、そのままゲーム内の距離として比較しないでください。</remarks>
    public IReadOnlyList<TrackerTelemetry> GetTelemetry() =>
        _devices.Select(d => { var valid = TryRaw(d, out var p); return new TrackerTelemetry(d, p, valid); }).ToArray();

    /// <summary>
    /// 校正失敗を切り分けるため、原点変換・HMD整合性・割り当てた部位の生姿勢とワールド姿勢を記録します。
    /// </summary>
    /// <param name="camera">判定に使用したゲーム内のHMDカメラ。</param>
    /// <returns>各APIの原点・HMD差分と、割り当てたデバイスの姿勢の診断文。欠測はmissingと表示します。</returns>
    /// <remarks>ワールド姿勢は直近のReadWorldPosesの結果です。HMDが一致しても、異なる機器間の空間合わせが正しいことは保証しません。</remarks>
    public string CalibrationDiagnostics(Camera camera)
    {
        var lines = new List<string>();
        lines.Add($"Ultimate Tracker 180 correction: {(_config.UltimateTrackerRotate180 ? "ON" : "OFF")}; " +
            $"detected={_devices.Count(IsUltimateTracker)}");
        foreach (var source in new[] { "SteamVR", "Unity XR" })
        {
            if (!_anchors.TryGetValue(source, out var anchor))
            {
                lines.Add($"space={source}; anchor=missing");
                continue;
            }
            lines.Add($"space={source}; translation={FormatVector(anchor.Translation)}; " +
                $"rotation={anchor.Rotation.ToString("F4")}; " +
                $"up={FormatVector(anchor.Rotation * Vector3.up)}");
            if (_ultimateCorrections.TryGetValue(source, out var correction))
                lines.Add($"space={source}; Ultimate correction pivot={CalibrationPoseCheck.Format(correction.Pivot)}; " +
                    $"destination={CalibrationPoseCheck.Format(correction.Destination)}; " +
                    $"shared shift={CalibrationPoseCheck.Format(correction.Destination - correction.Pivot)}; " +
                    $"rotation={correction.Rotation}");
            if (RawHead(source, out var head))
            {
                var mapped = anchor.ToWorld(head);
                lines.Add($"space={source}; raw HMD={FormatPose(head)}; mapped HMD={FormatPose(mapped)}; " +
                    FormattableString.Invariant($"camera error={Vector3.Distance(mapped.Position, camera.transform.position):F3} m / {Quaternion.Angle(mapped.Rotation, camera.transform.rotation):F2} deg"));
            }
            else lines.Add($"space={source}; raw HMD=missing");
        }
        foreach (var role in TrackerRoles.All.Where(HasAssignment))
        {
            var device = Assigned(role);
            var raw = TryRaw(device, out var pose) ? FormatPose(pose) : "missing";
            var world = _rolePoses.TryGetValue(role, out var mapped) ? FormatPose(mapped) : "missing";
            var state = device?.Source == "SteamVR" ? _steamVr.PoseStatus(device.Id) : "see raw pose";
            lines.Add($"role={TrackerRoles.Label(role)}; {AssignmentLabel(role)}; model={device?.Name ?? "missing"}; " +
                $"state={state}; ultimate180={(_config.UltimateTrackerRotate180 && device != null && IsUltimateTracker(device))}; raw={raw}; world={world}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// 姿勢の座標と回転軸を診断用の固定精度で表示します。
    /// </summary>
    /// <param name="pose">表示する生またはワールド姿勢。</param>
    /// <returns>位置と前方・上方ベクトルの文字列。</returns>
    private static string FormatPose(Pose pose) =>
        $"position={FormatVector(pose.Position)}, forward={FormatVector(pose.Rotation * Vector3.forward)}, up={FormatVector(pose.Rotation * Vector3.up)}";

    /// <summary>
    /// 座標または方向をロケールに依存しないXYZ文字列へ変換します。
    /// </summary>
    /// <param name="value">表示するベクトル。</param>
    /// <returns>小数点以下3桁のXYZ文字列。</returns>
    private static string FormatVector(Vector3 value) => CalibrationPoseCheck.Format(TPoseCalibration.ToNumerics(value));

    /// <summary>
    /// 利用できる各APIのHMDとゲーム視点を合わせ、校正用の固定原点を取り直します。
    /// </summary>
    /// <param name="camera">ゲーム内のHMD視点として使用するカメラ。</param>
    /// <returns>少なくとも1つの原点を確保できればtrue。</returns>
    public bool CaptureWorldAnchor(Camera camera)
    {
        _anchors.Clear();
        foreach (var source in new[] { "SteamVR", "Unity XR" })
            if (RawHead(source, out var head)) _anchors[source] = TrackingSpaceAnchor.FromHead(head, camera);
        return _anchors.Count > 0;
    }
    /// <summary>
    /// 保存した取得元ごとの固定原点とUltimate Trackerの補正中心をすべて破棄します。
    /// </summary>
    public void ClearWorldAnchor()
    {
        _anchors.Clear();
        _ultimateCorrections.Clear();
    }
    /// <summary>
    /// 指定APIについて保存済みの固定原点を取得します。
    /// </summary>
    /// <param name="source">取得元APIの識別名。</param>
    /// <param name="space">成功時に、取得元の追跡空間からワールド空間への固定変換を受け取ります。</param>
    /// <returns>指定した取得元の原点が保存されていればtrue。</returns>
    public bool TryGetSpace(string source, out TrackingSpaceAnchor space) => _anchors.TryGetValue(source, out space);
    /// <summary>
    /// 腰の割り当てが使う取得元と、その固定原点を取得します。
    /// </summary>
    /// <param name="source">腰入力に割り当てた取得元APIの識別名を受け取ります。</param>
    /// <param name="space">成功時に、取得元の追跡空間からワールド空間への固定変換を受け取ります。</param>
    /// <returns>腰の取得元に対応する原点が存在する場合はtrue。</returns>
    public bool TryGetCalibrationSpace(out string source, out TrackingSpaceAnchor space)
    {
        source = Assigned(TrackerRole.Waist)?.Source ?? "";
        return TryGetSpace(source, out space);
    }
    /// <summary>
    /// 指定APIのHMD姿勢を取得して追跡原点を取り直せるかを確認します。
    /// </summary>
    /// <param name="source">取得元APIの識別名。</param>
    /// <returns>生HMD姿勢が取得できる場合はtrue。</returns>
    public bool CanRebase(string source) => RawHead(source, out _);
    /// <summary>
    /// 取得元に応じて生のHMD姿勢を読み取ります。
    /// </summary>
    /// <param name="source">取得元APIの識別名。</param>
    /// <param name="head">成功時に、取得元の追跡空間でのHMD姿勢を受け取ります。</param>
    /// <returns>指定APIの追跡空間内で有効な姿勢が得られればtrue。</returns>
    private bool RawHead(string source, out Pose head)
    {
        if (source == "SteamVR") return _steamVr.TryGetRawHeadPose(out head);
        return TryRead(_hmd, out head);
    }
    /// <summary>
    /// 指定デバイスの取得元へ問い合わせ、生姿勢を例外を吸収して読み取ります。
    /// </summary>
    /// <param name="device">姿勢または入力を読み取るデバイス。</param>
    /// <param name="pose">成功時に、デバイスの取得元の追跡空間での生姿勢を受け取ります。</param>
    /// <returns>追跡空間内の有効な姿勢を取得できればtrue。</returns>
    private bool TryRaw(TrackingDevice? device, out Pose pose)
    {
        pose = default;
        if (device == null) return false;
        try
        {
            return device.Source == "SteamVR" ? _steamVr.TryRead(device.Id, out pose) :
                TryRead(_xrDevices.FirstOrDefault(d => d.deviceId.ToString() == device.Id), out pose);
        }
        catch { return false; }
    }
    /// <summary>
    /// 指定デバイスに読み取り可能な生姿勢があるかを調べます。
    /// </summary>
    /// <param name="device">姿勢または入力を読み取るデバイス。</param>
    /// <returns>姿勢を取得できる場合はtrue。</returns>
    private bool HasRawPose(TrackingDevice? device) => TryRaw(device, out _);
    /// <summary>
    /// デバイスの生姿勢を固定原点または現在のHMD対応でワールド姿勢へ変換します。
    /// </summary>
    /// <param name="device">姿勢または入力を読み取るデバイス。</param>
    /// <param name="camera">ゲーム内のHMD視点として使用するカメラ。</param>
    /// <param name="frozen">保存した固定原点を優先するか。falseでは現在のHMDから変換を求めます。</param>
    /// <param name="pose">成功時に、ゲーム内ワールド空間の姿勢を受け取ります。</param>
    /// <returns>生姿勢と必要な原点変換を取得できればtrue。</returns>
    /// <remarks>保存済み原点があればHMDの一時欠測だけで変換を失いません。固定原点がなくHMDも取得できない場合は失敗します。</remarks>
    private bool TryWorld(TrackingDevice? device, Camera camera, bool frozen, out Pose pose)
    {
        pose = default;
        if (device == null || !TryRaw(device, out var raw)) return false;
        TrackingSpaceAnchor anchor;
        if (frozen && _anchors.TryGetValue(device.Source, out var saved)) anchor = saved;
        else
        {
            if (!RawHead(device.Source, out var head)) return false;
            anchor = TrackingSpaceAnchor.FromHead(head, camera);
            // A source that reconnects after a scene transition gets one fixed
            // anchor; never rebuild it from head movement on every frame.
            if (frozen && _anchors.Count > 0) _anchors[device.Source] = anchor;
        }
        pose = anchor.ToWorld(CorrectUltimatePose(device, raw, anchor, camera));
        return true;
    }
    /// <summary>
    /// 対象機種の判定を、UI・ログ・姿勢取得で共通に使用します。
    /// </summary>
    /// <param name="device">列挙で得た機器情報。</param>
    /// <returns>SteamVRのUltimate Trackerならtrue。</returns>
    private static bool IsUltimateTracker(TrackingDevice device) =>
        UltimateTrackerCorrection.IsTarget(device.Source, device.Kind, device.Name);

    /// <summary>
    /// 有効にしたUltimate Trackerだけへ、取得元の追跡空間で固定した180度補正を適用します。
    /// </summary>
    /// <param name="device">姿勢に対応する機器。</param>
    /// <param name="raw">取得元の未補正姿勢。</param>
    /// <param name="anchor">追跡空間からゲーム空間への変換。最初の鉛直軸取得に使用します。</param>
    /// <param name="camera">直立校正時の頭の位置を与えるHMDカメラ。位置合わせの初回だけ参照します。</param>
    /// <returns>補正後の生空間姿勢。無効時や対象外の機器では入力をそのまま返します。</returns>
    /// <remarks>
    /// 対象の腰があれば、その水平位置をHMDの真下へ一度だけ合わせます。
    /// 頭と腰の共通の位置ずれを、各関節の回転に連動する長い装着オフセットへ混ぜないためです。
    /// 対象の腰を取得できない場合は、最初の対象機器を中心に回転だけを適用します。
    /// 再校正ではClearWorldAnchorで取り直し、シーン復帰時のCaptureWorldAnchorでは保持します。
    /// </remarks>
    private Pose CorrectUltimatePose(TrackingDevice device, Pose raw, TrackingSpaceAnchor anchor, Camera camera)
    {
        if (!_config.UltimateTrackerRotate180 || !IsUltimateTracker(device)) return raw;
        if (!_ultimateCorrections.TryGetValue(device.Source, out var correction))
        {
            var pivot = raw.Position;
            var waist = Assigned(TrackerRole.Waist);
            var inverseAnchor = Quaternion.Inverse(anchor.Rotation);
            var up = TPoseCalibration.ToNumerics(inverseAnchor * Vector3.up);
            var destination = TPoseCalibration.ToNumerics(pivot);
            if (waist != null && waist.Source == device.Source && IsUltimateTracker(waist) && TryRaw(waist, out var waistPose))
            {
                pivot = waistPose.Position;
                var head = inverseAnchor * (camera.transform.position - anchor.Translation);
                destination = UltimateTrackerCorrection.AlignWaistBelowHead(
                    TPoseCalibration.ToNumerics(pivot), TPoseCalibration.ToNumerics(head), up);
            }
            correction = UltimateTrackerCorrection.Create(TPoseCalibration.ToNumerics(pivot), up, destination);
            _ultimateCorrections[device.Source] = correction;
        }
        var q = raw.Rotation;
        var corrected = correction.Apply(TPoseCalibration.ToNumerics(raw.Position), new(q.x, q.y, q.z, q.w));
        return new Pose(new Vector3(corrected.Position.X, corrected.Position.Y, corrected.Position.Z),
            new Quaternion(corrected.Rotation.X, corrected.Rotation.Y, corrected.Rotation.Z, corrected.Rotation.W));
    }
    /// <summary>
    /// 足首の割り当てがあれば足首、未設定なら足先を主足入力として選びます。
    /// </summary>
    /// <param name="left">左側を選択するか。falseなら右側を選択します。</param>
    /// <returns>指定側で位置の基準に使用する部位。</returns>
    /// <remarks>足首が割り当て済みなら、追跡喪失時も足先を代用しません。選択は接続可否ではなく割り当ての有無で決まります。</remarks>
    public TrackerRole PrimaryFoot(bool left) => left
        ? HasAssignment(TrackerRole.LeftFoot) ? TrackerRole.LeftFoot : TrackerRole.LeftToes
        : HasAssignment(TrackerRole.RightFoot) ? TrackerRole.RightFoot : TrackerRole.RightToes;
    /// <summary>
    /// 割り当てた全身部位のワールド姿勢を更新し、腰と主足入力をまとめます。
    /// </summary>
    /// <param name="camera">ゲーム内のHMD視点として使用するカメラ。</param>
    /// <returns>部位ごとの追跡可否を含む現在のフレーム。</returns>
    public TrackerFrame ReadWorldPoses(Camera camera)
    {
        _rolePoses.Clear();
        foreach (var role in TrackerRoles.All)
            if (TryWorld(Assigned(role), camera, true, out var pose)) _rolePoses[role] = pose;
        var hasWaist = _rolePoses.TryGetValue(TrackerRole.Waist, out var waist);
        var hasLeft = _rolePoses.TryGetValue(PrimaryFoot(true), out var left);
        var hasRight = _rolePoses.TryGetValue(PrimaryFoot(false), out var right);
        return new TrackerFrame(waist, left, right, hasWaist, hasLeft, hasRight);
    }
    /// <summary>
    /// 重複割り当て、または姿勢が得られない設定済み部位を最初の1件について報告します。
    /// </summary>
    /// <returns>問題のある部位の説明。該当しなければnull。</returns>
    public string? MissingAssignedPose()
    {
        foreach (var role in TrackerRoles.All)
        {
            if (TrackerRoles.IsConflicted(_config.RoleBindings, role))
                return $"{TrackerRoles.Label(role)}: device assigned twice; choose roles again";
            if (HasAssignment(role) && !_rolePoses.ContainsKey(role))
                return $"{TrackerRoles.Label(role)}: assigned device is not tracked";
        }
        return null;
    }
    /// <summary>
    /// 指定されたコントローラーと同じ取得元からトリガーボタンを読み取ります。
    /// </summary>
    /// <param name="device">姿勢または入力を読み取るデバイス。</param>
    /// <returns>有効なコントローラーでトリガーが押されていればtrue。</returns>
    private bool Trigger(TrackingDevice? device)
    {
        if (device == null || device.Kind != "Controller") return false;
        if (device.Source == "SteamVR") return _steamVr.Trigger(device.Id);
        var xr = _xrDevices.FirstOrDefault(d => d.deviceId.ToString() == device.Id);
        return xr.isValid && xr.TryGetFeatureValue(CommonUsages.triggerButton, out bool pressed) && pressed;
    }
    /// <summary>
    /// 手首への手動割り当てを優先し、各手のUI操作個体をフレーム内で一度だけ選択します。
    /// </summary>
    private void ResolveControllers()
    {
        if (_controllerFrame == Time.frameCount) return;
        _controllerFrame = Time.frameCount;
        TrackingDevice? Pick(bool left, TrackingDevice? previous)
        {
            var assigned = Assigned(left ? TrackerRole.LeftWrist : TrackerRole.RightWrist);
            if (assigned?.Kind == "Controller" && HasRawPose(assigned)) return assigned;
            return _devices.Where(d => d.Kind == "Controller" && d.Side == (left ? "LeftHand" : "RightHand") && HasRawPose(d))
                .OrderByDescending(Trigger).ThenByDescending(d => d.Key == previous?.Key)
                .ThenByDescending(d => d.Source == "Unity XR").FirstOrDefault();
        }
        _uiLeft = Pick(true, _uiLeft); _uiRight = Pick(false, _uiRight);
    }
    /// <summary>
    /// 選択した左右コントローラーのトリガーと、その論理和を取得します。
    /// </summary>
    /// <param name="trigger">左右いずれかのトリガーが押されているかを受け取ります。</param>
    /// <param name="leftTrigger">左手のトリガーが押されているかを受け取ります。</param>
    /// <param name="rightTrigger">右手のトリガーが押されているかを受け取ります。</param>
    public void ReadControllerTriggers(out bool trigger, out bool leftTrigger, out bool rightTrigger)
    {
        ResolveControllers();
        leftTrigger = Trigger(_uiLeft); rightTrigger = Trigger(_uiRight);
        trigger = leftTrigger || rightTrigger;
    }

    /// <summary>手首割り当てを優先して、表情操作用の左右ボタンを同じデバイスから取得します。</summary>
    public ExpressionInput ReadExpressionButtons()
    {
        ResolveControllers();
        var left = ExpressionButtons(_uiLeft);
        var right = ExpressionButtons(_uiRight);
        return new ExpressionInput(left.Pressed | (right.Pressed << 3),
            left.Available | (right.Available << 3), $"{_uiLeft?.Key}|{_uiRight?.Key}");
    }

    /// <summary>指定個体の主・副・グリップボタンを読み取り、未対応入力と未押下を区別します。</summary>
    private (int Pressed, int Available) ExpressionButtons(TrackingDevice? device)
    {
        if (device == null || device.Kind != "Controller" || !HasRawPose(device)) return default;
        if (device.Source == "SteamVR") return _steamVr.ExpressionButtons(device.Id);
        var xr = _xrDevices.FirstOrDefault(d => d.deviceId.ToString() == device.Id);
        if (!xr.isValid) return default;
        var available = 0;
        var pressed = 0;
        if (xr.TryGetFeatureValue(CommonUsages.primaryButton, out bool primary))
        { available |= 1; if (primary) pressed |= 1; }
        if (xr.TryGetFeatureValue(CommonUsages.secondaryButton, out bool secondary))
        { available |= 2; if (secondary) pressed |= 2; }
        if (xr.TryGetFeatureValue(CommonUsages.gripButton, out bool grip))
        { available |= 4; if (grip) pressed |= 4; }
        return (pressed, available);
    }
    /// <summary>
    /// 手首割り当てまたはUI用コントローラーの姿勢を、固定原点でワールド空間へ移します。
    /// </summary>
    /// <param name="camera">ゲーム内のHMD視点として使用するカメラ。</param>
    /// <param name="left">左側を選択するか。falseなら右側を選択します。</param>
    /// <param name="pose">成功時に、指定した手のワールド姿勢を受け取ります。</param>
    /// <returns>指定した手の有効な姿勢を取得できた場合はtrue。</returns>
    public bool TryGetControllerWorldPose(Camera camera, bool left, out Pose pose)
    {
        ResolveControllers();
        var role = left ? TrackerRole.LeftWrist : TrackerRole.RightWrist;
        return TryWorld(HasAssignment(role) ? Assigned(role) : left ? _uiLeft : _uiRight, camera, true, out pose);
    }
    /// <summary>
    /// トリガーと同じ個体の照準姿勢を取得し、取得できない場合はその個体のグリップ姿勢を使います。
    /// </summary>
    /// <param name="camera">ゲーム内のHMD視点として使用するカメラ。</param>
    /// <param name="left">左側を選択するか。falseなら右側を選択します。</param>
    /// <param name="pose">成功時に、UIポインターのワールド姿勢を受け取ります。</param>
    /// <returns>UI操作用のワールド姿勢を取得できた場合はtrue。</returns>
    public bool TryGetControllerPointerWorldPose(Camera camera, bool left, out Pose pose)
    {
        ResolveControllers();
        var device = left ? _uiLeft : _uiRight;
        pose = default;
        if (device == null) return false;
        // Never take the pointer from a different controller than the selected trigger.
        if (!HasAssignment(left ? TrackerRole.LeftWrist : TrackerRole.RightWrist) && device.Source == "Unity XR" &&
            _devices.Count(d => d.Source == "Unity XR" && d.Kind == "Controller" && d.Side == device.Side) == 1 &&
            RawHead("Unity XR", out var head))
        {
            try
            {
                var controller = left ? XRController.leftHand : XRController.rightHand;
                if (controller != null && controller.isTracked.isPressed)
                {
                    var position = controller.TryGetChildControl<Vector3Control>("pointerPosition");
                    var rotation = controller.TryGetChildControl<QuaternionControl>("pointerRotation");
                    if (position != null && rotation != null)
                    {
                        var raw = new Pose(position.ReadValue(), rotation.ReadValue());
                        if (raw.IsFinite) { pose = TrackingSpaceAnchor.FromHead(head, camera).ToWorld(raw); return true; }
                    }
                }
            }
            catch { /* Use the chosen controller's grip pose below. */ }
        }
        return TryWorld(device, camera, false, out pose);
    }
    /// <summary>
    /// Unity XRデバイスの接続・追跡・位置・回転を確認し、有効な生姿勢だけを返します。
    /// </summary>
    /// <param name="device">姿勢または入力を読み取るデバイス。</param>
    /// <param name="pose">成功時に、Unity XRの追跡空間での生姿勢を受け取ります。</param>
    /// <returns>有限な位置と有効な回転を取得できた場合はtrue。</returns>
    private static bool TryRead(InputDevice device, out Pose pose)
    {
        pose = default;
        if (!device.isValid ||
            (device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && !tracked) ||
            !device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position) ||
            !device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation)) return false;
        pose = new Pose(position, rotation);
        if (pose.IsFinite) return true;
        pose = default; return false;
    }
}
