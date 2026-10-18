using UnityEngine;

namespace SynthRidersFullBody;

/// <summary>HMDの姿勢を校正時の頭ボーンへ合わせ、ゲームの中間目標の更新停止に依存せず首を駆動します。</summary>
internal sealed class CalibratedHeadTarget
{
    private RigidPoseOffset? _offset;
    private Transform? _headset, _nativeTarget, _target;
    private Func<Transform?>? _read;
    private Action<Transform?>? _write;

    /// <summary>校正済みの頭オフセットがあるかを示します。</summary>
    public bool Calibrated => _offset.HasValue;
    /// <summary>接続先が元の目標か所有目標で、HMDと復元先が生存しているかを確認します。</summary>
    public bool OwnsBinding => _target is null ||
        (_target != null && _headset != null && _nativeTarget != null &&
            (_read!() == _nativeTarget || _read() == _target));

    /// <summary>固定した基準姿勢で頭入力から頭ボーンへの位置・回転を記録します。</summary>
    /// <param name="source">校正時のHMD姿勢。追加の頭トラッカー使用時はnull。</param>
    /// <param name="bone">固定プレビューの頭ボーン姿勢。</param>
    public void Capture(Pose? source, Pose bone)
    {
        Detach();
        _offset = source is { IsFinite: true } input && bone.IsFinite
            ? RigidPoseOffset.Between(N(input.Position), N(input.Rotation), N(bone.Position), N(bone.Rotation)) : null;
    }

    /// <summary>基準の頭ボーン位置に目標を作成します。復帰時の現在姿勢はルート校正へ混ぜません。</summary>
    /// <param name="bone">校正時の頭ボーン。</param>
    /// <param name="headset">現在のシーンのHMD。ゲームの頭目標とは独立に追跡姿勢を読みます。</param>
    /// <param name="read">ソルバーの頭目標を読む処理。</param>
    /// <param name="write">ソルバーの頭目標を接続する処理。</param>
    public void Attach(Transform bone, Transform? headset, Func<Transform?> read, Action<Transform?> write)
    {
        Detach();
        if (!_offset.HasValue || headset == null || read() is not { } source || source == null) return;
        _headset = headset;
        _nativeTarget = source;
        _read = read;
        _write = write;
        var go = new GameObject("SynthRidersFullBody.CalibratedHeadTarget");
        _target = go.transform;
        UnityEngine.Object.DontDestroyOnLoad(go);
        _target.SetPositionAndRotation(bone.position, bone.rotation);
        write(_target);
    }

    /// <summary>入力の移動・首振りを剛体オフセットへ反映し、停止時には元の目標へ戻します。</summary>
    /// <param name="active">腰追跡を含むMOD駆動が有効か。</param>
    /// <param name="blend">追跡復帰時の補正適用率。</param>
    public void Update(bool active, float blend)
    {
        if (!OwnsBinding || _target == null || _headset == null || !_offset.HasValue) return;
        var input = new Pose(_headset.position, _headset.rotation);
        if (!active || !input.IsFinite || !float.IsFinite(blend)) { Restore(); return; }
        var pose = _offset.Value.Apply(N(input.Position), N(input.Rotation));
        // Native targets may be frozen after a scene change, or the game may write
        // directly to the currently bound target. Only use them for the fade-in.
        var previous = new Pose(_nativeTarget!.position, _nativeTarget.rotation);
        if (!previous.IsFinite) previous = new Pose(U(pose.Position), U(pose.Rotation));
        _target.SetPositionAndRotation(Vector3.Lerp(previous.Position, U(pose.Position), Mathf.Clamp01(blend)),
            Quaternion.Slerp(previous.Rotation, U(pose.Rotation), Mathf.Clamp01(blend)));
        _write!(_target);
    }

    /// <summary>このクラスの目標が接続されている場合だけゲームの目標へ戻します。</summary>
    public void Restore()
    {
        if (_target is not null && _read != null && _read() == _target) _write!(_nativeTarget);
    }

    /// <summary>ゲーム目標を復元して所有オブジェクトを解放します。校正値は再接続まで保持します。</summary>
    public void Detach()
    {
        try { Restore(); }
        finally
        {
            try { if (_target != null) UnityEngine.Object.Destroy(_target.gameObject); }
            finally
            {
                _target = _nativeTarget = _headset = null;
                _read = null;
                _write = null;
            }
        }
    }

    /// <summary>Unityの位置を計算用の同じ座標値へ変換します。</summary>
    private static System.Numerics.Vector3 N(Vector3 value) => new(value.x, value.y, value.z);
    /// <summary>Unityの回転を計算用の同じ成分へ変換します。</summary>
    private static System.Numerics.Quaternion N(Quaternion value) => new(value.x, value.y, value.z, value.w);
    /// <summary>計算した位置をUnityへ戻します。</summary>
    private static Vector3 U(System.Numerics.Vector3 value) => new(value.X, value.Y, value.Z);
    /// <summary>計算した回転をUnityへ戻します。</summary>
    private static Quaternion U(System.Numerics.Quaternion value) => new(value.X, value.Y, value.Z, value.W);
}
