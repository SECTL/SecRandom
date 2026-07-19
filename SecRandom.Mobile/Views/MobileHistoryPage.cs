using Avalonia.Controls;
using SecRandom.Core.Abstraction.Services;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views;

internal sealed class MobileHistoryPage : UserControl
{
    internal MobileHistoryPage(
        IProfileService profileService,
        IDrawTemporaryRecordService temporaryRecordService,
        Action refresh)
    {
        var entries = (profileService.CurrentStudentHistory?.Students.Values ?? [])
            .SelectMany(item => item.Histories)
            .Concat((profileService.CurrentPrizeHistory?.Prizes.Values ?? []).SelectMany(item => item.Histories))
            .OrderByDescending(item => item.DrawTime)
            .Take(30)
            .ToList();

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
                var name = string.IsNullOrWhiteSpace(item.RecordName) ? item.RecordNumber : item.RecordName;
                rows.Children.Add(MobileUi.CreateRow(
                    name,
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
                refresh();
            })
        ]);
    }
}
