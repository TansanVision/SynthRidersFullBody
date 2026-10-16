using Il2CppSynth.Avatars;
using UnityEngine;

namespace SynthRidersFullBody;

/// <summary>配信用カメラの位置を、HMDを描画先に指定した球とラベルで表示します。</summary>
internal sealed class CaptureCameraGuide
{
    private readonly CaptureCameraMarker _marker = new();
    private Camera? _captureCamera;
    private float _nextCameraScan;
    private float _nextDrawAttempt;

    /// <summary>現在の配信用カメラが見つかったかを示します。描画結果はErrorも確認してください。</summary>
    public bool HasCaptureCamera { get; private set; }
    /// <summary>目印を表示できない理由。正常時はnullです。</summary>
    public string? Error { get; private set; }
    /// <summary>選択モード・カメラ名・個体ID、または待機理由です。</summary>
    public string Status { get; private set; } = "OFF";
    /// <summary>HMDから見た目印の方向と距離です。</summary>
    public string Location { get; private set; } = "";
    /// <summary>対象・描画エラー・文字の準備状態の変化をログへ通知するためのキーです。</summary>
    public string DiagnosticKey => $"{Status}|{Error}|{_marker.LabelStatus}";

    /// <summary>選択モード、対象個体、HMDと全カメラの階層・描画先をログへ列挙します。</summary>
    public string Diagnostics(Camera headset) =>
        $"mode={CaptureCameraResolver.CurrentMode() ?? "unknown"}, render=explicit HMD camera, " +
        $"headset={CaptureCameraResolver.Identity(headset)}, target={CaptureCameraResolver.Identity(_captureCamera)}, " +
        $"label={_marker.LabelStatus}, error={Error ?? "none"}; " +
        string.Join("; ", Resources.FindObjectsOfTypeAll<Camera>()
            .Where(c => c != null && c.gameObject.scene.IsValid())
            .Select(c => $"{CaptureCameraResolver.Identity(c)}[path={HierarchyPath(c.transform)}, " +
                $"active={c.gameObject.activeInHierarchy}, enabled={c.enabled}, stereo={c.stereoEnabled}, " +
                $"type={c.cameraType}, texture={(c.targetTexture != null ? c.targetTexture.name : "screen")}, " +
                $"position={c.transform.position}, mask={c.cullingMask:X8}]"));

    /// <summary>同名のカメラも区別できるよう、親を含むオブジェクトのパスを返します。</summary>
    private static string HierarchyPath(Transform transform)
    {
        var names = new List<string>();
        for (var node = transform; node != null; node = node.parent) names.Add(node.name);
        names.Reverse();
        return string.Join("/", names);
    }

    /// <summary>シーン変更後に古いカメラ参照を破棄し、次のフレームで再探索します。</summary>
    public void RequestRescan()
    {
        Hide();
        _captureCamera = null;
        HasCaptureCamera = false;
        Error = null;
        _nextDrawAttempt = 0f;
    }

    /// <summary>出力カメラを再確認し、その現在位置へHMD専用の描画を毎フレーム提出します。</summary>
    public void Update(Camera headsetCamera, bool enabled)
    {
        if (!enabled)
        {
            Status = "OFF";
            RequestRescan();
            return;
        }
        if (Time.unscaledTime >= _nextCameraScan ||
            (_captureCamera != null && !_captureCamera.gameObject.activeInHierarchy))
        {
            _nextCameraScan = Time.unscaledTime + 0.25f;
            _captureCamera = CaptureCameraResolver.Find(headsetCamera, out var status);
            Status = status;
        }
        HasCaptureCamera = _captureCamera != null;
        Location = "";
        if (_captureCamera == null) { Error = null; return; }
        var loader = AvatarLoader.s_instance;
        if (!headsetCamera.stereoEnabled && (loader == null || loader.m_HMDCamera != headsetCamera))
        {
            Error = "Headset camera could not be identified for the camera guide.";
            return;
        }
        var markerPosition = _captureCamera.transform.position;
        var fromHead = markerPosition - headsetCamera.transform.position;
        var local = headsetCamera.transform.InverseTransformDirection(fromHead);
        var direction = local.z < 0f ? (local.x < 0f ? "behind / left" : "behind / right") :
            local.x < -0.4f ? "left" : local.x > 0.4f ? "right" : "ahead";
        Location = $"Cyan sphere: {direction}, {fromHead.magnitude:0.0} m";
        if (Time.unscaledTime < _nextDrawAttempt) return;
        Error = null;
        try
        {
            if (!_marker.Draw(headsetCamera, _captureCamera, markerPosition))
                Error = "Headset camera has no drawable view for the camera guide.";
        }
        catch (Exception exception)
        {
            Error = $"Camera marker rendering failed: {exception.GetType().Name}: {exception.Message}";
            _nextDrawAttempt = Time.unscaledTime + 5f;
        }
    }

    /// <summary>次フレーム以降の再探索を予約します。目印は1フレーム分の描画なので常駐表示はありません。</summary>
    public void Hide()
    {
        Location = "";
        _nextCameraScan = 0f;
    }

    /// <summary>目印用に所有しているメッシュ・マテリアルとカメラ参照を解放します。</summary>
    public void Dispose()
    {
        RequestRescan();
        _marker.Dispose();
    }
}
