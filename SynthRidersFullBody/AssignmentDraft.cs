namespace SynthRidersFullBody;

/// <summary>接続世代を含む、選択時点の個体情報です。再接続された個体は別の候補として扱います。</summary>
internal sealed record AssignmentDevice(string Key, string Name, TrackerBinding Binding, long Session = 0);

/// <summary>API列挙順に依存しない一覧と、切断・一時ID再利用を識別する接続世代を管理します。</summary>
internal sealed class AssignmentDevices
{
    private Dictionary<string, AssignmentDevice> _active = new(StringComparer.Ordinal);
    private long _generation;
    /// <summary>取得元と個体識別子の順で並べた一意な接続個体です。</summary>
    public IReadOnlyList<AssignmentDevice> Items { get; private set; } = Array.Empty<AssignmentDevice>();

    /// <summary>同じキーの重複は選択不能にし、消失後の再出現には新しい世代を発行します。</summary>
    public void Update(IEnumerable<AssignmentDevice> devices)
    {
        var next = new Dictionary<string, AssignmentDevice>(StringComparer.Ordinal);
        foreach (var group in devices.GroupBy(d => d.Key, StringComparer.Ordinal))
        {
            if (group.Count() != 1) continue;
            var device = group.First();
            var session = _active.TryGetValue(device.Key, out var previous) &&
                previous.Binding.DeviceId == device.Binding.DeviceId && previous.Name == device.Name
                ? previous.Session : ++_generation;
            next[device.Key] = device with { Session = session, Binding = device.Binding with { SessionId = session } };
        }
        _active = next;
        Items = next.Values.OrderBy(d => d.Key, StringComparer.Ordinal).ToArray();
    }

    /// <summary>現在の一意な接続個体を返します。重複・切断したキーは返しません。</summary>
    public AssignmentDevice? Find(string key) => _active.GetValueOrDefault(key);

    /// <summary>選択した個体が同じ接続世代のまま存在するかを調べます。</summary>
    public bool Contains(AssignmentDevice device) => Find(device.Key)?.Session == device.Session;
}

/// <summary>確認後に設定を変更したか、変更不要だったか、検証に失敗したかを表します。</summary>
internal enum AssignmentApplyResult { Invalid, Unchanged, Applied }

/// <summary>手動・自動の割り当て変更を保存設定から分離し、全件検証後に一括適用します。</summary>
internal sealed class AssignmentDraft
{
    private readonly Dictionary<string, TrackerBinding> _original;
    private readonly Dictionary<string, TrackerBinding> _proposed;
    private readonly Dictionary<string, AssignmentDevice> _candidates;
    /// <summary>確認画面へ表示する変更案です。</summary>
    public IReadOnlyDictionary<string, TrackerBinding> Proposed => _proposed;
    /// <summary>割り当ての実質的な変更があるか。</summary>
    public bool HasChanges => !Equal(_original, _proposed);
    /// <summary>変更がある身体部位です。移動による解除元も含みます。</summary>
    public IReadOnlyList<TrackerRole> ChangedRoles => TrackerRoles.All.Where(r =>
        !Same(_original.GetValueOrDefault(r.ToString()), _proposed.GetValueOrDefault(r.ToString()))).ToArray();

    /// <summary>現在の割り当てを複製し、まだ実設定を書き換えない編集を開始します。</summary>
    public AssignmentDraft(IReadOnlyDictionary<string, TrackerBinding> current)
    {
        _original = new(current);
        _proposed = new(current);
        _candidates = new();
    }

    /// <summary>未確定の自動提案をさらに編集するため、元設定と接続情報も含めて複製します。</summary>
    public AssignmentDraft Copy()
    {
        var copy = new AssignmentDraft(_original);
        copy._proposed.Clear();
        foreach (var entry in _proposed) copy._proposed.Add(entry.Key, entry.Value);
        foreach (var entry in _candidates) copy._candidates.Add(entry.Key, entry.Value);
        return copy;
    }

    /// <summary>個体を指定部位へ移す変更案を作ります。nullは解除案です。</summary>
    public void Set(TrackerRole role, AssignmentDevice? device)
    {
        TrackerRoles.Assign(_proposed, role, device?.Binding);
        foreach (var key in _candidates.Keys.ToArray())
            if (!_proposed.TryGetValue(key, out var binding) || !Same(binding, _candidates[key].Binding))
                _candidates.Remove(key);
        if (device != null) _candidates[role.ToString()] = device;
    }

    /// <summary>元設定が変わっておらず、選択した個体が同じ接続世代で追跡中かを検証します。</summary>
    public string? Validate(IReadOnlyDictionary<string, TrackerBinding> current, Func<AssignmentDevice, bool> available)
    {
        if (!Equal(_original, current)) return "Assignments changed. Cancel and select again.";
        if (_candidates.Values.Any(d => !available(d))) return "Device disconnected or not tracked. Cancel and select again.";
        return null;
    }

    /// <summary>すべての候補を検証してから一括適用します。失敗・変更なしでは設定を変更しません。</summary>
    public AssignmentApplyResult Apply(Dictionary<string, TrackerBinding> current, Func<AssignmentDevice, bool> available)
    {
        if (Validate(current, available) != null) return AssignmentApplyResult.Invalid;
        if (!HasChanges) return AssignmentApplyResult.Unchanged;
        current.Clear();
        foreach (var entry in _proposed) current.Add(entry.Key, entry.Value);
        return AssignmentApplyResult.Applied;
    }

    /// <summary>シリアルがある場合は一時番号の変更を無視し、ない場合は接続世代を含めて比較します。</summary>
    private static bool Same(TrackerBinding? a, TrackerBinding? b) => a == null || b == null ? a == b :
        a.Source == b.Source && (a.Serial.Length > 0 && b.Serial.Length > 0
            ? string.Equals(a.Serial, b.Serial, StringComparison.OrdinalIgnoreCase)
            : a.Serial == b.Serial && a.DeviceId == b.DeviceId && a.SessionId == b.SessionId);

    /// <summary>保存先とスナップショットの割り当て内容を比較します。</summary>
    private static bool Equal(IReadOnlyDictionary<string, TrackerBinding> a, IReadOnlyDictionary<string, TrackerBinding> b) =>
        a.Count == b.Count && a.All(e => b.TryGetValue(e.Key, out var value) && Same(e.Value, value));
}
