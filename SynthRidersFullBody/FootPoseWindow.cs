using System.Globalization;

namespace SynthRidersFullBody;

/// <summary>ログの間に一瞬だけ正常になる足も確認できるよう、計算直後の値の範囲を集計します。</summary>
internal sealed class FootPoseWindow
{
    private int _count;
    private float _firstTime, _lastTime;
    private float _maxPosition;
    private float _minAngle = float.PositiveInfinity, _maxAngle = float.NegativeInfinity;
    private float _minReach = float.PositiveInfinity, _maxReach = float.NegativeInfinity;
    private float _minTargetSlope = float.PositiveInfinity, _maxTargetSlope = float.NegativeInfinity;
    private float _minSolvedSlope = float.PositiveInfinity, _maxSolvedSlope = float.NegativeInfinity;

    /// <summary>有効な追跡・IK重みで得られた足だけを集計し、欠測や復帰中の値を混ぜません。</summary>
    /// <param name="sample">VRIK直後に保存した足の実測値。</param>
    /// <param name="time">サンプルの計測時刻。Unityの非スケール時間です。</param>
    /// <param name="usable">この足の追跡が有効で、IKと脚の位置・回転重みが十分か。</param>
    public void Add(FootPoseSample sample, float time, bool usable)
    {
        if (!usable || !float.IsFinite(time) || sample.AnkleError is not { } position ||
            sample.AnkleAngle is not { } angle || sample.Reach is not { } reach ||
            !float.IsFinite(position) || !float.IsFinite(angle) || !float.IsFinite(reach)) return;
        if (_count != 0 && time < _lastTime) Reset();
        if (_count++ == 0) _firstTime = time;
        _lastTime = time;
        _maxPosition = MathF.Max(_maxPosition, position);
        Include(angle, ref _minAngle, ref _maxAngle);
        Include(reach, ref _minReach, ref _maxReach);
        Include(sample.TargetSlope, ref _minTargetSlope, ref _maxTargetSlope);
        Include(sample.SolvedSlope, ref _minSolvedSlope, ref _maxSolvedSlope);
    }

    /// <summary>区間のサンプル数・時間と範囲を表します。値は最終描画の測定ではありません。</summary>
    public string Summary => _count == 0 ? "samples=0" :
        $"samples={_count}; span={(_lastTime - _firstTime).ToString("0.00", CultureInfo.InvariantCulture)}s; " +
        $"max ankle error={_maxPosition.ToString("0.000", CultureInfo.InvariantCulture)}m; " +
        $"ankle angle range={Range(_minAngle, _maxAngle)}deg; reach range={Range(_minReach, _maxReach)}; " +
        $"foot slope target range={Range(_minTargetSlope, _maxTargetSlope)}deg; solved range={Range(_minSolvedSlope, _maxSolvedSlope)}deg";

    /// <summary>ログ出力後・校正変更・追跡喪失時に、以前の状態の集計を捨てます。</summary>
    public void Reset()
    {
        _count = 0;
        _firstTime = _lastTime = _maxPosition = 0f;
        _minAngle = _minReach = _minTargetSlope = _minSolvedSlope = float.PositiveInfinity;
        _maxAngle = _maxReach = _maxTargetSlope = _maxSolvedSlope = float.NegativeInfinity;
    }

    /// <summary>有限な値だけを範囲へ加えます。足先ボーンがない場合の仰角は欠測のままにします。</summary>
    private static void Include(float? value, ref float minimum, ref float maximum)
    {
        if (value is not { } number || !float.IsFinite(number)) return;
        minimum = MathF.Min(minimum, number);
        maximum = MathF.Max(maximum, number);
    }

    /// <summary>未計測の範囲をn/aとし、ゲーム言語に依存しない小数書式を使います。</summary>
    private static string Range(float minimum, float maximum) => !float.IsFinite(minimum) ? "n/a" :
        minimum.ToString("0.###", CultureInfo.InvariantCulture) + ".." + maximum.ToString("0.###", CultureInfo.InvariantCulture);
}
