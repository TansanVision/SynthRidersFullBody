using System.Numerics;
using SynthRidersFullBody;

internal static class HeadPoseFreshnessTests
{
    /// <summary>静止と停止を区別し、姿勢が再開した場合に警告を解除することを確認します。</summary>
    public static void Run(Action<bool, string> check)
    {
        var monitor = new HeadPoseFreshness();
        var p = new Vector3(0, 1.6f, 0);
        var q = Quaternion.Identity;
        check(!monitor.Update(p, q, p, q, 0), "Initial sample warned.");
        check(!monitor.Update(p, q, p, q, 5), "Stationary user was treated as stale.");
        check(monitor.Update(p, q, p + Vector3.UnitX * .1f, q, 6), "Frozen head with live translation went undetected.");
        check(!monitor.Update(p + Vector3.UnitX * .1f, q, p + Vector3.UnitX * .1f, q, 7), "Resumed tracking retained stale warning.");
        monitor.Reset();
        check(!monitor.Update(p, q, p, q, 10), "Reset retained history.");
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitX, .6f);
        check(!monitor.Update(p, q, p, turn, 10.5f), "Brief runtime latency was treated as a frozen pose.");
        check(monitor.Update(p, q, p, turn, 11.1f), "Frozen head with live rotation went undetected.");
        check(!monitor.Update(p, turn, p, turn, 12), "Rotation recovery retained stale warning.");
    }
}
