using UnityEngine;
using UnityEngine.Rendering;

namespace SynthRidersFullBody;

// A light skeleton overlay on the selected fixed pose, visible in the game's mirror.
// No copied Animator, scripts, lights or additional reflection camera.
/// <summary>
/// 固定したアバターの骨格線と手の目印を表示し、Tポーズ・直立の姿勢合わせを補助します。
/// </summary>
internal sealed class CalibrationGhostGuide
{
    private static readonly HumanBodyBones[][] Chains =
    {
        new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Head },
        new[] { HumanBodyBones.LeftHand, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftUpperArm,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand },
        new[] { HumanBodyBones.LeftFoot, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftUpperLeg,
            HumanBodyBones.Hips, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot }
    };
    private GameObject? _root;
    private Material? _material;
    private readonly List<LineRenderer> _lines = new();
    private readonly List<Transform> _hands = new();
    private Animator? _avatar;
    private Transform[][] _bones = Array.Empty<Transform[]>();

    /// <summary>
    /// 対象アバターの骨格線と計測した手の位置を、表示条件に応じて更新します。
    /// </summary>
    /// <param name="avatar">処理対象のアバター。</param>
    /// <param name="visible">対象表示を有効にするか。</param>
    /// <param name="hasHands">左右の手の姿勢を取得できているか。</param>
    /// <param name="left">左手の計測したワールド姿勢。</param>
    /// <param name="right">右手の計測したワールド姿勢。</param>
    public void Update(AvatarIK? avatar, bool visible, bool hasHands, Pose left, Pose right)
    {
        if (!visible || avatar == null) { Hide(); return; }
        EnsureCreated();
        if (_root == null) return;
        if (_avatar != avatar.Animator)
        {
            _avatar = avatar.Animator;
            _bones = Chains.Select(chain => chain.Select(avatar.Animator.GetBoneTransform)
                .Where(t => t != null).ToArray()).ToArray();
        }
        _root.SetActive(true);
        for (var i = 0; i < Chains.Length; i++)
        {
            var points = _bones[i];
            _lines[i].positionCount = points.Length;
            for (var j = 0; j < points.Length; j++) _lines[i].SetPosition(j, points[j].position);
        }
        for (var i = 0; i < _hands.Count; i++)
        {
            _hands[i].gameObject.SetActive(hasHands);
            if (hasHands) _hands[i].SetPositionAndRotation(i == 0 ? left.Position : right.Position,
                Quaternion.LookRotation(avatar.Animator.transform.forward, Vector3.up));
        }
    }

    /// <summary>
    /// 作成済みのゴースト表示を非表示にします。
    /// </summary>
    public void Hide() { if (_root != null) _root.SetActive(false); }

    /// <summary>
    /// 骨格線・手の目印・マテリアルを破棄し、参照を解除します。
    /// </summary>
    public void Dispose()
    {
        Hide();
        if (_root != null) UnityEngine.Object.Destroy(_root);
        if (_material != null) UnityEngine.Object.Destroy(_material);
        _root = null;
        _material = null;
        _lines.Clear();
        _hands.Clear();
        _avatar = null;
        _bones = Array.Empty<Transform[]>();
    }

    /// <summary>
    /// ゴースト表示のルート、骨格線、手の目印を必要な場合に作成します。
    /// </summary>
    private void EnsureCreated()
    {
        if (_root != null) return;
        var shader = Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("Sprites/Default");
        if (shader == null) return;
        _material = new Material(shader);
        _material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        _material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        _material.SetInt("_ZWrite", 0);
        _material.SetInt("_ZTest", (int)CompareFunction.Always);
        _material.SetInt("_Cull", (int)CullMode.Off);
        _material.renderQueue = 3100;
        _root = new GameObject("SynthRidersFullBody.CalibrationGhost");
        UnityEngine.Object.DontDestroyOnLoad(_root);
        for (var i = 0; i < Chains.Length; i++)
        {
            var child = new GameObject($"PoseGuide{i}");
            child.transform.SetParent(_root.transform, false);
            var line = child.AddComponent<LineRenderer>();
            line.sharedMaterial = _material;
            line.useWorldSpace = true;
            line.startWidth = line.endWidth = 0.018f;
            line.startColor = line.endColor = new Color(0.06f, 0.87f, 0.94f, 0.75f);
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            _lines.Add(line);
        }
        // Small rings show the real controller positions over the ghost hands.
        for (var i = 0; i < 2; i++)
        {
            var child = new GameObject($"LiveHand{i}");
            child.transform.SetParent(_root.transform, false);
            var ring = child.AddComponent<LineRenderer>();
            ring.sharedMaterial = _material;
            ring.useWorldSpace = false;
            ring.positionCount = 33;
            ring.startWidth = ring.endWidth = 0.012f;
            ring.startColor = ring.endColor = new Color(1f, 0.25f, 0.8f, 0.95f);
            ring.shadowCastingMode = ShadowCastingMode.Off;
            ring.receiveShadows = false;
            for (var j = 0; j <= 32; j++)
            {
                var angle = j * Mathf.PI * 2f / 32f;
                ring.SetPosition(j, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 0.05f);
            }
            _hands.Add(child.transform);
        }
    }
}
