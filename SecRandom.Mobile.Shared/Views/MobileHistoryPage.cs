using Avalonia.Controls;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Views;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views;

public sealed class MobileHistoryPage : ViewBase
{
    public MobileHistoryPage(
        IProfileService profileService,
        IHistoryQueryService historyQueryService,
        IDrawTemporaryRecordService temporaryRecordService)
    {
        Render();

        void Render()
        {
        var entries = historyQueryService.GetRecentItems(30);

        var rows = new StackPanel { Spacing = 8 };
        if (entries.Count == 0)
        {
            rows.Children.Add(new TextBlock
            {
                Text = LR.M_NoHistory,
                Foreground = MobileTheme.MutedText,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });
        }
        else
        {
            foreach (var item in entries)
            {
                rows.Children.Add(MobileUi.CreateRow(
                    item.DisplayName,
                    item.DrawTime.ToString("g", System.Globalization.CultureInfo.CurrentCulture)));
            }
        }

            Content = MobileUi.CreateScroll([
            MobileUi.CreateLabel(LR.S_RecentDraws),
            MobileUi.CreateTitle(LR.P_History),
            rows,
            MobileUi.CreateSecondaryButton(LR.C_ClearTemporaryRecords, () =>
            {
                temporaryRecordService.ClearStudentList(profileService.StudentListConfig?.Name ?? "default");
                temporaryRecordService.ClearPrizeList(profileService.PrizeListConfig?.Name ?? "default");
                    Render();
            })
        ]);
        }
    }
}
