using SynthRidersFullBody;
using UnityEngine;
using Vector3 = System.Numerics.Vector3;

/// <summary>
/// ゲームを起動せず、本体の足先補正を回転・階層を持つテスト用Transformで検証します。
/// </summary>
internal static class ToeRotationTests
{
    /// <summary>
    /// 大小のモデル、独自ボーン軸、ターン、追加足先入力、追跡喪失を検証します。
    /// </summary>
    public static void Run(Action<bool, string> check)
    {
        foreach (var size in new[] { 0.01f, 0.3f, 1f, 3f, 100f })
        foreach (var intermediate in new[] { false, true })
        {
            var root = new GameObject("root").transform;
            root.localScale = Vector3.One * size;
            root.localPosition = new Vector3(3, -1, 2);
            var foot = new GameObject("foot").transform;
            foot.parent = root;
            foot.localPosition = new Vector3(-0.1f, 0.1f, 0);
            foot.localRotation = Turn(Vector3.UnitY, 25) * Turn(Vector3.UnitZ, 12);
            var parent = foot;
            if (intermediate)
            {
                parent = new GameObject("intermediate").transform;
                parent.parent = foot;
                parent.localRotation = Turn(Vector3.UnitY, -30);
            }
            var toes = new GameObject("toes").transform;
            toes.parent = parent;
            toes.localPosition = new Vector3(0, -0.04f, 0.18f);
            toes.localRotation = Turn(Vector3.UnitX, 73); // VRM bone axes need not match.
            var reference = Quaternion.Inverse(foot.rotation) * toes.rotation;
            var correction = new CalibratedToeRotation(foot, toes);
            var beforeCapture = toes.rotation;
            correction.Apply(true, false, 1);
            check(Same(toes.rotation, beforeCapture), "Uncalibrated toes were modified.");

            // Fit a small/large model to the same user, then capture the fixed pose.
            var head = root.position + new Vector3(0, 1.7f, 0) * size;
            check(AvatarScaleMath.TryFit(root.position, root.localScale, head, foot.position, foot.position,
                new Vector3(0, 1.75f, 0), new Vector3(0, 0.15f, 0), new Vector3(0, 0.15f, 0),
                out var fittedPosition, out var fittedScale, out _), "Size fit failed in toe regression.");
            root.localPosition = fittedPosition;
            root.localScale = fittedScale;
            correction.Capture();
            correction.Apply(true, false, 1);
            check(Same(Quaternion.Inverse(foot.rotation) * toes.rotation, reference),
                "Size fitting changed a correct toe angle.");
            foreach (var heading in new[] { 0f, 90f, 180f, 270f, 360f })
            foreach (var lift in new[] { 0f, -60f, 35f })
            {
                root.localRotation = Turn(Vector3.UnitY, heading);
                foot.localRotation = Turn(Vector3.UnitX, lift) * Turn(Vector3.UnitZ, 12);
                if (intermediate) parent.localRotation = Turn(Vector3.UnitY, heading / 3);
                var intended = foot.rotation * reference;
                // Reproduce a toe-only bend after animation/IK, with the sole unchanged.
                toes.rotation = intended * Turn(Vector3.UnitX, 80);
                var footRotation = foot.rotation;
                var footPosition = foot.position;
                var toePosition = toes.position;
                var toeLocalPosition = toes.localPosition;
                var toeLocalScale = toes.localScale;
                correction.Apply(true, false, 1);
                check(Same(toes.rotation, intended), "Single-input toe bend survived correction.");
                check(Math.Abs(correction.CorrectionDegrees - 80) < 0.02f, "Toe correction diagnostic missed the bend.");
                check(Same(foot.rotation, footRotation) && Vector3.Distance(foot.position, footPosition) < 0.00001f,
                    "Toe correction moved or tilted the sole.");
                check(Vector3.Distance(toes.position, toePosition) < 0.00001f &&
                    toes.localPosition == toeLocalPosition && toes.localScale == toeLocalScale,
                    "Toe correction stretched or translated the foot.");
                correction.Apply(true, false, 1);
                check(Same(toes.rotation, intended), "Repeated correction accumulated a toe rotation.");
            }

            var neutral = foot.rotation * reference;
            var measured = neutral * Turn(Vector3.UnitX, -45);
            toes.rotation = measured;
            correction.Apply(true, true, 1);
            check(Same(toes.rotation, measured), "Independent ankle/toe tracking was locked to neutral.");
            correction.Apply(false, false, 1);
            check(Same(toes.rotation, measured), "A lost foot input still changed its toes.");
            foreach (var weight in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            {
                correction.Apply(true, false, weight);
                check(Same(toes.rotation, measured), "Inactive/invalid solver weight changed toes.");
            }
            correction.Apply(true, false, 0.25f);
            check(Math.Abs(Quaternion.Angle(toes.rotation, neutral) - 33.75f) < 0.02f,
                "Tracking recovery did not blend the toe correction.");

            // Missing toes, a mistaken self-reference and an unrelated hierarchy must be harmless.
            foreach (var missing in new Transform?[] { null, foot, root })
            {
                var invalid = new CalibratedToeRotation(foot, missing);
                var oldFoot = foot.rotation;
                var oldRoot = root.rotation;
                invalid.Capture();
                invalid.Apply(true, false, 1);
                check(Same(oldFoot, foot.rotation) && Same(oldRoot, root.rotation),
                    "Missing/unrelated toe reference changed the rig.");
            }
        }
    }

    /// <summary>軸と度数からテスト用の回転を作ります。</summary>
    private static Quaternion Turn(Vector3 axis, float degrees) =>
        new(System.Numerics.Quaternion.CreateFromAxisAngle(axis, degrees * MathF.PI / 180));

    /// <summary>Quaternionの符号に依存せず、回転の一致を検証します。</summary>
    private static bool Same(Quaternion a, Quaternion b) =>
        Math.Abs(System.Numerics.Quaternion.Dot(a.Value, b.Value)) > 0.99999f;
}

namespace UnityEngine
{
    // Math/hierarchy doubles only; these do not emulate the native VRIK solver.
    public readonly struct Quaternion
    {
        internal readonly System.Numerics.Quaternion Value;
        internal Quaternion(System.Numerics.Quaternion value) => Value = value;
        public static Quaternion identity => new(System.Numerics.Quaternion.Identity);
        public static Quaternion Inverse(Quaternion q) => new(System.Numerics.Quaternion.Inverse(q.Value));
        public static Quaternion operator *(Quaternion a, Quaternion b) => new(a.Value * b.Value);
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) =>
            new(System.Numerics.Quaternion.Slerp(a.Value, b.Value, t));
        public static float Angle(Quaternion a, Quaternion b) => 2 * MathF.Acos(Math.Clamp(Math.Abs(
            System.Numerics.Quaternion.Dot(System.Numerics.Quaternion.Normalize(a.Value),
                System.Numerics.Quaternion.Normalize(b.Value))), 0, 1)) * 180 / MathF.PI;
    }
    public static class Mathf
    {
        public static float Clamp01(float value) => Math.Clamp(value, 0, 1);
        public static float Abs(float value) => Math.Abs(value);
        public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
        public static float MoveTowards(float current, float target, float delta) =>
            current + Math.Clamp(target - current, -delta, delta);
    }
}
