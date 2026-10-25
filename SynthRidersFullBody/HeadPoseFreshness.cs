using System.Numerics;

namespace SynthRidersFullBody;

/// <summary>有効フラグが付いたまま一方のHMDだけが停止している疑いを検出します。追跡を自動停止しません。</summary>
internal sealed class HeadPoseFreshness
{
    private bool _hasSample;
    private Vector3 _raw, _reference;
    private Quaternion _rawRotation, _referenceRotation;
    private float _changedAt;
    /// <summary>入力喪失時に過去の比較を破棄します。</summary>
    public void Reset() => _hasSample = false;

    /// <summary>SteamVR姿勢が1秒以上変わらず、ゲーム側HMDだけが2cmまたは5度以上動いた場合に通知します。</summary>
    public bool Update(Vector3 raw, Quaternion rotation, Vector3 reference, Quaternion referenceRotation, float now)
    {
        if (!_hasSample || Vector3.Distance(raw, _raw) > .00001f ||
            MathF.Abs(Quaternion.Dot(rotation, _rawRotation)) < .9999999f)
        {
            _hasSample = true; _raw = raw; _rawRotation = rotation;
            _reference = reference; _referenceRotation = referenceRotation; _changedAt = now;
            return false;
        }
        return now - _changedAt >= 1f && (Vector3.Distance(reference, _reference) > .02f ||
            MathF.Abs(Quaternion.Dot(referenceRotation, _referenceRotation)) < MathF.Cos(2.5f * MathF.PI / 180));
    }
}
