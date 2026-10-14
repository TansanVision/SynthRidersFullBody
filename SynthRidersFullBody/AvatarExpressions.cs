using Il2CppVRM;
using Il2CppUniVRM10;
using UnityEngine;

namespace SynthRidersFullBody;

/// <summary>ロード中のVRMの表情を列挙し、MODが選択した表情の重みだけを一時変更します。</summary>
internal sealed class AvatarExpressions : IDisposable
{
    private GameObject? _avatar;
    private VRMBlendShapeProxy? _proxy;
    private readonly Dictionary<string, Slot> _slots = new(StringComparer.Ordinal);
    private readonly List<ExpressionChoice> _choices = new();
    private readonly List<(BlendShapeKey Key, string Id)> _vrm0Keys = new();
    private readonly Dictionary<string, float> _pendingValues = new();
    private readonly ExpressionControls _controls = new();
    private float _nextScan;
    private string _active = "";
    private float _previous;
    private float _lastWritten;
    /// <summary>現在のアバターが持つ表情。解除・未割り当てはUI側で追加します。</summary>
    public IReadOnlyList<ExpressionChoice> Choices => _choices;
    /// <summary>表情APIの検出結果または現在の適用状態です。</summary>
    public string Status { get; private set; } = "Load an avatar to see expressions";

    /// <summary>アバター変更を検出し、適用可能なVRM表情を低頻度で探索します。</summary>
    public void Refresh(GameObject? avatar)
    {
        if (_avatar != avatar) { Dispose(); _avatar = avatar; _nextScan = 0; }
        if (_avatar == null || !_avatar.activeInHierarchy)
        {
            Reset();
            Status = "Load an avatar to see expressions";
            return;
        }
        if (_slots.Count > 0 || Time.unscaledTime < _nextScan) return;
        _nextScan = Time.unscaledTime + 2f;
        var vrm10 = _avatar.GetComponentInChildren<Vrm10Instance>(true);
        _proxy = null;
        _vrm0Keys.Clear();
        if (vrm10 != null && vrm10.Runtime?.Expression is { } expression)
        {
            var keys = expression.ExpressionKeys;
            var count = keys.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<ExpressionKey>>().Count;
            for (var i = 0; i < count; i++)
            {
                var key = keys[i];
                var id = key.Preset.ToString().Equals("custom", StringComparison.OrdinalIgnoreCase)
                    ? "custom:" + key.Name : ExpressionControls.PresetId(key.Preset.ToString());
                Add(id, key.ToString(), () => expression.GetWeight(key), v => expression.SetWeight(key, v));
            }
        }
        else
        {
            _proxy = _avatar.GetComponentInChildren<VRMBlendShapeProxy>(true);
            if (_proxy?.BlendShapeAvatar?.Clips is { } clips)
            {
                foreach (var clip in clips)
                {
                    if (clip == null || ((clip.Values?.Length ?? 0) == 0 && (clip.MaterialValues?.Length ?? 0) == 0)) continue;
                    var key = clip.Key;
                    var id = clip.Preset == BlendShapePreset.Unknown ? "custom:" + clip.BlendShapeName
                        : ExpressionControls.PresetId(clip.Preset.ToString());
                    if (Add(id, clip.BlendShapeName, () => _proxy.GetValue(key), v => _pendingValues[id] = v))
                        _vrm0Keys.Add((key, id));
                }
            }
        }
        _choices.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
        Status = _slots.Count == 0 ? "No VRM expressions found" : $"{_slots.Count} expressions available";
    }

    /// <summary>有効な表情キーを保存し、モデル由来の表示名を長さと装飾を制限して表示します。</summary>
    private bool Add(string id, string label, Func<float> read, Action<float> write)
    {
        if (!_slots.TryAdd(id, new Slot(read, write))) return false;
        var safe = (string.IsNullOrWhiteSpace(label) ? id : label).Replace('<', '(').Replace('>', ')')
            .Replace('\n', ' ').Replace('\r', ' ');
        if (safe.Length > 36) safe = safe[..33] + "...";
        _choices.Add(new ExpressionChoice(id, safe));
        return true;
    }

    /// <summary>現在の入力を処理し、表情を適用します。まばたき等の他のキーは変更しません。</summary>
    public void Update(ExpressionInput input, ModConfig config, bool allowed)
    {
        config.ExpressionBindings ??= new Dictionary<string, ExpressionBinding>();
        var selected = _controls.Update(input, config.ExpressionBindings,
            allowed && config.ExpressionsEnabled && _avatar != null && _avatar.activeInHierarchy && _slots.Count > 0,
            id => !string.IsNullOrEmpty(id) && _slots.ContainsKey(id));
        if (selected != _active)
        {
            Release();
            if (_slots.TryGetValue(selected, out var next))
            {
                _previous = next.Read();
                _lastWritten = _previous;
                _active = selected;
            }
        }
        if (_slots.TryGetValue(_active, out var slot))
        {
            var live = slot.Read();
            if (Mathf.Abs(live - _lastWritten) > 0.001f) _previous = live;
            _lastWritten = Mathf.MoveTowards(_lastWritten, 1f, Mathf.Clamp(Time.unscaledDeltaTime, 0, 0.1f) / 0.15f);
            slot.Write(_lastWritten);
            Apply();
            Status = "Active: " + _choices.First(c => c.Id == _active).Label;
        }
        else if (_slots.Count > 0) Status = $"{_slots.Count} expressions | { (config.ExpressionsEnabled ? "Ready" : "OFF") }";
    }

    /// <summary>VRM 0.xの蓄積値をまとめて適用します。VRM 1.0は通常のRuntime更新へ反映を委ねます。</summary>
    private void Apply()
    {
        if (_proxy != null)
        {
            // Flush contributions queued by the game before submitting a full
            // snapshot; otherwise AccumulateValue would add them twice.
            _proxy.Apply();
            // Apply clears the merger's accumulated contributions. Re-submit
            // all current expression values so an unmodified blink/mouth key
            // sharing a morph target is not erased by our selected expression.
            foreach (var entry in _vrm0Keys)
            {
                var value = _pendingValues.TryGetValue(entry.Id, out var pending) ? pending : _proxy.GetValue(entry.Key);
                _proxy.AccumulateValue(entry.Key, value);
            }
            _proxy.Apply();
            _pendingValues.Clear();
        }
    }

    /// <summary>MODの最終書き込みが残っている場合だけ、操作前の表情の重みへ戻します。</summary>
    private void Release()
    {
        var old = _active;
        _active = "";
        if (_avatar == null || !_slots.TryGetValue(old, out var slot)) return;
        if (Mathf.Abs(slot.Read() - _lastWritten) < 0.001f)
        {
            slot.Write(_previous);
            Apply();
        }
    }

    /// <summary>押下中・保持中の表情を解除し、次の操作前にボタンを離すことを要求します。</summary>
    public void Reset()
    {
        _controls.Reset();
        Release();
    }

    /// <summary>元の表情へ戻して、破棄されるアバターのAPI参照を解放します。</summary>
    public void Dispose()
    {
        try { Reset(); }
        finally
        {
            _slots.Clear(); _choices.Clear(); _vrm0Keys.Clear(); _pendingValues.Clear();
            _avatar = null; _proxy = null;
            Status = "Load an avatar to see expressions";
        }
    }

    /// <summary>1表情の現在値取得と、VRM合成器への書き込みを保持します。</summary>
    private sealed record Slot(Func<float> Read, Action<float> Write);
}
