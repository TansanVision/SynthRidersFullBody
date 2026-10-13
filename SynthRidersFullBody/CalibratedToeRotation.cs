using UnityEngine;

namespace SynthRidersFullBody;

/// <summary>
/// 足先の独立した入力がない場合に、校正した足首と足先の相対回転を保ちます。
/// </summary>
internal sealed class CalibratedToeRotation
{
    private readonly Transform _foot;
    private readonly Transform? _toes;
    private Quaternion _relativeRotation;
    private bool _captured;

    /// <summary>
    /// 最後の適用前に存在した、校正時の相対回転からのずれを度数で示します。
    /// </summary>
    public float CorrectionDegrees { get; private set; }

    /// <summary>
    /// 足首と、その子孫にある任意の足先ボーンを保持します。
    /// </summary>
    /// <param name="foot">足首のボーン。</param>
    /// <param name="toes">足先のボーン。未登録・別系統の場合は補正しません。</param>
    public CalibratedToeRotation(Transform foot, Transform? toes)
    {
        _foot = foot;
        _toes = toes != null && toes != foot && toes.IsChildOf(foot) ? toes : null;
    }

    /// <summary>
    /// サイズ調整後の固定校正姿勢から、ボーン軸に依存しない相対回転を保存します。
    /// </summary>
    public void Capture()
    {
        _captured = false;
        CorrectionDegrees = 0f;
        if (_foot == null || _toes == null) return;
        _relativeRotation = Quaternion.Inverse(_foot.rotation) * _toes.rotation;
        _captured = true;
    }

    /// <summary>
    /// 片足1台の追跡中だけ、現在の足首に対して校正時の足先角度を適用します。
    /// </summary>
    /// <param name="tracked">この足の主入力が有効か。</param>
    /// <param name="independentToes">足首と足先に別々の校正済み入力があるか。</param>
    /// <param name="weight">追跡復帰とIK全体の重み。0以下では骨を変更しません。</param>
    /// <remarks>位置・スケール・足首回転は変更しません。中間ボーンがあっても足首基準で補正します。</remarks>
    public void Apply(bool tracked, bool independentToes, float weight)
    {
        CorrectionDegrees = 0f;
        if (!_captured || !tracked || independentToes || !float.IsFinite(weight) || weight <= 0f ||
            _foot == null || _toes == null) return;
        var target = _foot.rotation * _relativeRotation;
        CorrectionDegrees = Quaternion.Angle(_toes.rotation, target);
        _toes.rotation = Quaternion.Slerp(_toes.rotation, target, Mathf.Clamp01(weight));
    }
}
