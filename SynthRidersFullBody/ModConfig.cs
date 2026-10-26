using System.Text.Json;

namespace SynthRidersFullBody;

/// <summary>
/// 全アバター共通の追跡・補正・描画設定と、アバター別のHMD表示希望値を保存します。
/// </summary>
public sealed class ModConfig
{
    /// <summary>
    /// 過去の補正設定を移行するためのバージョン番号です。
    /// </summary>
    public int CorrectionVersion { get; set; } = 4;
    /// <summary>
    /// 旧形式との互換性のために保持する腰トラッカーのシリアル番号です。
    /// </summary>
    public string WaistSerial { get; set; } = "";
    /// <summary>
    /// 旧形式との互換性のために保持する左足首トラッカーのシリアル番号です。
    /// </summary>
    public string LeftFootSerial { get; set; } = "";
    /// <summary>
    /// 旧形式との互換性のために保持する右足首トラッカーのシリアル番号です。
    /// </summary>
    public string RightFootSerial { get; set; } = "";
    /// <summary>
    /// 部位名をキーとするデバイス割り当てです。未設定の部位は辞書に含めません。
    /// </summary>
    public Dictionary<string, TrackerBinding> RoleBindings { get; set; } = new();
    /// <summary>
    /// 校正済みの全身追跡を有効にするかを指定します。
    /// </summary>
    public bool Enabled { get; set; } = true;
    /// <summary>
    /// SteamVRのUltimate Trackerの位置・回転を水平に180度補正し、対象の腰の水平位置を校正時のHMDへ合わせます。
    /// 前後と左右の移動がともに逆の環境向けです。変更後は再校正が必要です。
    /// </summary>
    public bool UltimateTrackerRotate180 { get; set; }
    /// <summary>
    /// HMD内に配信用カメラのガイドを表示するかを指定します。
    /// </summary>
    public bool ShowCaptureCameraGuide { get; set; }
    /// <summary>プレイ外でアバター表示中の配信カメラを固定バストショットへ切り替えます。全アバター共通、初期値はOFFです。</summary>
    public bool MenuBustShot { get; set; }
    private float _leftHandRollDegrees, _rightHandRollDegrees;
    /// <summary>自動校正後の左掌面の微調整角。手を取得できた新規キャリブレーション時は0に戻します。</summary>
    public float LeftHandRollDegrees
    {
        get => _leftHandRollDegrees;
        set => _leftHandRollDegrees = float.IsFinite(value) ? Math.Clamp(value, -180f, 180f) : 0f;
    }
    /// <summary>右掌面の微調整角。位置・ゲーム操作には影響せず、新規校正時は0に戻します。</summary>
    public float RightHandRollDegrees
    {
        get => _rightHandRollDegrees;
        set => _rightHandRollDegrees = float.IsFinite(value) ? Math.Clamp(value, -180f, 180f) : 0f;
    }
    /// <summary>
    /// 対象アバターの影の投射・受け取りを無効にするかを指定します。
    /// </summary>
    public bool DisableAvatarShadows { get; set; }
    /// <summary>
    /// 対象アバター配下のライトを無効にするかを指定します。
    /// </summary>
    public bool DisableAvatarLights { get; set; }
    /// <summary>文字と操作ボタンを15%拡大します。全アバター共通です。</summary>
    public bool UiLargeText { get; set; }
    private float _uiPanelDistance = 1.4f;
    /// <summary>開くときのパネル距離（m）。1.1〜1.8mの範囲で保存します。</summary>
    public float UiPanelDistance
    {
        get => _uiPanelDistance;
        set => _uiPanelDistance = float.IsFinite(value) ? Math.Clamp(value, 1.1f, 1.8f) : 1.4f;
    }
    /// <summary>対応する操作元コントローラーへ短い振動を返します。</summary>
    public bool UiHaptics { get; set; } = true;
    /// <summary>短い操作音を有効にします。初期状態では無効です。</summary>
    public bool UiSound { get; set; }
    /// <summary>LIVE TRACKERSへ取得元ごとの生座標・速度を表示します。</summary>
    public bool UiTrackerDetails { get; set; }
    /// <summary>
    /// アバター識別キーごとの一人称表示希望値です。
    /// </summary>
    public Dictionary<string, bool> AvatarVisibleInHeadset { get; set; } = new();
    /// <summary>コントローラーによる表情操作を有効にします。初期状態では無効です。</summary>
    public bool ExpressionsEnabled { get; set; }
    /// <summary>全アバター共通のボタンと表情の割り当て。存在しない表情は適用しません。</summary>
    public Dictionary<string, ExpressionBinding> ExpressionBindings { get; set; } = new();
    /// <summary>
    /// 追跡入力の適応型平滑化を有効にするかを指定します。
    /// </summary>
    public bool SmoothingEnabled { get; set; } = true;
    /// <summary>
    /// 位置のOne Euro Filterに使用する最小カットオフ周波数です。単位はHzです。
    /// </summary>
    public float PositionMinCutoffHz { get; set; } = 2.5f;
    /// <summary>
    /// 移動速度に応じて位置フィルターのカットオフを上げる係数です。
    /// </summary>
    public float PositionBeta { get; set; } = 3f;
    /// <summary>
    /// 回転の適応型フィルターに使用する最小カットオフ周波数です。単位はHzです。
    /// </summary>
    public float RotationMinCutoffHz { get; set; } = 2f;
    /// <summary>
    /// 角速度に応じて回転フィルターのカットオフを上げる係数です。
    /// </summary>
    public float RotationBeta { get; set; } = 1f;
    /// <summary>
    /// 水平旋回を除いた骨盤の傾きに適用する上限角度です。単位は度です。
    /// </summary>
    public float PelvisMaxTiltDegrees { get; set; } = 20f;
    /// <summary>
    /// 腰トラッカーの傾きを骨盤へ反映する割合です。
    /// </summary>
    public float PelvisTiltWeight { get; set; } = 0.5f;
    /// <summary>
    /// VRIKの骨盤位置目標に使用する重みです。
    /// </summary>
    public float PelvisPositionWeight { get; set; } = 1f;
    /// <summary>
    /// VRIKの骨盤回転目標に使用する重みです。
    /// </summary>
    public float PelvisRotationWeight { get; set; } = 1f;
    /// <summary>
    /// VRIKのルートコントローラーによるルート制御を使用するかを指定します。
    /// </summary>
    public bool UseVrikRootController { get; set; } = true;
    /// <summary>
    /// 腰から推定した胸目標へ向ける重みです。
    /// </summary>
    public float ChestGoalWeight { get; set; } = 0.15f;
    // Retained for configuration compatibility. Waist mode no longer blends
    // head/hand directions into the virtual chest.
    /// <summary>
    /// 旧設定との互換性用の値です。現在の腰基準の胸制御では頭方向の混合に使用しません。
    /// </summary>
    public float ChestHeadDirectionWeight { get; set; }
    /// <summary>
    /// 旧設定との互換性用の値です。現在の腰基準の胸制御では手方向の混合に使用しません。
    /// </summary>
    public float ChestHandDirectionWeight { get; set; }
    /// <summary>
    /// 2ボーン直接駆動で許可する膝の最大屈曲角です。単位は度です。
    /// </summary>
    public float KneeMaxFlexionDegrees { get; set; } = 145f;
    /// <summary>
    /// 計測した膝入力がないときのVRIK膝ガイドの重みです。
    /// </summary>
    public float KneeBendGoalWeight { get; set; } = 0.3f;

    /// <summary>
    /// JSON設定を読み込み、旧形式の割り当てと補正の既定値を移行します。未作成なら既定設定を保存します。
    /// </summary>
    /// <param name="path">設定JSONの保存先パス。</param>
    /// <returns>読み込み・移行済み、または新規作成した設定。</returns>
    public static ModConfig Load(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<ModConfig>(json) ?? new ModConfig();
            using var document = JsonDocument.Parse(json);
            config.ExpressionBindings ??= new();
            foreach (var key in config.ExpressionBindings.Keys.ToArray())
            {
                var binding = config.ExpressionBindings[key];
                if (binding == null || !Enum.TryParse<ExpressionButton>(key, out var button) ||
                    !Enum.IsDefined(typeof(ExpressionButton), button) || button.ToString() != key)
                    config.ExpressionBindings.Remove(key);
                else config.ExpressionBindings[key] = binding with { Expression = binding.Expression ?? "" };
            }
            config.RoleBindings ??= new();
            foreach (var key in config.RoleBindings.Keys.ToArray())
            {
                var binding = config.RoleBindings[key];
                if (binding == null || !Enum.TryParse<TrackerRole>(key, out var role) || !Enum.IsDefined(typeof(TrackerRole), role))
                    config.RoleBindings.Remove(key);
                else config.RoleBindings[key] = binding with { Source = binding.Source ?? "", Serial = binding.Serial ?? "" };
            }
            if (!document.RootElement.TryGetProperty(nameof(RoleBindings), out _))
            {
                foreach (var entry in new[] { (TrackerRole.Waist, config.WaistSerial),
                             (TrackerRole.LeftFoot, config.LeftFootSerial), (TrackerRole.RightFoot, config.RightFootSerial) })
                    if (!string.IsNullOrWhiteSpace(entry.Item2))
                        config.RoleBindings[entry.Item1.ToString()] = new TrackerBinding { Serial = entry.Item2 };
            }
            var correctionVersion = document.RootElement.TryGetProperty(
                nameof(CorrectionVersion), out var version) &&
                version.ValueKind == JsonValueKind.Number &&
                version.TryGetInt32(out var savedVersion) ? savedVersion : 0;
            if (correctionVersion < 2)
            {
                // Keep deliberate tuning; update only values shipped as defaults.
                if (config.PelvisMaxTiltDegrees == 30f) config.PelvisMaxTiltDegrees = 20f;
                if (config.PelvisRotationWeight == 0.7f) config.PelvisRotationWeight = 0.35f;
                if (config.ChestGoalWeight == 0.35f) config.ChestGoalWeight = 0.15f;
                if (config.ChestHeadDirectionWeight == 0.20f)
                    config.ChestHeadDirectionWeight = 0.35f;
                config.CorrectionVersion = 2;
            }
            if (correctionVersion < 3)
            {
                if (config.PelvisPositionWeight == 0.8f)
                    config.PelvisPositionWeight = 1f;
                config.CorrectionVersion = 3;
            }
            if (correctionVersion < 4)
            {
                if (config.PelvisRotationWeight == 0.35f)
                    config.PelvisRotationWeight = 1f;
                config.CorrectionVersion = 4;
            }
            return config;
        }

        var newConfig = new ModConfig();
        newConfig.Save(path);
        return newConfig;
    }

    /// <summary>
    /// 設定を一時JSONファイルへ書き出し、書き込み完了後に保存先を置き換えます。
    /// </summary>
    /// <param name="path">設定JSONの保存先パス。</param>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath,
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, path, true);
    }
}
