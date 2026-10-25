namespace SynthRidersFullBody;

internal sealed partial class SettingsPanel
{
    /// <summary>手のひら補正の変更・初期化を処理します。</summary>
    private PanelCommand HandleHandRotationRow(int row)
    {
        switch (row)
        {
            case 0: _uiConfig.LeftHandRollDegrees -= 15f; break;
            case 1: _uiConfig.LeftHandRollDegrees += 15f; break;
            case 2: _uiConfig.RightHandRollDegrees -= 15f; break;
            case 3: _uiConfig.RightHandRollDegrees += 15f; break;
            case 4: _uiConfig.LeftHandRollDegrees = _uiConfig.RightHandRollDegrees = 0f; break;
            default: return PanelCommand.None;
        }
        _nextTextUpdate = 0f;
        return PanelCommand.HandRotationChanged;
    }

    /// <summary>補正の上限と下限で対応するボタンを無効にします。</summary>
    private void UpdateHandRotationControls()
    {
        SetPaging(1, 2);
        _controls[0] = new(PanelControlKind.Navigation, _uiConfig.LeftHandRollDegrees > -180f);
        _controls[1] = new(PanelControlKind.Navigation, _uiConfig.LeftHandRollDegrees < 180f);
        _controls[2] = new(PanelControlKind.Navigation, _uiConfig.RightHandRollDegrees > -180f);
        _controls[3] = new(PanelControlKind.Navigation, _uiConfig.RightHandRollDegrees < 180f);
        _controls[4] = new(PanelControlKind.Navigation);
        _controls[5] = new(PanelControlKind.Information);
    }

    /// <summary>手を拍手の位置に置き、鏡で確認しながら補正する手順を表示します。</summary>
    private void UpdateHandRotationText()
    {
        _title!.text = UiText.Get("HAND ROTATION");
        _status!.text = UiText.Format("Palm roll: left {0} / right {1} deg", _uiConfig.LeftHandRollDegrees, _uiConfig.RightHandRollDegrees);
        for (var i = 0; i < _rows.Length; i++) VrUi.SetFontSize(_rows[i]!, 30f);
        _rows[0]!.text = UiText.Get("Left palm: -15 deg");
        _rows[1]!.text = UiText.Get("Left palm: +15 deg");
        _rows[2]!.text = UiText.Get("Right palm: -15 deg");
        _rows[3]!.text = UiText.Get("Right palm: +15 deg");
        _rows[4]!.text = UiText.Get("Reset hand rotation");
        _rows[5]!.text = UiText.Get("Active after full-body calibration");
        _footer!.text = UiText.Get("Hold your palms together and adjust each hand using the mirror.\nPosition and game inputs stay the same. No recalibration needed.");
    }
}
