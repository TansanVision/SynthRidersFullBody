using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using UnityEngine;
using Valve.VR;

namespace SynthRidersFullBody;

// OpenVR sees SteamVR generic trackers even when the game's OpenXR input list does not.
/// <summary>
/// OpenVRからSteamVRのHMD・コントローラー・汎用トラッカーを列挙し、姿勢と入力を取得します。
/// </summary>
internal sealed class SteamVrTracking
{
    private readonly TrackedDevicePose_t[] _poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
    private readonly List<TrackingDevice> _devices = new();
    private CVRSystem? _system;
    private float _nextInit;
    private string _error = "Not connected";
    private int _sampleFrame = -1;
    /// <summary>
    /// 直近の探索で接続が確認された追跡デバイスの一覧です。
    /// </summary>
    public IReadOnlyList<TrackingDevice> Devices => _devices;
    /// <summary>
    /// SteamVRデバイスの識別・追跡状態、または初期化エラーを示す診断文です。
    /// </summary>
    public string DeviceSummary { get; private set; } = "Not connected";
    /// <summary>
    /// キャッシュされたOpenVR姿勢でHMDを有効に追跡できているかを示します。
    /// </summary>
    public bool HmdPresent => TryPose(0, out _);

    /// <summary>
    /// 同じフレームでの重複取得を避け、立位原点の全デバイス姿勢をキャッシュします。
    /// </summary>
    public void Sample()
    {
        if (_system == null || _sampleFrame == Time.frameCount) return;
        _system.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0f, _poses);
        _sampleFrame = Time.frameCount;
    }

    /// <summary>
    /// OpenVRを必要に応じて初期化し、接続中デバイスと診断情報を再構築します。
    /// </summary>
    public void Refresh()
    {
        _devices.Clear();
        if (!EnsureSystem()) { DeviceSummary = _error; return; }
        _sampleFrame = -1;
        Sample();
        var lines = new List<string>();
        for (uint index = 0; index < _poses.Length; index++)
        {
            if (!_poses[index].bDeviceIsConnected) continue;
            var kind = _system!.GetTrackedDeviceClass(index);
            var name = Property(index, ETrackedDeviceProperty.Prop_ModelNumber_String);
            var serial = Property(index, ETrackedDeviceProperty.Prop_SerialNumber_String);
            var hasPose = TryPose(index, out _);
            lines.Add($"{kind}: {name} | index={index} | serial={serial} | pose={(hasPose ? "yes" : "no")} | result={_poses[index].eTrackingResult}");
            var deviceKind = kind == ETrackedDeviceClass.HMD ? "HMD" :
                kind == ETrackedDeviceClass.Controller ? "Controller" :
                kind == ETrackedDeviceClass.GenericTracker ? "Tracker" : "";
            if (deviceKind.Length == 0) continue;
            var side = kind == ETrackedDeviceClass.Controller
                ? _system.GetControllerRoleForTrackedDeviceIndex(index).ToString() : "";
            _devices.Add(new TrackingDevice("SteamVR", index.ToString(), name, serial, deviceKind, side));
        }
        DeviceSummary = lines.Count == 0 ? "<no connected SteamVR devices>" : string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// 文字列のデバイス番号を解決し、このフレームの追跡姿勢を取得します。
    /// </summary>
    /// <param name="id">取得元APIで使用するデバイス番号の文字列。</param>
    /// <param name="pose">成功時に、Unityの軸方向へ変換したOpenVR立位追跡空間の姿勢を受け取ります。</param>
    /// <returns>番号とキャッシュされた姿勢が有効な場合はtrue。</returns>
    public bool TryRead(string id, out Pose pose)
    {
        Sample();
        pose = default;
        return uint.TryParse(id, out var index) && TryPose(index, out pose);
    }

    /// <summary>
    /// OpenVRのHMD番号0から、追跡空間内の姿勢を取得します。
    /// </summary>
    /// <param name="pose">成功時に、Unityの軸方向へ変換したOpenVR立位追跡空間のHMD姿勢を受け取ります。</param>
    /// <returns>HMDの有効な姿勢を取得できた場合はtrue。</returns>
    public bool TryGetRawHeadPose(out Pose pose) => TryRead("0", out pose);

    /// <summary>
    /// 接続・姿勢有効フラグとOpenVRの追跡状態を、再位置合わせなどの切り分け用に表示します。
    /// </summary>
    /// <param name="id">取得元APIのデバイス番号。</param>
    /// <returns>このフレームの追跡状態。番号が不正ならunknown device。</returns>
    public string PoseStatus(string id)
    {
        Sample();
        if (!uint.TryParse(id, out var index) || index >= _poses.Length) return "unknown device";
        var pose = _poses[index];
        return $"connected={pose.bDeviceIsConnected}, valid={pose.bPoseIsValid}, result={pose.eTrackingResult}";
    }

    /// <summary>
    /// 指定コントローラーのSteamVRトリガーボタン状態を読み取ります。
    /// </summary>
    /// <param name="id">取得元APIで使用するデバイス番号の文字列。</param>
    /// <returns>有効なコントローラー状態でトリガーが押されていればtrue。</returns>
    public bool Trigger(string id)
    {
        if (_system == null || !uint.TryParse(id, out var index)) return false;
        var state = new VRControllerState_t();
        return _system.GetControllerState(index, ref state, (uint)Marshal.SizeOf<VRControllerState_t>()) &&
            (state.ulButtonPressed & (1UL << (int)EVRButtonId.k_EButton_SteamVR_Trigger)) != 0;
    }

    /// <summary>選択したOpenVRコントローラーの主・副・グリップ入力を3ビットで取得します。</summary>
    /// <param name="id">コントローラーの現在のデバイス番号。</param>
    /// <returns>押下ビットと対応ボタンのビット。読み取れない場合は両方0です。</returns>
    public (int Pressed, int Available) ExpressionButtons(string id)
    {
        if (_system == null || !uint.TryParse(id, out var index)) return default;
        var state = new VRControllerState_t();
        if (!_system.GetControllerState(index, ref state, (uint)Marshal.SizeOf<VRControllerState_t>())) return default;
        var error = ETrackedPropertyError.TrackedProp_Success;
        var supported = _system.GetUint64TrackedDeviceProperty(index,
            ETrackedDeviceProperty.Prop_SupportedButtons_Uint64, ref error);
        if (error != ETrackedPropertyError.TrackedProp_Success) return default;
        var primary = (supported & (1UL << (int)EVRButtonId.k_EButton_A)) != 0
            ? EVRButtonId.k_EButton_A : EVRButtonId.k_EButton_SteamVR_Touchpad;
        var pressed = 0;
        var available = 0;
        for (var i = 0; i < 3; i++)
        {
            var button = i == 0 ? primary : i == 1 ? EVRButtonId.k_EButton_ApplicationMenu : EVRButtonId.k_EButton_Grip;
            var mask = 1UL << (int)button;
            if ((supported & mask) == 0) continue;
            available |= 1 << i;
            if ((state.ulButtonPressed & mask) != 0) pressed |= 1 << i;
        }
        return (pressed, available);
    }

    /// <summary>
    /// 再試行間隔を守りながら、OpenVRをバックグラウンドアプリとして初期化します。
    /// </summary>
    /// <returns>OpenVRシステムを利用できる場合はtrue。</returns>
    private bool EnsureSystem()
    {
        if (_system != null) return true;
        if (Time.unscaledTime < _nextInit) return false;
        _nextInit = Time.unscaledTime + 5f;
        try
        {
            if (!OpenVrNative.TryPrepare(out _error)) return false;
            var error = EVRInitError.None;
            _system = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Background);
            if (_system != null && error == EVRInitError.None) return true;
            _error = $"OpenVR initialization: {error}";
            _system = null;
        }
        catch (Exception exception)
        {
            _error = $"OpenVR unavailable: {exception.GetType().Name}: {exception.Message}";
            _system = null;
        }
        return false;
    }

    /// <summary>
    /// OpenVRデバイスの文字列プロパティを読み取ります。
    /// </summary>
    /// <param name="index">OpenVRの追跡デバイス番号。</param>
    /// <param name="property">取得するOpenVR文字列プロパティの識別子。</param>
    /// <returns>取得した文字列。OpenVRがエラーを返した場合は空文字列。</returns>
    private string Property(uint index, ETrackedDeviceProperty property)
    {
        var value = new StringBuilder(256);
        var error = ETrackedPropertyError.TrackedProp_Success;
        _system!.GetStringTrackedDeviceProperty(index, property, value, (uint)value.Capacity, ref error);
        return error == ETrackedPropertyError.TrackedProp_Success ? value.ToString() : "";
    }

    /// <summary>
    /// 接続と姿勢の有効性を確認し、OpenVRの行列をUnity向きの位置・回転へ変換します。
    /// </summary>
    /// <param name="index">OpenVRの追跡デバイス番号。</param>
    /// <param name="pose">成功時に、Unityの軸方向へ変換したOpenVR立位追跡空間の姿勢を受け取ります。</param>
    /// <returns>有限で有効な姿勢を構築できた場合はtrue。</returns>
    private bool TryPose(uint index, out Pose pose)
    {
        pose = default;
        if (index >= _poses.Length || !_poses[index].bDeviceIsConnected || !_poses[index].bPoseIsValid)
            return false;
        var matrix = _poses[index].mDeviceToAbsoluteTracking;
        var position = new Vector3(matrix.m3, matrix.m7, -matrix.m11);
        var forward = new Vector3(-matrix.m2, -matrix.m6, matrix.m10);
        var up = new Vector3(matrix.m1, matrix.m5, -matrix.m9);
        if (!float.IsFinite(position.x) || !float.IsFinite(position.y) ||
            !float.IsFinite(position.z) || !float.IsFinite(forward.x) ||
            !float.IsFinite(forward.y) || !float.IsFinite(forward.z) ||
            !float.IsFinite(up.x) || !float.IsFinite(up.y) || !float.IsFinite(up.z) ||
            forward.sqrMagnitude < 0.01f || up.sqrMagnitude < 0.01f ||
            Vector3.Cross(forward, up).sqrMagnitude < 0.0001f) return false;
        pose = new Pose(position, Quaternion.LookRotation(forward, up));
        if (pose.IsFinite) return true;
        pose = default;
        return false;
    }

}

/// <summary>
/// SteamVRランタイムのネイティブDLLを探し、OpenVRの呼び出し先を解決します。
/// </summary>
internal static class OpenVrNative
{
    private static bool _prepared;

    /// <summary>
    /// openvr_api.dllをロードし、生成済みOpenVRバインディングのDLL解決へ登録します。
    /// </summary>
    /// <param name="error">失敗理由を受け取ります。成功時は空文字列です。</param>
    /// <returns>準備済み、またはロードと登録に成功した場合はtrue。</returns>
    public static bool TryPrepare(out string error)
    {
        if (_prepared) { error = ""; return true; }
        try
        {
            var handle = IntPtr.Zero;
            if (!NativeLibrary.TryLoad("openvr_api", out handle))
            {
                foreach (var path in CandidatePaths())
                    if (File.Exists(path) && NativeLibrary.TryLoad(path, out handle)) break;
            }
            if (handle == IntPtr.Zero)
            {
                error = "openvr_api.dll not found; check SteamVR installation";
                return false;
            }
            NativeLibrary.SetDllImportResolver(typeof(OpenVR).Assembly,
                (name, _, _) => name.Equals("openvr_api", StringComparison.OrdinalIgnoreCase)
                    ? handle : IntPtr.Zero);
            _prepared = true;
            error = "";
            return true;
        }
        catch (Exception exception)
        {
            error = $"Could not load openvr_api.dll: {exception.Message}";
            return false;
        }
    }

    /// <summary>
    /// 環境変数・OpenVRの設定ファイル・標準インストール先からDLL候補を列挙します。
    /// </summary>
    /// <returns>優先順に列挙したWindows 64bit用DLLパス。</returns>
    private static IEnumerable<string> CandidatePaths()
    {
        var runtime = Environment.GetEnvironmentVariable("VR_OVERRIDE");
        if (!string.IsNullOrWhiteSpace(runtime))
            yield return Path.Combine(runtime, "bin", "win64", "openvr_api.dll");

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var pathsFile = Path.Combine(localAppData, "openvr", "openvrpaths.vrpath");
        if (File.Exists(pathsFile))
        {
            string[] roots;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(pathsFile));
                roots = document.RootElement.GetProperty("runtime").EnumerateArray()
                    .Select(x => x.GetString()).OfType<string>().ToArray();
            }
            catch { roots = Array.Empty<string>(); }
            foreach (var root in roots)
                yield return Path.Combine(root, "bin", "win64", "openvr_api.dll");
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        yield return Path.Combine(programFiles, "Steam", "steamapps", "common", "SteamVR",
            "bin", "win64", "openvr_api.dll");
    }
}
