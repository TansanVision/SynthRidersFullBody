namespace SynthRidersFullBody;

/// <summary>キャリブレーション画面の順序です。姿勢の判定条件自体は変更しません。</summary>
internal enum CalibrationStage { Preparation, Pose, Countdown, Review }

/// <summary>設定済みの部位について、未割り当てと追跡喪失を区別します。</summary>
internal enum CalibrationDeviceState { Unassigned, Lost, Conflict, Ready }

/// <summary>同じフレームの割り当てとワールド姿勢から求めた開始準備です。</summary>
internal readonly record struct CalibrationReadiness(bool AvatarReady, CalibrationDeviceState Waist,
    CalibrationDeviceState Left, CalibrationDeviceState Right, bool WorldPosesReady, string? AdditionalIssue)
{
    /// <summary>姿勢はカウントダウン中に取れるため、腕の高さ・広がりは開始の必須条件にしません。</summary>
    public bool Ready => AvatarReady && WorldPosesReady && Waist == CalibrationDeviceState.Ready &&
        Left == CalibrationDeviceState.Ready && Right == CalibrationDeviceState.Ready && AdditionalIssue == null;

    /// <summary>保存済みの設定を変更せず、重複・未設定・未追跡を分類します。</summary>
    public static CalibrationDeviceState Device(bool assigned, bool tracked, bool conflict) =>
        conflict ? CalibrationDeviceState.Conflict : !assigned ? CalibrationDeviceState.Unassigned :
        tracked ? CalibrationDeviceState.Ready : CalibrationDeviceState.Lost;
}

/// <summary>準備・姿勢選択・計測・結果の遷移を管理し、キャンセル後の遅延結果を受け付けません。</summary>
internal sealed class CalibrationFlow
{
    /// <summary>現在の段階です。</summary>
    public CalibrationStage Stage { get; private set; }
    /// <summary>プレビューを固定表示するのは姿勢選択とカウントダウンだけです。</summary>
    public bool Preview => Stage is CalibrationStage.Pose or CalibrationStage.Countdown;
    /// <summary>有効な計測中か。画面遷移・キャンセルで即座に解除します。</summary>
    public bool CountingDown => Stage == CalibrationStage.Countdown;
    /// <summary>成功は計算処理の結果であり、ユーザーによる見た目の確認を意味しません。</summary>
    public bool Succeeded { get; private set; }
    /// <summary>ローカライズ前の完了・失敗理由です。</summary>
    public string? Result { get; private set; }
    /// <summary>校正を妨げない警告を、結果を確認するまで保持します。</summary>
    public string? Warning { get; private set; }
    /// <summary>キャンセルしたことを準備画面で伝えます。</summary>
    public bool Canceled { get; private set; }

    /// <summary>新しい準備画面へ戻します。トラッカー設定や実際の校正データは操作しません。</summary>
    public void Reset()
    {
        Stage = CalibrationStage.Preparation;
        Succeeded = Canceled = false;
        Result = Warning = null;
    }

    /// <summary>必要な機器とアバターが揃った場合だけ、固定姿勢のプレビューへ進みます。</summary>
    public bool SelectPose(bool ready)
    {
        if (Stage != CalibrationStage.Preparation || !ready) return false;
        Stage = CalibrationStage.Pose;
        Canceled = false;
        return true;
    }

    /// <summary>準備を再確認し、一度だけカウントダウンへ進みます。</summary>
    public bool Start(bool ready)
    {
        if (Stage != CalibrationStage.Pose || !ready) return false;
        Stage = CalibrationStage.Countdown;
        return true;
    }

    /// <summary>計測中の結果だけを確定し、成功時も失敗時も姿勢固定を解除します。</summary>
    public bool Complete(string result, bool succeeded, string? warning)
    {
        if (!CountingDown) return false;
        Stage = CalibrationStage.Review;
        Result = result;
        Warning = warning;
        Succeeded = succeeded;
        return true;
    }

    /// <summary>固定姿勢または計測を中止し、準備画面へ戻します。</summary>
    public void Cancel()
    {
        var canceled = Preview;
        Reset();
        Canceled = canceled;
    }

    /// <summary>段階に対応する操作部品を設定します。状態表示・隠した一覧を当たり判定からも外します。</summary>
    public void ApplyControls(PanelControlState[] controls, bool ready, bool tPose)
    {
        for (var i = 0; i < PanelControls.RowCount; i++) controls[i] = default;
        controls[PanelControls.TPose] = controls[PanelControls.Standing] = default;
        controls[PanelControls.DashboardNext] = default;
        controls[PanelControls.DashboardPrevious] = default;
        controls[PanelControls.PagePrevious] = controls[PanelControls.PageNext] = default;
        for (var i = PanelControls.DashboardDetails; i < PanelControls.Count; i++) controls[i] = default;
        var stage = Stage;
        if (stage == CalibrationStage.Countdown)
        {
            for (var i = PanelControls.Trackers; i <= PanelControls.Details; i++) controls[i] = default;
            controls[0] = new(PanelControlKind.Primary);
            controls[1] = controls[2] = new(PanelControlKind.Information);
        }
        else if (stage == CalibrationStage.Preparation)
        {
            controls[0] = new(PanelControlKind.Primary, ready);
            for (var i = 1; i <= 4; i++) controls[i] = new(PanelControlKind.Information);
            controls[5] = new(PanelControlKind.Navigation);
        }
        else if (stage == CalibrationStage.Pose)
        {
            controls[0] = new(PanelControlKind.Primary, ready);
            controls[PanelControls.TPose] = new(PanelControlKind.Choice, true, tPose);
            controls[PanelControls.Standing] = new(PanelControlKind.Choice, true, !tPose);
            controls[2] = controls[3] = controls[4] = new(PanelControlKind.Information);
            controls[5] = new(PanelControlKind.Navigation);
        }
        else
        {
            controls[0] = new(PanelControlKind.Primary);
            controls[1] = new(PanelControlKind.Navigation);
            controls[2] = controls[3] = new(PanelControlKind.Information);
        }
    }
}
