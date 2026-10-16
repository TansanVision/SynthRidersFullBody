namespace SynthRidersFullBody;

/// <summary>設定パネルの表示と入力で共有する操作部品の種別です。</summary>
internal enum PanelControlKind { Hidden, Information, Primary, Navigation, Toggle, Choice }

/// <summary>表示の種類・操作可否・選択状態を一か所で管理します。</summary>
internal readonly record struct PanelControlState(PanelControlKind Kind, bool Enabled = true, bool Selected = false)
{
    /// <summary>状態表示や非表示の領域は入力対象にしません。</summary>
    public bool Interactive => Enabled && Kind is not (PanelControlKind.Hidden or PanelControlKind.Information);
}

/// <summary>描画と当たり判定が共有する、Canvasローカル座標の矩形です。</summary>
internal readonly record struct PanelRect(float X, float Y, float Width, float Height)
{
    /// <summary>矩形内の有限な座標だけを受け付けます。</summary>
    public bool Contains(float x, float y) => float.IsFinite(x) && float.IsFinite(y) &&
        MathF.Abs(x - X) <= Width / 2 && MathF.Abs(y - Y) <= Height / 2;
}

/// <summary>全画面で共通のナビゲーションと操作行の配置です。</summary>
internal static class PanelControls
{
    public const int RowCount = 6;
    public const int Back = 6, Close = 7, Trackers = 8, Display = 9, Expressions = 10, Details = 11;
    public const int DashboardNext = 12, TPose = 13, Standing = 14;
    public const int PagePrevious = 15, PageNext = 16, DashboardPrevious = 17;
    public const int DashboardDetails = 18, TextStandard = 19, TextLarge = 20, Nearer = 21, Farther = 22, Count = 23;

    /// <summary>表示側と入力側が同じ大きさ・位置を使用します。</summary>
    public static PanelRect Bounds(int index) => index switch
    {
        >= 0 and < RowCount => new(-340, 155 - index * 75, 820, 68),
        Back => new(-660, 350, 200, 60),
        Close => new(660, 350, 200, 60),
        >= Trackers and <= Details => new(-649 + (index - Trackers) * 206, 270, 192, 60),
        DashboardPrevious => new(282, -325, 304, 62),
        DashboardNext => new(608, -325, 304, 62),
        TPose => new(-550, 80, 400, 68),
        Standing => new(-130, 80, 400, 68),
        PagePrevious => new(-550, -145, 400, 68),
        PageNext => new(-130, -145, 400, 68),
        DashboardDetails => new(610, 270, 300, 60),
        TextStandard => new(-550, 155, 400, 68),
        TextLarge => new(-130, 155, 400, 68),
        Nearer => new(-550, 80, 400, 68),
        Farther => new(-130, 80, 400, 68),
        _ => default
    };

    /// <summary>描画時と同じ有効状態で判定します。非表示・無効・情報行は反応しません。</summary>
    public static int Hit(IReadOnlyList<PanelControlState> controls, float x, float y)
    {
        for (var i = 0; i < controls.Count; i++)
            if (controls[i].Interactive && Bounds(i).Contains(x, y)) return i;
        return -1;
    }

    /// <summary>押した手に対応する対象だけを選びます。同時押しでは右を優先して一度だけ実行します。</summary>
    public static int PressedTarget(PanelPress press, int left, int right, int gaze) =>
        Activation(press, left, right, gaze).Target;

    /// <summary>確定対象と同じ入力元を返し、振動を別の手へ送らないようにします。</summary>
    public static PanelActivation Activation(PanelPress press, int left, int right, int gaze) =>
        press.Right && right >= 0 ? new(right, UiHand.Right) :
        press.Left && left >= 0 ? new(left, UiHand.Left) :
        press.Gaze && gaze >= 0 ? new(gaze, UiHand.None) : new(-1, UiHand.None);

    /// <summary>1ページなら送り操作を隠し、複数ページの両端だけを無効にします。</summary>
    public static void Paging(PanelControlState[] controls, int previous, int next, int page, int pages)
    {
        controls[previous] = pages > 1 ? new(PanelControlKind.Navigation, page > 0) : default;
        controls[next] = pages > 1 ? new(PanelControlKind.Navigation, page + 1 < pages) : default;
    }

    /// <summary>UI調整画面の選択・距離の端・トグルを表示と入力へ同時に適用します。</summary>
    public static void Appearance(PanelControlState[] controls, float distance, bool large, bool haptics, bool sound)
    {
        controls[0] = controls[1] = default;
        controls[TextStandard] = new(PanelControlKind.Choice, large, !large);
        controls[TextLarge] = new(PanelControlKind.Choice, !large, large);
        controls[Nearer] = new(PanelControlKind.Navigation, distance > VrPanelLayout.MinDistance + .01f);
        controls[Farther] = new(PanelControlKind.Navigation, distance < VrPanelLayout.MaxDistance - .01f);
        controls[2] = controls[5] = new(PanelControlKind.Navigation);
        controls[3] = new(PanelControlKind.Toggle, true, haptics);
        controls[4] = new(PanelControlKind.Toggle, true, sound);
    }
}

/// <summary>操作した手です。視線操作はNoneで、振動を送信しません。</summary>
internal enum UiHand { None, Left, Right }
/// <summary>入力判定とフィードバックが共有する1回の操作です。</summary>
internal readonly record struct PanelActivation(int Target, UiHand Hand);

/// <summary>各入力元で新しく押されたトリガーを示します。</summary>
internal readonly record struct PanelPress(bool Left, bool Right, bool Gaze)
{
    /// <summary>このフレームに確定操作があるか。</summary>
    public bool Any => Left || Right || Gaze;
}

/// <summary>開閉・遷移をまたぐ押下を消費し、全トリガーの解放後に再入力を許可します。</summary>
internal sealed class PanelInputGate
{
    private bool _left = true, _right = true, _trigger = true;
    /// <summary>開く・閉じる・操作した直後は、指を離すまで入力を抑止します。</summary>
    public bool Armed { get; private set; }

    /// <summary>現在の操作を次の画面や背後の入口に持ち越さないようにします。</summary>
    public void Block() => Armed = false;

    /// <summary>姿勢のある手の押下か、両手の姿勢がない場合の視線押下を返します。</summary>
    public PanelPress Update(bool trigger, bool left, bool right, bool leftPose, bool rightPose)
    {
        var press = new PanelPress(Armed && leftPose && left && !_left,
            Armed && rightPose && right && !_right,
            Armed && !leftPose && !rightPose && trigger && !_trigger);
        _trigger = trigger;
        _left = left;
        _right = right;
        if (!trigger && !left && !right) Armed = true;
        return press;
    }
}

/// <summary>上位画面の種類です。個体・表情の選択画面はその配下で管理します。</summary>
internal enum PanelPage { Details, Calibration, Trackers, Display, Expressions }

/// <summary>タブ間では履歴を増やさず、戻る先の設定またはキャリブレーションを保持します。</summary>
internal sealed class PanelNavigation
{
    private PanelPage _parent = PanelPage.Details;
    /// <summary>現在の上位画面です。</summary>
    public PanelPage Current { get; private set; }
    /// <summary>詳細設定以外では上位画面へ戻れます。</summary>
    public bool CanGoBack => Current != PanelPage.Details;

    /// <summary>入口から開き、過去の画面移動を破棄します。</summary>
    public void Open(bool calibration)
    {
        Current = calibration ? PanelPage.Calibration : PanelPage.Details;
        _parent = PanelPage.Details;
    }

    /// <summary>タブを選びます。詳細タブは設定のトップへ戻ります。</summary>
    public void SelectTab(PanelPage page)
    {
        if (page == PanelPage.Details) { Open(false); return; }
        if (Current is PanelPage.Calibration or PanelPage.Details) _parent = Current;
        Current = page;
    }

    /// <summary>一段上へ戻ります。詳細設定のトップでは何もしません。</summary>
    public void Back()
    {
        Current = Current == PanelPage.Calibration ? PanelPage.Details : _parent;
        _parent = PanelPage.Details;
    }
}
