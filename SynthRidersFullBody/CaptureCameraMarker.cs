using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace SynthRidersFullBody;

/// <summary>常時描画されるRendererを持たず、球と文字のメッシュをHMDだけに描画します。</summary>
internal sealed class CaptureCameraMarker : IDisposable
{
    private Mesh? _sphere;
    private Material? _sphereMaterial;
    private readonly List<(Mesh Mesh, Material Material)> _label = new();
    private Bounds _labelBounds;
    private float _nextLabelAttempt;
    private int _labelRevision = -1;
    /// <summary>文字の生成状態です。文字が生成できない場合も球は描画します。</summary>
    public string LabelStatus { get; private set; } = "pending";

    /// <summary>距離に応じた大きさの球と正面向きのラベルを、このフレームのHMDへ描画します。</summary>
    public bool Draw(Camera headset, Camera capture, Vector3 position)
    {
        EnsureSphere();
        EnsureLabel();
        var fromHead = position - headset.transform.position;
        var size = Mathf.Clamp(fromHead.magnitude / 1.5f, 1f, 4f);
        var facing = Quaternion.LookRotation(fromHead.sqrMagnitude > 0.001f ? fromHead.normalized :
            headset.transform.forward, Vector3.up);
        if (!CameraGuideDraw.Submit(headset, capture, _sphere, _sphereMaterial,
                Matrix4x4.TRS(position, facing, Vector3.one * (0.20f * size)))) return false;
        var textScale = 0.65f * size / Mathf.Max(_labelBounds.size.x, 0.001f);
        var textMatrix = Matrix4x4.TRS(position + Vector3.up * (0.25f * size), facing,
            Vector3.one * textScale) * Matrix4x4.Translate(-_labelBounds.center);
        foreach (var part in _label)
            CameraGuideDraw.Submit(headset, capture, part.Mesh, part.Material, textMatrix);
        return true;
    }

    /// <summary>非表示の一時球から所有メッシュを複製し、ゲームの描画方式に合う非ライティング材質を用意します。</summary>
    private void EnsureSphere()
    {
        if (_sphere != null && _sphereMaterial != null) return;
        DestroySphere();
        var source = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        source.SetActive(false);
        try
        {
            _sphere = UnityEngine.Object.Instantiate(source.GetComponent<MeshFilter>().sharedMesh);
            _sphere.hideFlags = HideFlags.HideAndDontSave;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ??
                Shader.Find("Sprites/Default");
            if (shader == null) throw new InvalidOperationException("No unlit marker shader is available.");
            _sphereMaterial = new Material(shader);
            _sphereMaterial.hideFlags = HideFlags.HideAndDontSave;
            var cyan = new Color(0.06f, 0.87f, 0.94f, 1f);
            if (_sphereMaterial.HasProperty("_BaseColor")) _sphereMaterial.SetColor("_BaseColor", cyan);
            if (_sphereMaterial.HasProperty("_Color")) _sphereMaterial.SetColor("_Color", cyan);
            ConfigureMaterial(_sphereMaterial);
        }
        catch { DestroySphere(); throw; }
        finally { UnityEngine.Object.Destroy(source); }
    }

    /// <summary>ラベルをメッシュへ複製し、元の文字Rendererは即座に無効化して破棄します。</summary>
    private void EnsureLabel()
    {
        if (_labelRevision != UiLocalization.Revision)
        {
            DestroyLabel();
            _nextLabelAttempt = 0f;
            _labelRevision = UiLocalization.Revision;
        }
        if (_label.Count > 0 && _label.All(p => p.Mesh != null && p.Material != null)) return;
        if (Time.unscaledTime < _nextLabelAttempt) return;
        _nextLabelAttempt = Time.unscaledTime + 5f;
        DestroyLabel();
        GameObject? source = null;
        try
        {
            source = new GameObject("SynthRidersFullBody.CameraLabelMeshSource");
            var text = source.AddComponent<TextMeshPro>();
            text.renderer.enabled = false;
            if (text.font == null)
                text.font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f != null);
            if (text.font == null) { LabelStatus = "waiting for font"; return; }
            UiLocalization.ApplyFont(text, text.font);
            text.text = UiText.Get("CAPTURE CAMERA");
            text.fontSize = 30f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.rectTransform.sizeDelta = new Vector2(1000f, 100f);
            text.ForceMeshUpdate(true, true);
            _labelBounds = text.textBounds;
            var info = text.textInfo;
            var materials = text.fontSharedMaterials;
            for (var i = 0; i < info.materialCount && i < materials.Length; i++)
            {
                var mesh = info.meshInfo[i].mesh;
                if (mesh == null || mesh.vertexCount == 0 || materials[i] == null) continue;
                AddLabelPart(mesh, materials[i]);
            }
            LabelStatus = _label.Count > 0 ? "ready" : "font mesh empty";
        }
        catch (Exception exception)
        {
            DestroyLabel();
            LabelStatus = $"unavailable: {exception.GetType().Name}";
        }
        finally
        {
            if (source != null) { source.SetActive(false); UnityEngine.Object.Destroy(source); }
        }
    }

    /// <summary>文字の1部品を複製し、途中で失敗した場合も作成済みのリソースを解放します。</summary>
    private void AddLabelPart(Mesh mesh, Material source)
    {
        Mesh? copy = null;
        Material? material = null;
        try
        {
            copy = UnityEngine.Object.Instantiate(mesh);
            copy.hideFlags = HideFlags.HideAndDontSave;
            material = new Material(source);
            material.hideFlags = HideFlags.HideAndDontSave;
            ConfigureMaterial(material);
            _label.Add((copy, material));
        }
        catch
        {
            Release(copy);
            Release(material);
            throw;
        }
    }

    /// <summary>対応するシェーダーでは遮蔽を避け、ゲームの共有マテリアルを変更せず目印を描画します。</summary>
    private static void ConfigureMaterial(Material material)
    {
        material.renderQueue = 4000;
        foreach (var name in new[] { "_ZTest", "_ZTestMode" })
            if (material.HasProperty(name)) material.SetInt(name, (int)CompareFunction.Always);
        if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 0);
    }

    /// <summary>所有する球のメッシュと材質を解放します。</summary>
    private void DestroySphere()
    {
        Release(_sphere);
        Release(_sphereMaterial);
        _sphere = null; _sphereMaterial = null;
    }

    /// <summary>ラベル用に複製したメッシュと材質を解放します。ゲームのフォント材質は破棄しません。</summary>
    private void DestroyLabel()
    {
        foreach (var part in _label)
        {
            Release(part.Mesh);
            Release(part.Material);
        }
        _label.Clear();
    }

    /// <summary>Unity終了時に一部が破棄済みでも、残りの所有リソースの解放を継続します。</summary>
    private static void Release(UnityEngine.Object? resource)
    {
        try { if (resource != null) UnityEngine.Object.Destroy(resource); }
        catch { /* Native objects may already be destroyed during shutdown. */ }
    }

    /// <summary>この目印が所有する描画リソースを解放します。</summary>
    public void Dispose()
    {
        DestroySphere(); DestroyLabel();
        _nextLabelAttempt = 0f;
        LabelStatus = "pending";
    }
}
