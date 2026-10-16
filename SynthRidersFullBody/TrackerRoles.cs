using System.Text.Json.Serialization;

namespace SynthRidersFullBody;

/// <summary>
/// トラッカーまたはコントローラーを割り当てられる身体部位です。
/// </summary>
public enum TrackerRole
{
    /// <summary>
    /// 腰・骨盤を駆動する入力。
    /// </summary>
    Waist, 
    /// <summary>
    /// 左足首の位置・回転を駆動する入力。
    /// </summary>
    LeftFoot, 
    /// <summary>
    /// 右足首の位置・回転を駆動する入力。
    /// </summary>
    RightFoot, 
    /// <summary>
    /// ゲーム標準の頭入力を置き換える追加入力。
    /// </summary>
    Head, 
    /// <summary>
    /// 左肘の曲げ位置を指定する入力。
    /// </summary>
    LeftElbow, 
    /// <summary>
    /// 右肘の曲げ位置を指定する入力。
    /// </summary>
    RightElbow,
    /// <summary>
    /// 左手首を駆動する入力。
    /// </summary>
    LeftWrist, 
    /// <summary>
    /// 右手首を駆動する入力。
    /// </summary>
    RightWrist, 
    /// <summary>
    /// 左膝の曲げ位置を指定する入力。
    /// </summary>
    LeftKnee, 
    /// <summary>
    /// 右膝の曲げ位置を指定する入力。
    /// </summary>
    RightKnee, 
    /// <summary>
    /// 左足先の追加入力。左足首が未設定なら左足の主入力として使用します。
    /// </summary>
    LeftToes, 
    /// <summary>
    /// 右足先の追加入力。右足首が未設定なら右足の主入力として使用します。
    /// </summary>
    RightToes
}

/// <summary>
/// 取得元・シリアル・一時デバイス番号によって部位と実機を対応付ける設定です。
/// </summary>
public sealed record TrackerBinding
{
    /// <summary>
    /// 取得元APIの名前です。空文字列は旧設定の取得元未指定を表します。
    /// </summary>
    public string Source { get; init; } = "";
    /// <summary>
    /// 再接続・再起動後の復元に用いるシリアル番号です。
    /// </summary>
    public string Serial { get; init; } = "";
    // Runtime indices are not stable across launches. Never persist one.
    /// <summary>
    /// 現在の起動中だけ有効なデバイス番号です。JSONには保存しません。
    /// </summary>
    [JsonIgnore] public string DeviceId { get; init; } = "";
    /// <summary>シリアルなし個体の再接続を識別する実行中だけの接続世代です。</summary>
    [JsonIgnore] public long SessionId { get; init; }

    /// <summary>
    /// シリアルを優先し、なければ取得元と一時番号を用いてデバイスの一致を判定します。
    /// </summary>
    /// <param name="source">取得元APIの識別名。</param>
    /// <param name="serial">取得したデバイスのシリアル番号。</param>
    /// <param name="id">取得元APIで使用するデバイス番号の文字列。</param>
    /// <param name="sessionId">シリアルなし個体の現在の接続世代。</param>
    /// <returns>指定デバイスがこの割り当てに一致する場合はtrue。</returns>
    public bool Matches(string source, string serial, string id, long sessionId = 0) =>
        (string.IsNullOrEmpty(Source) || Source == source) &&
        (!string.IsNullOrWhiteSpace(Serial)
            ? string.Equals(Serial, serial, StringComparison.OrdinalIgnoreCase)
            : DeviceId.Length > 0 && DeviceId == id && (SessionId == 0 || SessionId == sessionId));

    /// <summary>
    /// 2つの割り当てが同じ物理個体を指すかを調べます。
    /// </summary>
    /// <param name="other">同じ個体を参照するか比較する割り当て。</param>
    /// <returns>シリアルが一致、またはシリアル比較ができず取得元と有効な番号が一致すればtrue。</returns>
    /// <remarks>両者にシリアルがある場合は取得元が異なっても同じ個体とみなします。シリアルがない場合の番号は今回の起動中だけ有効です。</remarks>
    public bool SameDevice(TrackerBinding other) =>
        Serial.Length > 0 && other.Serial.Length > 0
            ? string.Equals(Serial, other.Serial, StringComparison.OrdinalIgnoreCase)
            : Source == other.Source && DeviceId.Length > 0 && DeviceId == other.DeviceId;
}

/// <summary>
/// 部位表示と重複割り当ての検出・移動を管理します。
/// </summary>
internal static class TrackerRoles
{
    /// <summary>
    /// 指定部位のデバイスが別部位にも割り当てられているかを確認します。
    /// </summary>
    /// <param name="bindings">部位名をキーとする現在の割り当て一覧。</param>
    /// <param name="role">対象の身体部位。</param>
    /// <returns>同じ個体を参照する別部位があればtrue。</returns>
    public static bool IsConflicted(IReadOnlyDictionary<string, TrackerBinding> bindings, TrackerRole role)
    {
        var key = role.ToString();
        return bindings.TryGetValue(key, out var binding) &&
            bindings.Any(x => x.Key != key && x.Value.SameDevice(binding));
    }

    /// <summary>
    /// UIでの表示と全身姿勢の走査に使用する部位の順序です。
    /// </summary>
    public static readonly TrackerRole[] All =
    {
        TrackerRole.Waist, TrackerRole.LeftFoot, TrackerRole.RightFoot,
        TrackerRole.LeftToes, TrackerRole.RightToes, TrackerRole.Head,
        TrackerRole.LeftWrist, TrackerRole.RightWrist, TrackerRole.LeftElbow,
        TrackerRole.RightElbow, TrackerRole.LeftKnee, TrackerRole.RightKnee
    };

    /// <summary>
    /// 部位をUIに表示する英語名へ変換します。
    /// </summary>
    /// <param name="role">対象の身体部位。</param>
    /// <returns>足首・足先などを区別した表示名。</returns>
    public static string Label(TrackerRole role) => role switch
    {
        TrackerRole.LeftFoot => "Left ankle", TrackerRole.RightFoot => "Right ankle",
        TrackerRole.LeftToes => "Left toes", TrackerRole.RightToes => "Right toes",
        TrackerRole.LeftWrist => "Left wrist", TrackerRole.RightWrist => "Right wrist",
        TrackerRole.LeftElbow => "Left elbow", TrackerRole.RightElbow => "Right elbow",
        TrackerRole.LeftKnee => "Left knee", TrackerRole.RightKnee => "Right knee",
        _ => role.ToString()
    };

    /// <summary>
    /// 同じ個体の既存割り当てを解除して指定部位へ移し、nullの場合は指定部位を解除します。
    /// </summary>
    /// <param name="bindings">部位名をキーとする現在の割り当て一覧。</param>
    /// <param name="role">対象の身体部位。</param>
    /// <param name="binding">新しい個体の割り当て。nullは指定部位の解除です。</param>
    public static void Assign(Dictionary<string, TrackerBinding> bindings, TrackerRole role, TrackerBinding? binding)
    {
        if (binding == null) { bindings.Remove(role.ToString()); return; }
        // Move one physical device to the chosen role; never invent a swap.
        foreach (var key in bindings.Where(x => x.Value.SameDevice(binding)).Select(x => x.Key).ToArray())
            bindings.Remove(key);
        bindings[role.ToString()] = binding;
    }
}
