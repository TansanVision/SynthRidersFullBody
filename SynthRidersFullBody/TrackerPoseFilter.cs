using UnityEngine;

namespace SynthRidersFullBody;

// The 1€ filter adapts its cutoff to motion speed: steady trackers are smoothed
// while fast movements retain a higher cutoff and less lag.
/// <summary>
/// 腰と左右足の位置・回転を独立して平滑化し、足の追跡喪失時には履歴を破棄します。
/// </summary>
internal sealed class TrackerPoseFilter
{
    private readonly AdaptivePoseFilter _waist = new();
    private readonly AdaptivePoseFilter _left = new();
    private readonly AdaptivePoseFilter _right = new();

    /// <summary>
    /// 腰と左右足の平滑化履歴を初期化します。
    /// </summary>
    public void Reset()
    {
        _waist.Reset();
        _left.Reset();
        _right.Reset();
    }

    /// <summary>
    /// 有効な入力へ速度適応フィルターを適用し、欠測した足の履歴と出力をリセットします。
    /// </summary>
    /// <param name="waist">腰のワールド姿勢。</param>
    /// <param name="left">左足のワールド姿勢。</param>
    /// <param name="right">右足のワールド姿勢。</param>
    /// <param name="hasLeft">左足の追跡姿勢が有効か。</param>
    /// <param name="hasRight">右足の追跡姿勢が有効か。</param>
    /// <param name="time">サンプルの取得時刻。単位は秒です。</param>
    /// <param name="config">使用する共有設定。保存処理がないメソッドではファイルへの書き込みは行いません。</param>
    /// <param name="smoothWaist">平滑化後の腰姿勢を受け取ります。</param>
    /// <param name="smoothLeft">平滑化後の左足姿勢を受け取ります。追跡喪失時は既定値です。</param>
    /// <param name="smoothRight">平滑化後の右足姿勢を受け取ります。追跡喪失時は既定値です。</param>
    public void Filter(Pose waist, Pose left, Pose right, bool hasLeft, bool hasRight,
        float time, ModConfig config,
        out Pose smoothWaist, out Pose smoothLeft, out Pose smoothRight)
    {
        smoothWaist = _waist.Filter(waist, time, config);
        if (!hasLeft) _left.Reset();
        if (!hasRight) _right.Reset();
        smoothLeft = hasLeft ? _left.Filter(left, time, config) : default;
        smoothRight = hasRight ? _right.Filter(right, time, config) : default;
    }

    /// <summary>
    /// 1個体の位置・回転に、速度に応じて遅延と平滑化を調整するフィルターを適用します。
    /// </summary>
    internal sealed class AdaptivePoseFilter
    {
        private bool _initialized;
        private float _lastTime;
        private Vector3 _lastPosition;
        private Vector3 _filteredPosition;
        private Vector3 _filteredVelocity;
        private Quaternion _lastRotation;
        private Quaternion _filteredRotation;
        private float _filteredAngularSpeed;

        /// <summary>
        /// 次のサンプルを新しい初期値として受け取れる状態へ戻します。
        /// </summary>
        public void Reset() => _initialized = false;

        /// <summary>
        /// 単発の跳びを制限した後、並進速度・角速度に応じたカットオフで姿勢を平滑化します。
        /// </summary>
        /// <param name="sample">現在時刻で取得した入力姿勢。</param>
        /// <param name="time">サンプルの取得時刻。単位は秒です。</param>
        /// <param name="config">使用する共有設定。保存処理がないメソッドではファイルへの書き込みは行いません。</param>
        /// <returns>平滑化した姿勢。初回・時刻逆転・長い欠測後は入力姿勢。</returns>
        /// <remarks>時刻差が0以下、または0.25秒を超える場合は入力を新しい初期値にします。通常更新では並進8m/s、回転720度/sを超える1フレームの跳びを制限します。</remarks>
        public Pose Filter(Pose sample, float time, ModConfig config)
        {
            var dt = time - _lastTime;
            if (!_initialized || dt <= 0f || dt > 0.25f)
            {
                _initialized = true;
                _lastTime = time;
                _lastPosition = _filteredPosition = sample.Position;
                _lastRotation = _filteredRotation = sample.Rotation;
                _filteredVelocity = Vector3.zero;
                _filteredAngularSpeed = 0f;
                return sample;
            }

            // Reject one-frame tracking jumps before their huge derivative can
            // make the adaptive cutoff pass the bad sample through.
            var position = Vector3.MoveTowards(_lastPosition, sample.Position, 8f * dt);
            var rotation = Quaternion.RotateTowards(_lastRotation, sample.Rotation, 720f * dt);
            var derivativeAlpha = Alpha(1f, dt);
            var velocity = (position - _lastPosition) / dt;
            _filteredVelocity = Vector3.Lerp(_filteredVelocity, velocity, derivativeAlpha);
            var positionCutoff = Mathf.Clamp(config.PositionMinCutoffHz, 0.1f, 30f) +
                Mathf.Clamp(config.PositionBeta, 0f, 20f) * _filteredVelocity.magnitude;
            _filteredPosition = Vector3.Lerp(_filteredPosition, position,
                Alpha(positionCutoff, dt));

            var angularSpeed = Quaternion.Angle(_lastRotation, rotation) *
                Mathf.Deg2Rad / dt;
            _filteredAngularSpeed = Mathf.Lerp(_filteredAngularSpeed, angularSpeed, derivativeAlpha);
            var rotationCutoff = Mathf.Clamp(config.RotationMinCutoffHz, 0.1f, 30f) +
                Mathf.Clamp(config.RotationBeta, 0f, 20f) * _filteredAngularSpeed;
            _filteredRotation = Quaternion.Slerp(_filteredRotation, rotation,
                Alpha(rotationCutoff, dt));

            _lastTime = time;
            _lastPosition = position;
            _lastRotation = rotation;
            return new Pose(_filteredPosition, _filteredRotation);
        }

        /// <summary>
        /// カットオフ周波数とサンプル間隔から一次ローパスの混合率を計算します。
        /// </summary>
        /// <param name="cutoff">カットオフ周波数。単位はHzです。</param>
        /// <param name="dt">前回サンプルからの経過秒数。</param>
        /// <returns>新しい値へ適用する混合率。</returns>
        private static float Alpha(float cutoff, float dt)
        {
            var tau = 1f / (2f * Mathf.PI * cutoff);
            return 1f / (1f + tau / dt);
        }
    }
}
