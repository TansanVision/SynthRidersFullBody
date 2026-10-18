using UnityEngine;
using UnityEngine.Rendering;

namespace SynthRidersFullBody;

/// <summary>表示中のアバターを、選択中の配信カメラの描画時だけ固定バストショットにします。</summary>
internal sealed class MenuBustCamera : IDisposable
{
    private Camera? _headset, _capture;
    private Animator? _avatar;
    private bool _enabled, _posePending, _framed, _refreshFrame;
    private Renderer[] _renderers = Array.Empty<Renderer>();
    private float _nextRendererScan;
    private Vector3 _position, _scale;
    private Quaternion _rotation;
    private float _aspect, _nearClip;
    private float _nextScan, _retryAt;
    private string? _cameraMode;
    private Camera? _rendering;
    private Vector3 _savedLocalPosition;
    private Quaternion _savedLocalRotation;
    private float _savedFov;
    private Camera.CameraCallback? _before, _after;
    private Il2CppSystem.Action<ScriptableRenderContext, Camera>? _beginSrp, _endSrp;

    /// <summary>UI用の状態。実機でフックの動作を確認できるよう、初回描画までは待機を示します。</summary>
    public string Status { get; private set; } = "Fixed bust shot: OFF";
    /// <summary>イベント内の例外。描画を止めず、診断へ残します。</summary>
    public string? Error { get; private set; }
    /// <summary>設定値を含めず、動作中のカメラと固定位置をログへ記録します。</summary>
    public string Diagnostic => $"{Status}; camera={CaptureCameraResolver.Identity(_capture)}; position={_position}; error={Error ?? "none"}";

    /// <summary>通常描画とURPの両方のカメライベントを接続します。</summary>
    public void Initialize()
    {
        if (_before != null) return;
        try
        {
            _before = (Action<Camera>)BeforeRender;
            _after = (Action<Camera>)AfterRender;
            _beginSrp = (Action<ScriptableRenderContext, Camera>)((_, camera) => BeforeRender(camera));
            _endSrp = (Action<ScriptableRenderContext, Camera>)((_, camera) => AfterRender(camera));
            Camera.onPreCull += _before;
            Camera.onPostRender += _after;
            RenderPipelineManager.beginCameraRendering += _beginSrp;
            RenderPipelineManager.endCameraRendering += _endSrp;
        }
        catch { Dispose(); throw; }
    }

    /// <summary>設定と対象アバターを更新します。姿勢の揺れでは構図を取り直しません。</summary>
    /// <param name="headset">除外するHMDカメラ。</param>
    /// <param name="avatar">現在選択されたアバター。</param>
    /// <param name="enabled">共有設定のON/OFF。</param>
    /// <param name="posePending">校正プレビュー・復帰中は既存構図を維持し、終了後に構図を取り直します。</param>
    public void Update(Camera? headset, Animator? avatar, bool enabled, bool posePending)
    {
        Restore(); // Recover even if a previous render was aborted before its end callback.
        if (_avatar != avatar || _enabled != enabled) ResetFrame();
        if (_headset != headset) RequestRescan();
        if (_posePending && !posePending) _refreshFrame = true;
        _headset = headset;
        _avatar = avatar;
        _enabled = enabled;
        _posePending = posePending;
        if (!enabled) { ResetFrame(); Status = "Fixed bust shot: OFF"; return; }
        if (_before == null) { Status = "Fixed bust shot: unavailable (see log)"; return; }
        if (Time.unscaledTime < _retryAt) { Status = "Fixed bust shot: unavailable (see log)"; return; }
        if (MenuCameraContext.UseGameCamera())
        { _refreshFrame = true; Status = "Fixed bust shot: game camera during play"; return; }
        if (headset == null || avatar == null || !avatar.gameObject.activeInHierarchy)
        { ResetFrame(); Status = "Fixed bust shot: waiting for visible avatar"; return; }
        if (Time.unscaledTime >= _nextRendererScan)
        {
            _renderers = avatar.GetComponentsInChildren<Renderer>(true).ToArray();
            _nextRendererScan = Time.unscaledTime + 1f;
        }
        var mode = CaptureCameraResolver.CurrentMode();
        if (Time.unscaledTime >= _nextScan || _cameraMode != mode ||
            (_capture != null && !CaptureCameraResolver.IsStillSelected(_capture, _cameraMode)))
        {
            var capture = CaptureCameraResolver.Find(headset, out _);
            _capture = capture;
            _cameraMode = mode;
            _nextScan = Time.unscaledTime + .25f;
        }
        if (_capture == null) { Status = "Fixed bust shot: select a third-person camera"; return; }
        if (!CanDisplayAvatar(_capture)) { Status = "Fixed bust shot: waiting for visible avatar"; return; }
        if (posePending && !_framed) { Status = "Fixed bust shot: waiting for avatar pose"; return; }
        if (!_framed) Status = "Fixed bust shot: waiting for camera render";
    }

    /// <summary>プレイ外で表示可能なアバターへ構図を適用します。HMD・鏡・LIVは探索対象外です。</summary>
    private void BeforeRender(Camera camera)
    {
        try
        {
            if (!_enabled || Time.unscaledTime < _retryAt || _headset == null || _avatar == null ||
                camera == null || camera != _capture || camera == _headset || camera.stereoEnabled ||
                camera.orthographic || !CanDisplayAvatar(camera) || MenuCameraContext.UseGameCamera()) return;
            // Mode changes can happen after the MOD's LateUpdate. Verify the actual selected output again.
            if (!CaptureCameraResolver.IsStillSelected(camera, _cameraMode)) return;
            if (_rendering == camera) return; // Some pipelines announce the same render through both event sets.
            Restore();
            var scale = _avatar.transform.lossyScale;
            if (!_posePending && (!_framed || _refreshFrame || (scale - _scale).sqrMagnitude > .000001f ||
                Mathf.Abs(camera.aspect - _aspect) > .001f || Mathf.Abs(camera.nearClipPlane - _nearClip) > .001f))
            {
                if (TryFrame(camera))
                {
                    _scale = scale; _aspect = camera.aspect; _nearClip = camera.nearClipPlane;
                    _framed = true; _refreshFrame = false;
                }
            }
            if (!_framed) { Status = "Fixed bust shot: waiting for avatar pose"; return; }
            _savedLocalPosition = camera.transform.localPosition;
            _savedLocalRotation = camera.transform.localRotation;
            _savedFov = camera.fieldOfView;
            _rendering = camera;
            camera.transform.SetPositionAndRotation(_position, _rotation);
            camera.fieldOfView = MenuBustFraming.FieldOfView;
            Error = null;
            Status = "Fixed bust shot: fixed";
        }
        catch (Exception exception)
        {
            Restore(); ResetFrame();
            Error = $"{exception.GetType().Name}: {exception.Message}";
            _retryAt = Time.unscaledTime + 5f;
            Status = "Fixed bust shot: unavailable (see log)";
        }
    }

    /// <summary>配信カメラが描画可能なアバターメッシュがあるか確認します。HMDの非表示設定や前フレームの可視判定には依存しません。</summary>
    /// <param name="camera">選択中の配信用カメラ。</param>
    /// <returns>有効な描画部品がカメラのレイヤーマスクに含まれる場合はtrue。</returns>
    private bool CanDisplayAvatar(Camera camera)
    {
        if (_avatar == null || !_avatar.gameObject.activeInHierarchy) return false;
        foreach (var renderer in _renderers)
            if (renderer != null && renderer.enabled && !renderer.forceRenderingOff &&
                renderer.gameObject.activeInHierarchy && renderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly &&
                (camera.cullingMask & (1 << renderer.gameObject.layer)) != 0) return true;
        return false;
    }

    /// <summary>骨格の頭・胸・肩幅から正面の固定構図を一度だけ計算します。</summary>
    private bool TryFrame(Camera camera)
    {
        var head = _avatar!.GetBoneTransform(HumanBodyBones.Head);
        var hips = _avatar.GetBoneTransform(HumanBodyBones.Hips);
        var chest = _avatar.GetBoneTransform(HumanBodyBones.UpperChest) ?? _avatar.GetBoneTransform(HumanBodyBones.Chest);
        if (head == null || hips == null) return false;
        var chestPosition = chest != null ? chest.position : Vector3.Lerp(hips.position, head.position, .55f);
        var left = _avatar.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        var right = _avatar.GetBoneTransform(HumanBodyBones.RightUpperArm);
        var width = left != null && right != null ? Vector3.Distance(left.position, right.position) : 0f;
        if (!MenuBustFraming.TryCreate(N(head.position), N(chestPosition), N(hips.position), N(_avatar.transform.forward),
                width, camera.aspect, camera.nearClipPlane, out var position, out var focus)) return false;
        if (System.Numerics.Vector3.Distance(position, focus) + Vector3.Distance(head.position, hips.position) >= camera.farClipPlane)
            return false;
        _position = U(position);
        _rotation = Quaternion.LookRotation(U(focus - position), Vector3.up);
        return true;
    }

    /// <summary>該当カメラの描画が終わると、その直前のゲーム側位置・回転・画角へ戻します。</summary>
    private void AfterRender(Camera camera) { if (_rendering == camera) Restore(); }

    /// <summary>カメラの目印へ、実際の描画で使用する固定位置を渡します。</summary>
    /// <param name="camera">目印の対象カメラ。</param>
    /// <param name="position">固定位置。戻り値がfalseの場合は使用しません。</param>
    /// <returns>現在そのカメラに固定構図を適用できる場合はtrue。</returns>
    public bool TryGetPosition(Camera camera, out Vector3 position)
    {
        position = _position;
        return _enabled && Time.unscaledTime >= _retryAt && _framed && camera != null && camera == _capture &&
            CanDisplayAvatar(camera) && !MenuCameraContext.UseGameCamera() && CaptureCameraResolver.IsStillSelected(camera, _cameraMode);
    }

    /// <summary>描画中の一時変更を解除します。ゲームのカメラ制御コンポーネントは停止しません。</summary>
    private void Restore()
    {
        var camera = _rendering;
        _rendering = null;
        if (camera == null) return;
        try
        {
            camera.transform.localPosition = _savedLocalPosition;
            camera.transform.localRotation = _savedLocalRotation;
            camera.fieldOfView = _savedFov;
        }
        catch (Exception exception)
        {
            Error = $"Restore failed: {exception.GetType().Name}: {exception.Message}";
            _retryAt = Time.unscaledTime + 5f;
            Status = "Fixed bust shot: unavailable (see log)";
        }
    }

    /// <summary>シーンのカメラを再探索します。同じアバターが見えている間は既存構図を維持します。</summary>
    public void RequestRescan() { Restore(); _nextScan = _nextRendererScan = 0f; _refreshFrame = true; }

    /// <summary>アバター交換・非表示・設定OFF時に構図と描画部品の参照を破棄します。</summary>
    public void ResetFrame()
    {
        RequestRescan(); _framed = false; _capture = null; _renderers = Array.Empty<Renderer>();
    }

    /// <summary>全フックを解除し、途中の描画変更を復元します。</summary>
    public void Dispose()
    {
        ResetFrame();
        if (_before != null) Camera.onPreCull -= _before;
        if (_after != null) Camera.onPostRender -= _after;
        if (_beginSrp != null) RenderPipelineManager.beginCameraRendering -= _beginSrp;
        if (_endSrp != null) RenderPipelineManager.endCameraRendering -= _endSrp;
        _before = _after = null;
        _beginSrp = _endSrp = null;
        _enabled = false;
    }

    /// <summary>Unity座標を同じ成分の数値ベクトルへ変換します。</summary>
    private static System.Numerics.Vector3 N(Vector3 v) => new(v.x, v.y, v.z);
    /// <summary>構図計算の結果をUnity座標へ戻します。</summary>
    private static Vector3 U(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
}
