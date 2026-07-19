using Avalonia.Controls;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services.Config;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views.Settings;

internal sealed class MobileFairDrawSettingsPage : UserControl
{
    private readonly MainConfigHandler _configHandler;
    private readonly Action _refresh;

    internal MobileFairDrawSettingsPage(MainConfigHandler configHandler, Action goBack, Action refresh)
    {
        _configHandler = configHandler;
        _refresh = refresh;
        var fairDraw = configHandler.Data.FairDrawSettings;

        Content = MobileUi.CreateSettingsScroll(LR.S_FairDraw, LR.S_FairDraw_D, goBack, [
            MobileUi.CreateToggleRow(LR.S_FairDrawEnabled, fairDraw.FairDraw, value => Save(() => fairDraw.FairDraw = value)),
            MobileUi.CreateToggleRow(LR.S_FairDrawGroup, fairDraw.FairDrawGroup, value => Save(() => fairDraw.FairDrawGroup = value)),
            MobileUi.CreateToggleRow(LR.S_FairDrawGender, fairDraw.FairDrawGender, value => Save(() => fairDraw.FairDrawGender = value)),
            MobileUi.CreateToggleRow(LR.S_FairDrawTime, fairDraw.FairDrawTime, value => Save(() => fairDraw.FairDrawTime = value)),
            MobileUi.CreateLabel(LR.S_FrequencyFunction),
            MobileUi.CreateChoiceRow(LR.O_FrequencyLinear, fairDraw.FrequencyFunction == FrequencyFunctionMode.Linear, () => Save(() => fairDraw.FrequencyFunction = FrequencyFunctionMode.Linear)),
            MobileUi.CreateChoiceRow(LR.O_FrequencySquareRoot, fairDraw.FrequencyFunction == FrequencyFunctionMode.SquareRoot, () => Save(() => fairDraw.FrequencyFunction = FrequencyFunctionMode.SquareRoot)),
            MobileUi.CreateChoiceRow(LR.O_FrequencyIndex, fairDraw.FrequencyFunction == FrequencyFunctionMode.Index, () => Save(() => fairDraw.FrequencyFunction = FrequencyFunctionMode.Index)),
            MobileUi.CreateToggleRow(LR.S_AverageGapProtection, fairDraw.EnableAvgGapProtection, value => Save(() => fairDraw.EnableAvgGapProtection = value)),
            MobileUi.CreateIntegerRow(LR.S_GapThreshold, fairDraw.GapThreshold, 0, value => Save(() => fairDraw.GapThreshold = value)),
            MobileUi.CreateToggleRow(LR.S_ColdStart, fairDraw.ColdStartEnabled, value => Save(() => fairDraw.ColdStartEnabled = value)),
            MobileUi.CreateIntegerRow(LR.S_ColdStartRounds, fairDraw.ColdStartRounds, 1, value => Save(() => fairDraw.ColdStartRounds = value)),
            MobileUi.CreateToggleRow(LR.S_Shield, fairDraw.ShieldEnabled, value => Save(() => fairDraw.ShieldEnabled = value)),
            MobileUi.CreateIntegerRow(LR.S_ShieldTime, fairDraw.ShieldTime, 0, value => Save(() => fairDraw.ShieldTime = value))
        ]);
    }

    private void Save(Action mutate)
    {
        mutate();
        _configHandler.Save();
        _refresh();
    }
}
