using Avalonia.Controls;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Icons;
using SecRandom.Core.Services.Config;
using SecRandom.Mobile.Controls;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views.Settings;

/// <summary>
/// 抽取设置页：点名 / 抽奖两组规则（抽取类型、重复规则、半重复阈值、清除记录）。
/// 抽奖组整体按 <see cref="MobileCapabilities.IsLotteryEnabled"/> 投影，关闭时不渲染。
/// 配置存取与 Save 模式（变更 → Save → 重渲染）与原实现等价。
/// </summary>
public sealed class MobileDrawSettingsPage : MobileSettingsPageBase
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
            new MobileSectionHeader(LR.S_RollCall, FluentIcons.PersonFilled),
            MobileSettingRow.Choice(LR.O_Fair, rollCall.DrawType == DrawType.Fair, () => Save(() => rollCall.DrawType = DrawType.Fair)),
            MobileSettingRow.Choice(LR.O_Random, rollCall.DrawType == DrawType.Random, () => Save(() => rollCall.DrawType = DrawType.Random)),
            new MobileSectionHeader(LR.S_RepeatMode),
            CreateDrawModeRow(LR.O_NoRepeat, rollCall.DrawMode == DrawMode.NoRepeat, () => rollCall.DrawMode = DrawMode.NoRepeat),
            CreateDrawModeRow(LR.O_Repeat, rollCall.DrawMode == DrawMode.Repeat, () => rollCall.DrawMode = DrawMode.Repeat),
            CreateDrawModeRow(LR.O_HalfRepeat, rollCall.DrawMode == DrawMode.HalfRepeat, () => rollCall.DrawMode = DrawMode.HalfRepeat),
            MobileSettingRow.Integer(LR.S_HalfRepeat, null, rollCall.HalfRepeat, 1, value => Save(() => rollCall.HalfRepeat = value)),
            new MobileSectionHeader(LR.S_ClearRecord),
            MobileSettingRow.Choice(LR.O_Restarted, rollCall.ClearRecord == ClearRecordMode.Restarted, () => Save(() => rollCall.ClearRecord = ClearRecordMode.Restarted)),
            MobileSettingRow.Choice(LR.O_Cleared, rollCall.ClearRecord == ClearRecordMode.Cleared, () => Save(() => rollCall.ClearRecord = ClearRecordMode.Cleared))
        };

        // 抽奖能力关闭时整组隐藏；清空临时记录按钮同时覆盖点名临时记录，保持在组外。
        if (IsLotteryEnabled)
        {
            items.Add(new MobileSectionHeader(LR.S_Lottery, FluentIcons.GiftFilled));
            items.Add(MobileSettingRow.Choice(LR.O_Count, lottery.DrawType == LotteryDrawType.Count, () => Save(() => lottery.DrawType = LotteryDrawType.Count)));
            items.Add(MobileSettingRow.Choice(LR.O_Pan, lottery.DrawType == LotteryDrawType.Pan, () => Save(() => lottery.DrawType = LotteryDrawType.Pan)));

            if (lottery.DrawType == LotteryDrawType.Pan)
            {
                items.Add(new MobileSectionHeader(LR.S_RepeatMode));
                items.Add(CreateDrawModeRow(LR.O_NoRepeat, lottery.DrawMode == DrawMode.NoRepeat, () => lottery.DrawMode = DrawMode.NoRepeat));
                items.Add(CreateDrawModeRow(LR.O_Repeat, lottery.DrawMode == DrawMode.Repeat, () => lottery.DrawMode = DrawMode.Repeat));
                items.Add(CreateDrawModeRow(LR.O_HalfRepeat, lottery.DrawMode == DrawMode.HalfRepeat, () => lottery.DrawMode = DrawMode.HalfRepeat));
                items.Add(MobileSettingRow.Integer(LR.S_HalfRepeat, null, lottery.HalfRepeat, 1, value => Save(() => lottery.HalfRepeat = value)));
            }

            items.Add(new MobileSectionHeader(LR.S_ClearRecord));
            items.Add(MobileSettingRow.Choice(LR.O_Restarted, lottery.ClearRecord == ClearRecordMode.Restarted, () => Save(() => lottery.ClearRecord = ClearRecordMode.Restarted)));
            items.Add(MobileSettingRow.Choice(LR.O_Cleared, lottery.ClearRecord == ClearRecordMode.Cleared, () => Save(() => lottery.ClearRecord = ClearRecordMode.Cleared)));
        }

        items.Add(MobileUi.CreateSecondaryButton(LR.C_ClearTemporaryRecords, ClearTemporaryRecords));
        Content = BuildPage(LR.S_DrawSettings, LR.S_DrawSettings_D, items);
    }

    private Control CreateDrawModeRow(string text, bool selected, Action setMode) =>
        MobileSettingRow.Choice(text, selected, () => Save(setMode));

    private void Save(Action mutate)
    {
        mutate();
        _configHandler.Save();
        Render();
    }

    private void ClearTemporaryRecords()
    {
        _temporaryRecordService.ClearStudentList(_profileService.StudentListConfig?.Name ?? MobileDefaults.ProfileName);
        _temporaryRecordService.ClearPrizeList(_profileService.PrizeListConfig?.Name ?? MobileDefaults.ProfileName);
        Render();
    }
}
