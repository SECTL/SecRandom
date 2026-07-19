using Avalonia.Controls;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Views;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views.Settings;

public sealed class MobileDrawSettingsPage : ViewBase
{
    private readonly MainConfigHandler _configHandler;
    private readonly IDrawTemporaryRecordService _temporaryRecordService;
    private readonly IProfileService _profileService;

    public MobileDrawSettingsPage(
        MainConfigHandler configHandler,
        IDrawTemporaryRecordService temporaryRecordService,
        IProfileService profileService)
    {
        _configHandler = configHandler;
        _temporaryRecordService = temporaryRecordService;
        _profileService = profileService;
        Render();
    }

    private void Render()
    {

        var rollCall = _configHandler.Data.RollCallSettings;
        var lottery = _configHandler.Data.LotterySettings;
        var items = new List<Control>
        {
            MobileUi.CreateLabel(LR.S_RollCall),
            MobileUi.CreateChoiceRow(LR.O_Fair, rollCall.DrawType == DrawType.Fair, () => Save(() => rollCall.DrawType = DrawType.Fair)),
            MobileUi.CreateChoiceRow(LR.O_Random, rollCall.DrawType == DrawType.Random, () => Save(() => rollCall.DrawType = DrawType.Random)),
            MobileUi.CreateLabel(LR.S_RepeatMode),
            CreateDrawModeRow(LR.O_NoRepeat, rollCall.DrawMode == DrawMode.NoRepeat, () => rollCall.DrawMode = DrawMode.NoRepeat),
            CreateDrawModeRow(LR.O_Repeat, rollCall.DrawMode == DrawMode.Repeat, () => rollCall.DrawMode = DrawMode.Repeat),
            CreateDrawModeRow(LR.O_HalfRepeat, rollCall.DrawMode == DrawMode.HalfRepeat, () => rollCall.DrawMode = DrawMode.HalfRepeat),
            MobileUi.CreateIntegerRow(LR.S_HalfRepeat, rollCall.HalfRepeat, 1, value => Save(() => rollCall.HalfRepeat = value)),
            MobileUi.CreateLabel(LR.S_ClearRecord),
            MobileUi.CreateChoiceRow(LR.O_Restarted, rollCall.ClearRecord == ClearRecordMode.Restarted, () => Save(() => rollCall.ClearRecord = ClearRecordMode.Restarted)),
            MobileUi.CreateChoiceRow(LR.O_Cleared, rollCall.ClearRecord == ClearRecordMode.Cleared, () => Save(() => rollCall.ClearRecord = ClearRecordMode.Cleared)),
            MobileUi.CreateLabel(LR.S_Lottery),
            MobileUi.CreateChoiceRow(LR.O_Count, lottery.DrawType == LotteryDrawType.Count, () => Save(() => lottery.DrawType = LotteryDrawType.Count)),
            MobileUi.CreateChoiceRow(LR.O_Pan, lottery.DrawType == LotteryDrawType.Pan, () => Save(() => lottery.DrawType = LotteryDrawType.Pan))
        };

        if (lottery.DrawType == LotteryDrawType.Pan)
        {
            items.Add(MobileUi.CreateLabel(LR.S_RepeatMode));
            items.Add(CreateDrawModeRow(LR.O_NoRepeat, lottery.DrawMode == DrawMode.NoRepeat, () => lottery.DrawMode = DrawMode.NoRepeat));
            items.Add(CreateDrawModeRow(LR.O_Repeat, lottery.DrawMode == DrawMode.Repeat, () => lottery.DrawMode = DrawMode.Repeat));
            items.Add(CreateDrawModeRow(LR.O_HalfRepeat, lottery.DrawMode == DrawMode.HalfRepeat, () => lottery.DrawMode = DrawMode.HalfRepeat));
            items.Add(MobileUi.CreateIntegerRow(LR.S_HalfRepeat, lottery.HalfRepeat, 1, value => Save(() => lottery.HalfRepeat = value)));
        }

        items.Add(MobileUi.CreateLabel(LR.S_ClearRecord));
        items.Add(MobileUi.CreateChoiceRow(LR.O_Restarted, lottery.ClearRecord == ClearRecordMode.Restarted, () => Save(() => lottery.ClearRecord = ClearRecordMode.Restarted)));
        items.Add(MobileUi.CreateChoiceRow(LR.O_Cleared, lottery.ClearRecord == ClearRecordMode.Cleared, () => Save(() => lottery.ClearRecord = ClearRecordMode.Cleared)));
        items.Add(MobileUi.CreateSecondaryButton(LR.C_ClearTemporaryRecords, ClearTemporaryRecords));

        Content = MobileUi.CreateSettingsScroll(LR.S_DrawSettings, LR.S_DrawSettings_D, CloseView, items);
    }

    private Control CreateDrawModeRow(string text, bool selected, Action setMode) =>
        MobileUi.CreateChoiceRow(text, selected, () => Save(setMode));

    private void Save(Action mutate)
    {
        mutate();
        _configHandler.Save();
        Render();
    }

    private void ClearTemporaryRecords()
    {
        _temporaryRecordService.ClearStudentList(_profileService.StudentListConfig?.Name ?? "default");
        _temporaryRecordService.ClearPrizeList(_profileService.PrizeListConfig?.Name ?? "default");
        Render();
    }

    private void CloseView() => _ = CloseAsync(reason: ViewCloseReason.Back);
}
