using SynthRidersFullBody;
using UnityEngine;
using Il2CppVRM;
using Il2CppUniVRM10;

internal static class AvatarExpressionTests
{
    /// <summary>実装のVRMアダプターにAPIダブルを接続し、元の表情復元・合成・モデル交換を検証します。</summary>
    public static void Run(Action<bool, string> check)
    {
        var config = new ModConfig { ExpressionsEnabled = true };
        config.ExpressionBindings["LeftPrimary"] = new() { Expression = "preset:happy" };
        var avatar = new GameObject("VRM0");
        var proxy = new VRMBlendShapeProxy(avatar);
        var joy = new BlendShapeClip("Joy", BlendShapePreset.Joy, 20);
        var blink = new BlendShapeClip("Blink", BlendShapePreset.Blink, 40);
        proxy.BlendShapeAvatar.Clips.AddRange(new[] { joy, blink });
        // An unfinished VRM1 component must not prevent the VRM0 fallback.
        _ = new Vrm10Instance(avatar);
        using var adapter = new AvatarExpressions();
        adapter.Refresh(avatar);
        check(adapter.Choices.Count == 2, "VRM0 expressions were not discovered.");
        proxy.AccumulateValue(joy.Key, 0.2f);
        proxy.AccumulateValue(blink.Key, 0.25f);
        proxy.Apply();
        void Update(int pressed, bool allowed = true) => adapter.Update(new(pressed, 63, "controller"), config, allowed);
        Update(0);
        for (var i = 0; i < 4; i++)
        {
            // A game writer has queued a contribution but has not called Apply.
            proxy.AccumulateValue(blink.Key, 0.25f);
            Update(1);
        }
        check(Math.Abs(proxy.GetValue(joy.Key) - 1) < 0.001f, "Hold did not apply the selected VRM0 expression.");
        check(Math.Abs(proxy.GetValue(blink.Key) - 0.25f) < 0.001f, "Selected expression erased a blink weight.");
        check(Math.Abs(proxy.Rendered - 30) < 0.001f, "Shared morph contributions were erased or counted twice.");
        Update(0);
        check(Math.Abs(proxy.GetValue(joy.Key) - 0.2f) < 0.001f && Math.Abs(proxy.Rendered - 14) < 0.001f,
            "Release failed to restore the original expression and preserve the blink.");

        Update(1); Update(1); Update(1);
        proxy.AccumulateValue(joy.Key, 0.6f); proxy.Apply();
        Update(1); Update(0);
        check(Math.Abs(proxy.GetValue(joy.Key) - 0.6f) < 0.001f, "Release overwrote a game expression changed during hold.");
        Update(1); Update(1); Update(1);
        proxy.AccumulateValue(joy.Key, 0.4f); proxy.Apply();
        Update(0);
        check(Math.Abs(proxy.GetValue(joy.Key) - 0.4f) < 0.001f, "Release overwrote a newer game value.");

        Update(1); Update(1); Update(1);
        Update(1, false);
        check(Math.Abs(proxy.GetValue(joy.Key) - 0.4f) < 0.001f, "Calibration/menu block left an expression active.");
        Update(1);
        check(Math.Abs(proxy.GetValue(joy.Key) - 0.4f) < 0.001f, "Still-held button fired after calibration.");
        Update(0); Update(1); Update(1); Update(1);

        var nextAvatar = new GameObject("VRM1");
        var runtime = new ExpressionRuntime();
        var happy = new ExpressionKey(ExpressionPreset.happy, "happy");
        var sad = new ExpressionKey(ExpressionPreset.sad, "sad");
        runtime.ExpressionKeys.Items.AddRange(new[] { happy, sad });
        runtime.SetWeight(happy, 0.1f); runtime.SetWeight(sad, 0.3f);
        _ = new Vrm10Instance(nextAvatar) { Runtime = new() { Expression = runtime } };
        adapter.Refresh(nextAvatar);
        check(Math.Abs(proxy.GetValue(joy.Key) - 0.4f) < 0.001f, "Avatar switch did not restore old VRM0 weights.");
        Update(1);
        check(runtime.GetWeight(happy) == 0.1f, "Avatar switch inherited a held input.");
        Update(0); Update(1); Update(1); Update(1);
        check(runtime.GetWeight(happy) > 0.99f && runtime.GetWeight(sad) == 0.3f,
            "VRM1 preset mapping failed or changed an unrelated expression.");
        adapter.Dispose();
        check(Math.Abs(runtime.GetWeight(happy) - 0.1f) < 0.001f && adapter.Choices.Count == 0,
            "Scene teardown retained expression weights or stale choices.");
    }
}

// These API doubles model weight storage and an additive shared morph. They do
// not verify native IL2CPP dispatch, Unity update order or actual facial rendering.
namespace UnityEngine
{
    public static class Time
    {
        public static float unscaledTime;
        public static float unscaledDeltaTime = 0.05f;
    }
}
namespace Il2CppVRM
{
    public enum BlendShapePreset { Unknown, Joy, Blink }
    public readonly record struct BlendShapeKey(string Name);
    public sealed class BlendShapeClip
    {
        public string BlendShapeName;
        public BlendShapePreset Preset;
        public float[] Values;
        public float[] MaterialValues = Array.Empty<float>();
        public BlendShapeKey Key => new(BlendShapeName);
        public BlendShapeClip(string name, BlendShapePreset preset, float weight)
        { BlendShapeName = name; Preset = preset; Values = new[] { weight }; }
    }
    public sealed class BlendShapeAvatar { public List<BlendShapeClip> Clips = new(); }
    public sealed class VRMBlendShapeProxy : Component
    {
        public BlendShapeAvatar BlendShapeAvatar = new();
        private readonly Dictionary<BlendShapeKey, float> _values = new();
        private float _pending;
        private bool _hasPending;
        public float Rendered;
        public VRMBlendShapeProxy(GameObject avatar) : base(avatar) { }
        public float GetValue(BlendShapeKey key) => _values.GetValueOrDefault(key);
        public void AccumulateValue(BlendShapeKey key, float value)
        {
            _values[key] = value;
            _pending += BlendShapeAvatar.Clips.First(c => c.Key == key).Values[0] * value;
            _hasPending = true;
        }
        public void Apply()
        {
            if (_hasPending) Rendered = _pending;
            _pending = 0; _hasPending = false;
        }
    }
}
namespace Il2CppSystem.Collections.Generic
{
    public interface IReadOnlyCollection<T> { int Count { get; } }
}
namespace Il2CppUniVRM10
{
    public enum ExpressionPreset { custom, happy, sad }
    public readonly record struct ExpressionKey(ExpressionPreset Preset, string Name)
    {
        public override string ToString() => Name;
    }
    public sealed class KeyList : Il2CppSystem.Collections.Generic.IReadOnlyCollection<ExpressionKey>
    {
        public List<ExpressionKey> Items = new();
        public int Count => Items.Count;
        public ExpressionKey this[int index] => Items[index];
        public T Cast<T>() => (T)(object)this;
    }
    public sealed class ExpressionRuntime
    {
        public KeyList ExpressionKeys = new();
        private readonly Dictionary<ExpressionKey, float> _values = new();
        public float GetWeight(ExpressionKey key) => _values.GetValueOrDefault(key);
        public void SetWeight(ExpressionKey key, float value) => _values[key] = value;
    }
    public sealed class VrmRuntime { public ExpressionRuntime? Expression; }
    public sealed class Vrm10Instance : Component
    {
        public VrmRuntime? Runtime;
        public Vrm10Instance(GameObject avatar) : base(avatar) { }
    }
}
