using Il2CppSynth.Avatars;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace SynthRidersFullBody;

// A world-space marker on a private layer rendered by the headset cameras only.
/// <summary>
/// 配信用カメラの位置を、専用レイヤーの球とラベルでHMD内に表示します。
/// </summary>
internal sealed class CaptureCameraGuide
{
    private readonly Dictionary<int, (Camera Camera, bool HadLayer)> _masks = new();
    private GameObject? _marker;
    private GameObject? _beacon;
    private Material? _beaconMaterial;
    private readonly List<Camera> _knownCameras = new();
    private Transform? _target;
    private Camera? _captureCamera;
    private int _layer = -1;
    private float _nextCameraScan;
    private float _nextMaskScan;

    /// <summary>
    /// 直近の探索でガイド対象カメラのTransformが見つかったかを示します。
    /// </summary>
    /// <remarks>検出済みでも、HMDの識別や専用レイヤーの確保に失敗して表示できないことがあります。Errorも確認してください。</remarks>
    public bool HasCaptureCamera { get; private set; }
    /// <summary>
    /// ガイドを表示できない理由。表示条件に問題がなければnullです。
    /// </summary>
    public string? Error { get; private set; }
    /// <summary>
    /// カメラ検出結果、待機理由、またはOFF状態を示す診断文です。
    /// </summary>
    public string Status { get; private set; } = "OFF";
    /// <summary>
    /// HMDから見た対象カメラの左右・前後方向と距離を示します。
    /// </summary>
    public string Location { get; private set; } = "";

    /// <summary>
    /// HMDとシーン内カメラの有効状態・位置・描画マスクをログ用に列挙します。
    /// </summary>
    /// <param name="headset">HMD用として識別したカメラ。</param>
    /// <returns>表示不具合の調査に使用するカメラ情報。</returns>
    public string Diagnostics(Camera headset) => $"headset={headset.name}, guide layer={_layer}; " +
        string.Join("; ", Resources.FindObjectsOfTypeAll<Camera>()
            .Where(c => c != null && c.gameObject.scene.IsValid()).Take(24)
            .Select(c => $"{c.name}[active={c.gameObject.activeInHierarchy}, enabled={c.enabled}, " +
                $"stereo={c.stereoEnabled}, type={c.cameraType}, position={c.transform.position}, mask={c.cullingMask:X8}]"));

    /// <summary>
    /// 現在の表示とカメラ参照を解除し、次回の更新で再探索します。
    /// </summary>
    public void RequestRescan()
    {
        Hide();
        _target = null;
        _captureCamera = null;
        _nextCameraScan = 0f;
        HasCaptureCamera = false;
        Location = "";
        if (_layer < 0) _layer = -1;
        Error = null;
    }

    /// <summary>
    /// 対象カメラの探索、マーカー位置・大きさ、およびHMD専用の描画マスクを更新します。
    /// </summary>
    /// <param name="headsetCamera">HMDへ描画する基準カメラ。</param>
    /// <param name="enabled">対象機能を有効にするか。</param>
    public void Update(Camera headsetCamera, bool enabled)
    {
        if (!enabled)
        {
            Status = "OFF";
            _nextCameraScan = 0f;
            HasCaptureCamera = false;
            Location = "";
            Error = null;
            Hide();
            return;
        }
        var target = FindCaptureTarget(headsetCamera);
        HasCaptureCamera = target != null;
        var headset = AvatarLoader.s_instance;
        if (!headsetCamera.stereoEnabled &&
            (headset == null || headset.m_HMDCamera != headsetCamera))
        {
            Error = "Headset camera could not be identified for the camera guide.";
            Hide();
            return;
        }
        if (target == null)
        {
            Location = "";
            Error = null;
            Hide();
            return;
        }
        Error = null;
        if (!EnsureMarker()) return;
        var markerPosition = target.position;
        var fromHead = markerPosition - headsetCamera.transform.position;
        var local = headsetCamera.transform.InverseTransformDirection(fromHead);
        var direction = local.z < 0f ? (local.x < 0f ? "behind / left" : "behind / right") :
            local.x < -0.4f ? "left" : local.x > 0.4f ? "right" : "ahead";
        Location = $"Cyan sphere: {direction}, {fromHead.magnitude:0.0} m";
        var size = Mathf.Clamp(fromHead.magnitude / 1.5f, 1f, 4f);
        _marker!.transform.localScale = Vector3.one * (0.001f * size);
        _beacon!.transform.localScale = Vector3.one * (0.16f * size);
        var facing = fromHead.sqrMagnitude > 0.001f ? fromHead.normalized :
            headsetCamera.transform.forward;
        _marker!.transform.SetPositionAndRotation(markerPosition + Vector3.up * (0.22f * size),
            Quaternion.LookRotation(facing, Vector3.up));
        _beacon!.transform.SetPositionAndRotation(markerPosition,
            Quaternion.LookRotation(facing, Vector3.up));
        ApplyCameraMasks(headsetCamera, _captureCamera);
        _marker.SetActive(true);
        _beacon.SetActive(true);
    }

    /// <summary>
    /// 球とラベルを隠し、変更したカメラの描画マスクを元に戻します。
    /// </summary>
    public void Hide()
    {
        try { if (_marker != null) _marker.SetActive(false); }
        catch { /* Scene teardown can destroy the marker first. */ }
        try { if (_beacon != null) _beacon.SetActive(false); }
        catch { /* Scene teardown can destroy the beacon first. */ }
        RestoreCameraMasks();
        _knownCameras.Clear();
        _nextMaskScan = 0f;
    }

    /// <summary>
    /// 描画マスクを戻してマーカーとマテリアルを破棄し、対象参照を解除します。
    /// </summary>
    public void Dispose()
    {
        Hide();
        DestroyMarkerObjects();
        _target = null;
        _captureCamera = null;
        HasCaptureCamera = false;
    }

    /// <summary>
    /// 所有する球・Canvas・マテリアルを破棄し、シーン破棄済みの参照も解除します。
    /// </summary>
    private void DestroyMarkerObjects()
    {
        try { if (_marker != null) UnityEngine.Object.Destroy(_marker); }
        catch { /* Scene teardown can destroy the marker first. */ }
        try { if (_beacon != null) UnityEngine.Object.Destroy(_beacon); }
        catch { /* Scene teardown can destroy the beacon first. */ }
        try { if (_beaconMaterial != null) UnityEngine.Object.Destroy(_beaconMaterial); }
        catch { /* Scene teardown can destroy the material first. */ }
        _marker = null;
        _beacon = null;
        _beaconMaterial = null;
    }

    /// <summary>
    /// 再探索間隔を守りながら、現在の画面出力用カメラのTransformを取得します。
    /// </summary>
    /// <param name="headsetCamera">HMDへ描画する基準カメラ。</param>
    /// <returns>ガイド対象のTransform。未検出や非アクティブならnull。</returns>
    private Transform? FindCaptureTarget(Camera headsetCamera)
    {
        // A camera can be replaced while the previous GameObject stays active.
        if (Time.unscaledTime < _nextCameraScan)
            return _target != null && _target.gameObject.activeInHierarchy ? _target : null;
        _nextCameraScan = Time.unscaledTime + 2f;
        _captureCamera = CaptureCameraResolver.Find(headsetCamera, out var status);
        _target = _captureCamera != null ? _captureCamera.transform : null;
        Status = status;
        return _target;
    }

    /// <summary>
    /// 未使用レイヤーを確保し、球とカメララベルを必要な場合に作成します。
    /// </summary>
    /// <returns>必要な表示オブジェクトを使用できる場合はtrue。</returns>
    private bool EnsureMarker()
    {
        if (_marker != null && _beacon != null) return true;
        Hide();
        DestroyMarkerObjects();
        if (_layer == -1) _layer = FindUnusedLayer();
        if (_layer < 0)
        {
            Error = "No unused Unity layer is available for a headset-only camera guide.";
            return false;
        }
        GameObject? marker = null;
        try
        {
            marker = VrUi.CreateCanvas("SynthRidersFullBody.CaptureCameraGuide",
                new Vector2(360f, 180f), 0.001f);
            _marker = marker;
            marker.SetActive(false);
            VrUi.Image(marker, "CameraBody", new Vector2(110f, 68f),
                new Vector2(0f, -5f), new Color(0.08f, 0.82f, 0.93f, 0.88f));
            VrUi.Image(marker, "Lens", new Vector2(38f, 50f),
                new Vector2(70f, -5f), new Color(0.88f, 0.10f, 0.72f, 0.95f));
            var label = VrUi.Text(marker, "Label", new Vector2(350f, 52f),
                new Vector2(0f, 62f), 30f, Color.white, TextAlignmentOptions.Center);
            label.text = "CAPTURE CAMERA";
            SetLayerRecursively(marker.transform, _layer);
            var beacon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _beacon = beacon;
            beacon.SetActive(false);
            beacon.name = "SynthRidersFullBody.CaptureCameraBeacon";
            beacon.transform.localScale = Vector3.one * 0.16f;
            beacon.layer = _layer;
            var renderer = beacon.GetComponent<Renderer>();
            if (renderer != null)
            {
                _beaconMaterial = renderer.material;
                var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
                if (shader != null) _beaconMaterial.shader = shader;
                _beaconMaterial.color = new Color(0.06f, 0.87f, 0.94f, 1f);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            var collider = beacon.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                UnityEngine.Object.Destroy(collider);
            }
            UnityEngine.Object.DontDestroyOnLoad(beacon);
        }
        catch
        {
            DestroyMarkerObjects();
            throw;
        }
        return true;
    }

    /// <summary>
    /// 既存オブジェクトに使用されず名前も付いていないユーザーレイヤーを探します。
    /// </summary>
    /// <returns>利用できるレイヤー番号。見つからない場合は-2。</returns>
    private static int FindUnusedLayer()
    {
        var occupied = new bool[32];
        foreach (var gameObject in Resources.FindObjectsOfTypeAll<GameObject>())
            if (gameObject != null) occupied[gameObject.layer] = true;
        for (var layer = 31; layer >= 8; layer--)
            if (!occupied[layer] && string.IsNullOrEmpty(LayerMask.LayerToName(layer)))
                return layer;
        return -2;
    }

    /// <summary>
    /// 指定Transformと全子孫をマーカー用レイヤーへ設定します。
    /// </summary>
    /// <param name="root">再帰的にレイヤーを設定する階層のルート。</param>
    /// <param name="layer">Unityのレイヤー番号。</param>
    private static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (var i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }

    /// <summary>
    /// 手動描画カメラも含めてマスクを更新し、HMD用の視点にだけガイドを描画させます。
    /// </summary>
    /// <param name="headsetCamera">HMDへ描画する基準カメラ。</param>
    /// <param name="captureCamera">ガイドを描画させない配信用カメラ。</param>
    private void ApplyCameraMasks(Camera headsetCamera, Camera? captureCamera)
    {
        var bit = 1 << _layer;
        if (Time.unscaledTime >= _nextMaskScan)
        {
            _nextMaskScan = Time.unscaledTime + 0.5f;
            _knownCameras.Clear();
            // Camera.allCameras omits disabled cameras used with Camera.Render().
            foreach (var camera in Resources.FindObjectsOfTypeAll<Camera>())
                if (camera != null && camera.gameObject.scene.IsValid()) _knownCameras.Add(camera);
            foreach (var id in _masks.Where(entry => entry.Value.Camera == null)
                         .Select(entry => entry.Key).ToArray())
                _masks.Remove(id);
        }
        foreach (var camera in _knownCameras)
            if (camera != null)
                SetCameraMask(camera, IsHeadsetView(camera, headsetCamera, captureCamera), bit);
        // Include enabled cameras created since the last scan immediately.
        foreach (var camera in Camera.allCameras)
        {
            if (camera == null) continue;
            SetCameraMask(camera, IsHeadsetView(camera, headsetCamera, captureCamera), bit);
        }
        SetCameraMask(headsetCamera, headsetCamera != captureCamera, bit);
        // These can be rendered manually while absent from Camera.allCameras.
        if (captureCamera != null) SetCameraMask(captureCamera, false, bit);
    }

    /// <summary>
    /// 配信用カメラを除外し、指定HMDまたは近接したステレオ視点かを確認します。
    /// </summary>
    /// <param name="camera">ガイド用レイヤーを描画させるか調べるカメラ。</param>
    /// <param name="headset">HMD用として識別したカメラ。</param>
    /// <param name="capture">ガイドの描画対象から除外する配信用カメラ。</param>
    /// <returns>ガイドの描画を許可するHMD視点ならtrue。</returns>
    private static bool IsHeadsetView(Camera camera, Camera headset, Camera? capture) =>
        camera != capture && (camera == headset || (camera.stereoEnabled && camera.targetTexture == null &&
            Vector3.Distance(camera.transform.position, headset.transform.position) < 0.25f &&
            Quaternion.Angle(camera.transform.rotation, headset.transform.rotation) < 15f));

    /// <summary>
    /// 元のビット状態を保存してから、ガイド用レイヤーの可視ビットだけを変更します。
    /// </summary>
    /// <param name="camera">ガイド用ビットを変更するカメラ。</param>
    /// <param name="visible">対象表示を有効にするか。</param>
    /// <param name="bit">ガイド用レイヤーを示す単一ビットのマスク。</param>
    private void SetCameraMask(Camera camera, bool visible, int bit)
    {
        var id = camera.GetInstanceID();
        if (!_masks.TryGetValue(id, out var entry) || entry.Camera != camera)
            _masks[id] = (camera, (camera.cullingMask & bit) != 0);
        var mask = visible ? camera.cullingMask | bit : camera.cullingMask & ~bit;
        if (camera.cullingMask != mask) camera.cullingMask = mask;
    }

    /// <summary>
    /// 保存したカメラごとのガイド用ビットを復元し、管理一覧を空にします。
    /// </summary>
    private void RestoreCameraMasks()
    {
        if (_layer < 0) return;
        var bit = 1 << _layer;
        foreach (var entry in _masks.Values)
            try
            {
                if (entry.Camera == null) continue;
                var mask = entry.HadLayer ? entry.Camera.cullingMask | bit :
                    entry.Camera.cullingMask & ~bit;
                if (entry.Camera.cullingMask != mask) entry.Camera.cullingMask = mask;
            }
            catch { /* Camera may have been destroyed. */ }
        _masks.Clear();
    }
}
