using UnityEngine;

namespace SynthRidersFullBody;

/// <summary>取得元APIと個体を識別した、校正または現在フレームの手入力です。</summary>
internal readonly record struct ControllerHandPose(string Source, Pose Pose);

/// <summary>左右を独立に保持します。一方の欠測で他方の補正を失いません。</summary>
internal readonly record struct HandCalibrationInput(ControllerHandPose? Left, ControllerHandPose? Right)
{
    /// <summary>シーン復帰時に元の校正入力を新しい原点へ移し、現在の腕の姿勢を再学習しません。</summary>
    public HandCalibrationInput Remap(Func<Pose, Pose> remap) => new(
        Left is { } left ? left with { Pose = remap(left.Pose) } : null,
        Right is { } right ? right with { Pose = remap(right.Pose) } : null);
}

/// <summary>標準の掌面と、コントローラーの校正回転の対応を保持します。</summary>
internal sealed class CalibratedHandRotation
{
    private string? _source;
    private Quaternion _offset;
    /// <summary>有効なコントローラーから掌面への回転対応が保存されているかを示します。</summary>
    public bool Calibrated => _source != null;

    /// <summary>固定プレビューで回転だけを記録します。ゲームが扱う手の位置は変更しません。</summary>
    public void Capture(ControllerHandPose? source, Pose? hand)
    {
        _source = null;
        if (source is not { } input || !input.Pose.IsFinite || hand is not { IsFinite: true } bone ||
            string.IsNullOrEmpty(input.Source)) return;
        _offset = Quaternion.Inverse(input.Pose.Rotation) * bone.Rotation;
        _source = input.Source;
    }

    /// <summary>同じAPI・個体の回転だけへ補正します。欠測・別個体・取得元変更時はゲーム標準へ戻します。</summary>
    public Quaternion? Apply(ControllerHandPose? input) => _source != null && input is { } value &&
        value.Source == _source && value.Pose.IsFinite ? value.Pose.Rotation * _offset : null;
}
