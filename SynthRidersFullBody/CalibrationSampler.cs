using UnityEngine;

namespace SynthRidersFullBody;

// Calibrate from a short stationary window, rather than one potentially noisy frame.
/// <summary>
/// 短い静止区間の入力を蓄積し、動きと欠測を検証した平均姿勢を校正へ供給します。
/// </summary>
internal sealed class CalibrationSampler
{
    private readonly List<Sample> _samples = new();
    /// <summary>
    /// 1回の計測時刻と頭・腰・左右足・追加部位のワールド姿勢です。
    /// </summary>
    /// <param name="Time">サンプルの取得時刻。単位は秒です。</param>
    /// <param name="Head">HMDのワールド位置。</param>
    /// <param name="Waist">腰のワールド姿勢。</param>
    /// <param name="Left">左足のワールド姿勢。</param>
    /// <param name="Right">右足のワールド姿勢。</param>
    /// <param name="Additional">部位ごとの校正時のワールド姿勢。</param>
    private readonly record struct Sample(float Time, Vector3 Head, Pose Waist, Pose Left, Pose Right,
        Dictionary<TrackerRole, Pose> Additional);

    /// <summary>
    /// 蓄積した校正サンプルをすべて破棄します。
    /// </summary>
    public void Reset() => _samples.Clear();

    /// <summary>
    /// 追加部位を値として複製してサンプルを追加し、0.6秒より古いサンプルを除去します。
    /// </summary>
    /// <param name="time">サンプルの取得時刻。単位は秒です。</param>
    /// <param name="head">現在時刻で取得したHMDのワールド位置。</param>
    /// <param name="waist">腰のワールド姿勢。</param>
    /// <param name="left">左足のワールド姿勢。</param>
    /// <param name="right">右足のワールド姿勢。</param>
    /// <param name="additional">部位ごとの追跡姿勢。校正・駆動処理ではゲームのワールド空間を使用します。</param>
    public void Add(float time, Vector3 head, Pose waist, Pose left, Pose right,
        IReadOnlyDictionary<TrackerRole, Pose> additional)
    {
        _samples.Add(new Sample(time, head, waist, left, right, additional.ToDictionary(x => x.Key, x => x.Value)));
        while (_samples.Count > 0 && time - _samples[0].Time > 0.6f)
            _samples.RemoveAt(0);
    }

    /// <summary>
    /// サンプル数・鮮度・移動量・回転量・部位の連続性を検証して平均姿勢を返します。
    /// </summary>
    /// <param name="now">現在時刻。蓄積時刻と同じ基準の秒数を使用します。</param>
    /// <param name="waist">成功時に、腰の静止区間の平均ワールド姿勢を受け取ります。失敗時は使用しません。</param>
    /// <param name="left">成功時に、左足の静止区間の平均ワールド姿勢を受け取ります。失敗時は使用しません。</param>
    /// <param name="right">成功時に、右足の静止区間の平均ワールド姿勢を受け取ります。失敗時は使用しません。</param>
    /// <param name="reason">失敗時の案内。成功時は空文字列です。</param>
    /// <param name="additional">成功時に、追加部位を含む平均ワールド姿勢の一覧を受け取ります。失敗時は使用しません。</param>
    /// <returns>すべての静止・連続性条件を満たす場合はtrue。</returns>
    /// <remarks>falseの場合は平均値を途中まで計算している可能性があるため、出力姿勢は使用しません。理由を表示し、新しい静止区間を蓄積してください。</remarks>
    public bool TryGet(float now, out Pose waist, out Pose left, out Pose right, out string reason,
        out Dictionary<TrackerRole, Pose> additional)
    {
        waist = left = right = default;
        additional = new();
        if (_samples.Count < 8 || _samples[^1].Time - _samples[0].Time < 0.25f ||
            now - _samples[^1].Time > 0.15f || now < _samples[^1].Time)
        {
            reason = "hold the pose steady for half a second";
            return false;
        }

        var head = Vector3.zero;
        var waistPosition = Vector3.zero;
        var leftPosition = Vector3.zero;
        var rightPosition = Vector3.zero;
        foreach (var sample in _samples)
        {
            head += sample.Head;
            waistPosition += sample.Waist.Position;
            leftPosition += sample.Left.Position;
            rightPosition += sample.Right.Position;
        }
        var inverseCount = 1f / _samples.Count;
        head *= inverseCount;
        waistPosition *= inverseCount;
        leftPosition *= inverseCount;
        rightPosition *= inverseCount;
        waist = new Pose(waistPosition, AverageRotation(s => s.Waist.Rotation));
        left = new Pose(leftPosition, AverageRotation(s => s.Left.Rotation));
        right = new Pose(rightPosition, AverageRotation(s => s.Right.Rotation));

        if (_samples.Any(s => s.Additional.Count != _samples[^1].Additional.Count))
        { reason = "Assigned inputs changed during calibration; retry"; return false; }
        foreach (var role in _samples[^1].Additional.Keys)
        {
            if (_samples.Any(s => !s.Additional.ContainsKey(role)))
            { reason = $"{TrackerRoles.Label(role)}: tracking changed during calibration"; return false; }
            var position = Vector3.zero;
            foreach (var sample in _samples) position += sample.Additional[role].Position;
            var pose = new Pose(position * inverseCount, AverageRotation(s => s.Additional[role].Rotation));
            if (_samples.Any(s => Vector3.Distance(s.Additional[role].Position, pose.Position) > 0.06f ||
                Quaternion.Angle(s.Additional[role].Rotation, pose.Rotation) > 18f))
            { reason = $"{TrackerRoles.Label(role)} moved; hold still and retry"; return false; }
            additional[role] = pose;
        }

        foreach (var sample in _samples)
        {
            if (Vector3.Distance(sample.Head, head) > 0.07f ||
                Vector3.Distance(sample.Waist.Position, waistPosition) > 0.06f ||
                Vector3.Distance(sample.Left.Position, leftPosition) > 0.05f ||
                Vector3.Distance(sample.Right.Position, rightPosition) > 0.05f ||
                Quaternion.Angle(sample.Waist.Rotation, waist.Rotation) > 18f ||
                Quaternion.Angle(sample.Left.Rotation, left.Rotation) > 18f ||
                Quaternion.Angle(sample.Right.Rotation, right.Rotation) > 18f)
            {
                reason = "trackers moved during calibration; hold still and retry";
                return false;
            }
        }
        reason = "";
        return true;
    }

    /// <summary>
    /// Quaternionの符号を最初の値に揃えて平均し、正規化した回転を求めます。
    /// </summary>
    /// <param name="select">各サンプルから平均対象の回転を取得する関数。</param>
    /// <returns>平均回転。平均のノルムが小さすぎる場合は最初の回転。</returns>
    private Quaternion AverageRotation(Func<Sample, Quaternion> select)
    {
        var reference = select(_samples[0]);
        var x = 0f;
        var y = 0f;
        var z = 0f;
        var w = 0f;
        foreach (var sample in _samples)
        {
            var rotation = select(sample);
            var sign = Quaternion.Dot(reference, rotation) < 0f ? -1f : 1f;
            x += sign * rotation.x;
            y += sign * rotation.y;
            z += sign * rotation.z;
            w += sign * rotation.w;
        }
        var magnitude = Mathf.Sqrt(x * x + y * y + z * z + w * w);
        return magnitude < 0.001f ? reference :
            new Quaternion(x / magnitude, y / magnitude, z / magnitude, w / magnitude);
    }
}
