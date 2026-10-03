using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Abstraction.Services;
using LR = SecRandom.Langs.SettingsPages.HistoryManagement.Resources;

namespace SecRandom.Views.SettingsPages.History;

[PageInfo("settings.history.management", FluentIcons.HistoryFilled, "settings.history")]
public partial class HistoryManagementSettingsPage : UserControl
{
    public HistoryManagementSettingsPage()
    {
        DataContext = this;
        InitializeComponent();
        RefreshCombos();
    }

    private void RefreshCombos()
    {
        var historyQueryService = IAppHost.GetService<IHistoryQueryService>();
        PopulateCombo(RollCallClassCombo, historyQueryService.GetStudentHistoryNames());
        PopulateCombo(LotteryPoolCombo, historyQueryService.GetPrizeHistoryNames());
        PopulateExportCombo(ExportRollCallCombo, LR.O_AllLists, historyQueryService.GetStudentHistoryNames());
        PopulateExportCombo(ExportLotteryCombo, LR.O_AllPools, historyQueryService.GetPrizeHistoryNames());
    }

    private static void PopulateCombo(ComboBox combo, IReadOnlyList<string> names)
    {
        combo.Items.Clear();
        foreach (var name in names)
            combo.Items.Add(name);

        if (combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }

    private static void PopulateExportCombo(ComboBox combo, string allLabel, IReadOnlyList<string> names)
    {
        var options = new List<ExportScopeOption> { new(null, allLabel) };
        options.AddRange(names.Select(name => new ExportScopeOption(name, name)));
        combo.ItemsSource = options;
        combo.SelectedIndex = 0;
    }

    private async void ClearRollCallHistory_OnClick(object? sender, RoutedEventArgs e)
    {
        var name = RollCallClassCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(name))
        {
            this.ShowWarningToast(LR.M_SelectFirst);
            return;
        }

        if (!await ConfirmClearAsync(name)) return;

        try
        {
            IAppHost.GetService<IProfileCatalogManager>().ClearStudentHistory(name);
            RefreshCombos();
            this.ShowSuccessToast(string.Format(LR.M_ClearSuccess, name));
        }
        catch (Exception ex)
        {
            this.ShowErrorToast(string.Format(LR.M_ClearFailed, ex.Message));
        }
    }

    private async void ClearLotteryHistory_OnClick(object? sender, RoutedEventArgs e)
    {
        var name = LotteryPoolCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(name))
        {
            this.ShowWarningToast(LR.M_SelectFirst);
            return;
        }

        if (!await ConfirmClearAsync(name)) return;

        try
        {
            IAppHost.GetService<IProfileCatalogManager>().ClearPrizeHistory(name);
            RefreshCombos();
            this.ShowSuccessToast(string.Format(LR.M_ClearSuccess, name));
        }
        catch (Exception ex)
        {
            this.ShowErrorToast(string.Format(LR.M_ClearFailed, ex.Message));
        }
    }

    private async System.Threading.Tasks.Task<bool> ConfirmClearAsync(string name)
    {
        var result = await new FAContentDialog
        {
            Title = LR.M_ClearConfirm_Title,
            Content = string.Format(LR.M_ClearConfirm_Content, name),
            PrimaryButtonText = LR.C_Clear,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));

        return result == FAContentDialogResult.Primary;
    }

    // ============ 导出历史记录 ============

    private async void ExportRollCallHistory_OnClick(object? sender, RoutedEventArgs e)
    {
        await ExportHistoryAsync(HistoryExportKind.RollCall, ExportRollCallCombo);
    }

    private async void ExportLotteryHistory_OnClick(object? sender, RoutedEventArgs e)
    {
        await ExportHistoryAsync(HistoryExportKind.Lottery, ExportLotteryCombo);
    }

    private async System.Threading.Tasks.Task ExportHistoryAsync(HistoryExportKind kind, ComboBox combo)
    {
        if (combo.SelectedItem is not ExportScopeOption option)
        {
            this.ShowWarningToast(LR.M_SelectExportFirst);
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LR.C_Export,
            SuggestedFileName = BuildSuggestedFileName(kind, option),
            DefaultExtension = "xlsx",
            FileTypeChoices =
            [
                new FilePickerFileType("Excel") { Patterns = ["*.xlsx"] },
                new FilePickerFileType("CSV") { Patterns = ["*.csv"] }
            ]
        });
        if (file is null)
            return;

        var extension = Path.GetExtension(file.Name).ToLowerInvariant();
        var format = extension == ".csv" ? HistoryExportFormat.Csv : HistoryExportFormat.Xlsx;
        // 移动端（SAF）拿不到本地路径：先写临时文件再整体拷贝进目标流。
        var localPath = file.TryGetLocalPath();
        var temporaryPath = localPath ?? Path.Combine(Path.GetTempPath(), $"SecRandom-{Guid.NewGuid():N}{extension}");

        try
        {
            HistoryExportResult result;
            await using (var output = File.Create(temporaryPath))
            {
                result = await IAppHost.GetService<IHistoryExportService>().ExportAsync(
                    output,
                    new HistoryExportRequest(kind, option.ProfileName, format),
                    BuildExportLabels());
            }

            if (localPath is null)
            {
                await using var source = File.OpenRead(temporaryPath);
                await using var destination = await file.OpenWriteAsync();
                await source.CopyToAsync(destination);
            }

            if (result.RecordCount == 0)
            {
                RemoveEmptyExportFile(localPath);
                this.ShowWarningToast(LR.M_NoHistoryToExport);
                return;
            }

            this.ShowSuccessToast(string.Format(LR.M_ExportSuccess, result.RecordCount));
        }
        catch (Exception ex)
        {
            // 目标文件已被本操作创建/截断，失败时清掉半成品，不留下无法打开的文件。
            if (localPath is not null && File.Exists(localPath))
                File.Delete(localPath);

            this.ShowErrorToast(string.Format(LR.M_ExportFailed, ex.Message));
        }
        finally
        {
            if (localPath is null && File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static string BuildSuggestedFileName(HistoryExportKind kind, ExportScopeOption option)
    {
        var kindName = kind == HistoryExportKind.RollCall ? LR.C_ExportRollCallName : LR.C_ExportLotteryName;
        var name = $"{kindName}-{option.ProfileName ?? option.DisplayName}-{DateTime.Now:yyyyMMdd-HHmmss}";
        foreach (var invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');

        return $"SecRandom-{name}";
    }

    private static void RemoveEmptyExportFile(string? localPath)
    {
        // 保存框可能已经建出空文件；确认是本次生成且仍为 0 字节时才清理。
        if (localPath is null || !File.Exists(localPath))
            return;

        if (new FileInfo(localPath).Length == 0)
            File.Delete(localPath);
    }

    private static HistoryExportLabels BuildExportLabels() => new()
    {
        RollCallProfileName = LR.E_ProfileRollCall,
        LotteryProfileName = LR.E_ProfileLottery,
        DrawTime = LR.E_DrawTime,
        RecordNumber = LR.E_RecordNumber,
        RecordName = LR.E_RecordName,
        Gender = LR.E_Gender,
        Group = LR.E_Group,
        DrawMethod = LR.E_DrawMethod,
        DrawCount = LR.E_DrawCount,
        DrawGender = LR.E_DrawGender,
        DrawGroup = LR.E_DrawGroup,
        Subject = LR.E_Subject,
        Weight = LR.E_Weight,
        TotalCount = LR.E_TotalCount,
        LastDrawTime = LR.E_LastDrawTime,
        MethodRandom = LR.E_MethodRandom,
        MethodWeight = LR.E_MethodWeight,
        AllGenders = LR.E_AllGenders,
        AllGroups = LR.E_AllGroups,
        BreakSubject = LR.E_BreakSubject,
        RollCallRecordsSheet = LR.E_SheetRollCallRecords,
        RollCallSummarySheet = LR.E_SheetRollCallSummary,
        LotteryRecordsSheet = LR.E_SheetLotteryRecords,
        LotterySummarySheet = LR.E_SheetLotterySummary
    };

    private sealed record ExportScopeOption(string? ProfileName, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}
