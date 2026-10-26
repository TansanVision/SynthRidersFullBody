using Il2CppRootMotion.FinalIK;
using UnityEngine;

namespace SynthRidersFullBody;

/// <summary>VRIK計算中だけ手のひら方向の目標を補正し、ゲームのコントローラーTransformは変更しません。</summary>
internal sealed class HandTargetAdjustment : IDisposable
{
    private readonly Slot _left = new("Left"), _right = new("Right");

    /// <summary>標準の手ボーンとコントローラーの回転を校正します。明示手首トラッカー側はnullで除外します。</summary>
    public void Capture(HandCalibrationInput input, Pose? leftHand, Pose? rightHand)
    {
        _left.Calibration.Capture(input.Left, leftHand);
        _right.Calibration.Capture(input.Right, rightHand);
        UpdateInput(input);
    }

    /// <summary>現在の左右入力を渡します。欠測した側だけゲーム標準の手目標へ戻します。</summary>
    public void UpdateInput(HandCalibrationInput input) { _left.Input = input.Left; _right.Input = input.Right; }

    /// <summary>左右それぞれの自動校正の有無をログへ表示します。</summary>
    public string CalibrationStatus => $"left={_left.Calibration.Calibrated}, right={_right.Calibration.Calibrated}";

    /// <summary>指先方向を軸として左右の手を個別に回します。位置と腕の重みは維持します。</summary>
    public void Apply(IKSolverVR solver, float leftDegrees, float rightDegrees)
    {
        Restore();
        _left.Apply(solver.leftArm, leftDegrees);
        _right.Apply(solver.rightArm, rightDegrees);
    }

    /// <summary>計算後、または計算が中断された次フレームに元の手目標を戻します。</summary>
    public void Restore() { try { _left.Restore(); } finally { _right.Restore(); } }

    /// <summary>元の接続を戻し、補正用Transformを破棄します。</summary>
    public void Dispose() { try { _left.Dispose(); } finally { _right.Dispose(); } }

    /// <summary>片手の一時的な目標差し替えを管理します。</summary>
    private sealed class Slot : IDisposable
    {
        private readonly string _name;
        private Transform? _target, _previous;
        private IKSolverVR.Arm? _arm;
        public readonly CalibratedHandRotation Calibration = new();
        public ControllerHandPose? Input;

        /// <summary>診断用の左右名を保存します。目標は補正が有効になってから作成します。</summary>
        public Slot(string name) => _name = name;

        /// <summary>ボーン固有の手首から指先への軸を使い、校正済み目標のローカル回転へ加算します。</summary>
        public void Apply(IKSolverVR.Arm arm, float degrees)
        {
            var calibratedRotation = Calibration.Apply(Input);
            if (arm.target == null || (calibratedRotation == null && Mathf.Abs(degrees) < .01f)) return;
            if (_target == null)
            {
                var go = new GameObject($"SynthRidersFullBody.{_name}HandAdjustment");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _target = go.transform;
            }
            _previous = arm.target;
            _arm = arm;
            var roll = arm.wristToPalmAxis.sqrMagnitude < .0001f ? Quaternion.identity :
                Quaternion.AngleAxis(degrees, arm.wristToPalmAxis.normalized);
            _target.SetPositionAndRotation(_previous.position, (calibratedRotation ?? _previous.rotation) * roll);
            arm.target = _target;
        }

        /// <summary>自分の目標がまだ接続されている場合だけ復元し、ゲーム側の再接続は上書きしません。</summary>
        public void Restore()
        {
            try { if (_arm != null && _arm.target == _target) _arm.target = _previous; }
            finally { _arm = null; _previous = null; }
        }

        /// <summary>一時目標の所有権を解放します。</summary>
        public void Dispose()
        {
            Restore();
            if (_target != null) UnityEngine.Object.Destroy(_target.gameObject);
            _target = null;
        }
    }
}
