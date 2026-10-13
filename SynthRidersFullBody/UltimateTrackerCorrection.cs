using System.Numerics;

namespace SynthRidersFullBody;

/// <summary>
/// Ultimate Trackerの追跡空間を水平に180度回転し、固定した基準位置へ合わせる補正です。
/// </summary>
/// <param name="Pivot">取得元APIの座標系で固定した回転中心。</param>
/// <param name="Rotation">取得元APIの座標系で表した、ゲームの鉛直軸周りの半回転。</param>
/// <param name="Destination">回転中心を移す先の位置。同じ取得元APIの座標系で表します。</param>
/// <remarks>位置と回転へ同じ剛体変換を適用します。身体部位や機器のローカル装着軸は交換しません。</remarks>
internal readonly record struct UltimateTrackerCorrection(Vector3 Pivot, Quaternion Rotation, Vector3 Destination)
{
    /// <summary>
    /// ログで確認したSteamVRのモデル名から、補正対象のUltimate Trackerを識別します。
    /// </summary>
    /// <param name="source">姿勢を取得するAPI名。</param>
    /// <param name="kind">Tracker、Controller、HMDなどの機器種別。</param>
    /// <param name="name">APIが報告するモデル名。</param>
    /// <returns>SteamVRのVIVE Ultimate Trackerならtrue。</returns>
    public static bool IsTarget(string source, string kind, string name) =>
        source == "SteamVR" && kind == "Tracker" &&
        (name.Equals("VIVE Ultimate Tracker", StringComparison.OrdinalIgnoreCase) ||
         name.StartsWith("VIVE Ultimate Tracker ", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 校正で使用する固定した回転中心と鉛直軸、位置合わせ先から剛体変換を作成します。
    /// </summary>
    /// <param name="pivot">生の追跡空間での固定中心。腰が対象なら腰の位置を使います。</param>
    /// <param name="up">ゲームの鉛直方向を生の追跡空間へ逆変換した方向。</param>
    /// <param name="destination">同じ生空間での位置合わせ先。省略すると中心を移動しません。</param>
    /// <returns>HMDのその後の動きに依存しない補正値。</returns>
    public static UltimateTrackerCorrection Create(Vector3 pivot, Vector3 up, Vector3? destination = null) =>
        new(pivot, Quaternion.CreateFromAxisAngle(Vector3.Normalize(up), MathF.PI), destination ?? pivot);

    /// <summary>
    /// 直立校正時の腰中心をHMDの真下へ水平移動する位置を求め、追跡空間の共通の横ずれを取り除きます。
    /// </summary>
    /// <param name="waist">生空間での腰トラッカー位置。</param>
    /// <param name="head">ゲームのHMD位置を同じ生空間へ逆変換した位置。</param>
    /// <param name="up">同じ生空間で表したゲームの鉛直方向。</param>
    /// <returns>腰の高さを保ち、水平位置を頭と合わせた固定中心。</returns>
    /// <remarks>直立時に頭と腰が上下に並ぶことを近似します。追跡中に呼び直すと頭の動きが全身へ混ざるため、基準取得時のみ使用します。</remarks>
    public static Vector3 AlignWaistBelowHead(Vector3 waist, Vector3 head, Vector3 up)
    {
        var axis = Vector3.Normalize(up);
        var difference = head - waist;
        return waist + difference - axis * Vector3.Dot(difference, axis);
    }

    /// <summary>
    /// 生位置と生回転へ同じ半回転を適用し、位置だけ反転して姿勢が食い違うことを防ぎます。
    /// </summary>
    /// <param name="position">取得元の生位置。</param>
    /// <param name="rotation">取得元の生回転。</param>
    /// <returns>同じ追跡空間で表した補正後の位置と回転。</returns>
    public (Vector3 Position, Quaternion Rotation) Apply(Vector3 position, Quaternion rotation) =>
        (Destination + Vector3.Transform(position - Pivot, Rotation), Quaternion.Normalize(Rotation * rotation));
}
