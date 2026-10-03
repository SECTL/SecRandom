using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Attributes;
using SecRandom.Core.Controls;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using LR = SecRandom.Langs.SettingsPages.HistoryManagement.Resources;

namespace SecRandom.Views.SettingsPages.History;

[PageInfo("settings.history.management", FluentIcons.HistoryFilled, "settings.history")]
public partial class HistoryManagementSettingsPage : UserControl
{
    private const string RangeAll = "all";
    private const string RangeLast7Days = "7";
    private const string RangeLast30Days = "30";
    private const string RangeCustom = "custom";
    private const string FormatCsv = "csv";
    private const string SortAscending = "asc";
    private const string BreakCourseMarker = "__break__";

    public HistoryManagementSettingsPage()
    {
        DataContext = this;
        InitializeComponent();
        InitializeExportOptions();
        RefreshCombos();
    }

    private void RefreshCombos()
    {
        var historyQueryService = IAppHost.GetService<IHistoryQueryService>();
        PopulateCombo(RollCallClassCombo, historyQueryService.GetStudentHistoryNames());
        PopulateCombo(LotteryPoolCombo, historyQueryService.GetPrizeHistoryNames());

        var exportService = IAppHost.GetService<IHistoryExportService>();
        PopulateExportProfiles(ExportRollCallProfiles, exportService.GetProfileNames(HistoryExportKind.RollCall));
        PopulateExportProfiles(ExportLotteryProfiles, exportService.GetProfileNames(HistoryExportKind.Lottery));
        ReloadExportOptions(HistoryExportKind.RollCall);
        ReloadExportOptions(HistoryExportKind.Lottery);
    }

    private static void PopulateCombo(ComboBox combo, IReadOnlyList<string> names)
    {
        combo.Items.Clear();
        foreach (var name in names)
            combo.Items.Add(name);

        if (combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }

    // ============ 导出选项 ============

    private void InitializeExportOptions()
    {
        var ranges = new List<ExportOption>
        {
            new(RangeAll, LR.O_TimeAll),
            new(RangeLast7Days, LR.O_TimeLast7Days),
            new(RangeLast30Days, LR.O_TimeLast30Days),
            new(RangeCustom, LR.O_TimeCustom)
        };
        var sorts = new List<ExportOption>
        {
            new("desc", LR.O_SortTimeDesc),
            new(SortAscending, LR.O_SortTimeAsc)
        };
        var formats = new List<ExportOption>
        {
            new("xlsx", LR.O_FormatXlsx),
            new(FormatCsv, LR.O_FormatCsv)
        };

        foreach (var combo in new[] { ExportRollCallRange, ExportLotteryRange })
            PopulateOptionCombo(combo, ranges);

        foreach (var combo in new[] { ExportRollCallSort, ExportLotterySort })
            PopulateOptionCombo(combo, sorts);

        foreach (var combo in new[] { ExportRollCallFormat, ExportLotteryFormat })
            PopulateOptionCombo(combo, formats);
    }

    private static void PopulateOptionCombo(ComboBox combo, IReadOnlyList<ExportOption> options)
    {
        combo.ItemsSource = options;
        combo.SelectedIndex = 0;
    }

    /// <summary>
    ///     名单勾选变化后重建查看对象与课程下拉。MultiComboBox 直接改写绑定集合，
    ///     所以先退订再换集合，避免一次勾选触发多次重建。
    /// </summary>
    private void PopulateExportProfiles(MultiComboBox combo, IReadOnlyList<string> names)
    {
        if (combo.SelectedItems is INotifyCollectionChanged previous)
            previous.CollectionChanged -= ExportProfiles_OnChanged;

        var options = names.Select(name => (object)new ExportOption(name, name)).ToList();
        combo.ItemsSource = options;

        var selected = new AvaloniaList<object>(options);
        combo.SelectedItems = selected;
        selected.CollectionChanged += ExportProfiles_OnChanged;
    }

    private void ExportProfiles_OnChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (ReferenceEquals(sender, ExportRollCallProfiles.SelectedItems))
            ReloadExportOptions(HistoryExportKind.RollCall);
        else if (ReferenceEquals(sender, ExportLotteryProfiles.SelectedItems))
            ReloadExportOptions(HistoryExportKind.Lottery);
    }

    private void ReloadExportOptions(HistoryExportKind kind)
    {
        var controls = ResolveExportControls(kind);
        var profileNames = GetSelectedProfileNames(controls.Profiles);

        if (controls.Subject is null || controls.SubjectRow is null)
            return;

        var subjects = new List<ExportOption> { new(string.Empty, LR.O_AllSubjects) };
        subjects.AddRange(IAppHost.GetService<IHistoryExportService>()
            .GetSubjectOptions(kind, profileNames)
            .Select(subject => new ExportOption(subject, FormatSubject(subject))));

        controls.Subject.ItemsSource = subjects;
        controls.Subject.SelectedIndex = 0;
        // 名单里没有任何课程记录时不显示这一行。
        controls.SubjectRow.IsVisible = subjects.Count > 1;
    }

    private ExportControls ResolveExportControls(HistoryExportKind kind) => kind == HistoryExportKind.RollCall
        ? new ExportControls(
            ExportRollCallProfiles,
            ExportRollCallSubject,
            ExportRollCallSubjectRow,
            ExportRollCallRange,
            ExportRollCallFromRow,
            ExportRollCallToRow,
            ExportRollCallFrom,
            ExportRollCallTo,
            ExportRollCallSort,
            ExportRollCallFormat)
        : new ExportControls(
            ExportLotteryProfiles,
            null,
            null,
            ExportLotteryRange,
            ExportLotteryFromRow,
            ExportLotteryToRow,
            ExportLotteryFrom,
            ExportLotteryTo,
            ExportLotterySort,
            ExportLotteryFormat);

    private void ExportRollCallRange_OnSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        UpdateCustomRangeVisibility(HistoryExportKind.RollCall);

    private void ExportLotteryRange_OnSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        UpdateCustomRangeVisibility(HistoryExportKind.Lottery);

    private void UpdateCustomRangeVisibility(HistoryExportKind kind)
    {
        var controls = ResolveExportControls(kind);
        var isCustom = GetSelectedKey(controls.Range) == RangeCustom;
        controls.FromRow.IsVisible = isCustom;
        controls.ToRow.IsVisible = isCustom;
    }

    private static IReadOnlyList<string> GetSelectedProfileNames(MultiComboBox combo) =>
        combo.SelectedItems?.OfType<ExportOption>().Select(option => option.Key).ToArray() ?? [];

    private static string GetSelectedKey(ComboBox combo) =>
        (combo.SelectedItem as ExportOption)?.Key ?? string.Empty;

    private static string FormatSubject(string subject) =>
        subject == BreakCourseMarker ? LR.E_BreakSubject : subject;

    // ============ 导出 ============

    private async void ExportRollCallHistory_OnClick(object? sender, RoutedEventArgs e)
    {
        await ExportHistoryAsync(HistoryExportKind.RollCall);
    }

    private async void ExportLotteryHistory_OnClick(object? sender, RoutedEventArgs e)
    {
        await ExportHistoryAsync(HistoryExportKind.Lottery);
    }

    private async System.Threading.Tasks.Task ExportHistoryAsync(HistoryExportKind kind)
    {
        var controls = ResolveExportControls(kind);
        var profileNames = GetSelectedProfileNames(controls.Profiles);
        if (profileNames.Count == 0)
        {
            this.ShowWarningToast(LR.M_SelectExportProfiles);
            return;
        }

        if (!TryBuildTimeRange(controls, out var from, out var to))
        {
            this.ShowWarningToast(LR.M_ExportInvalidRange);
            return;
        }

        var format = GetSelectedKey(controls.Format) == FormatCsv
            ? HistoryExportFormat.Csv
            : HistoryExportFormat.Xlsx;
        var sort = GetSelectedKey(controls.Sort) == SortAscending
            ? HistoryExportSort.TimeAscending
            : HistoryExportSort.TimeDescending;

        var subject = controls.Subject is null ? string.Empty : GetSelectedKey(controls.Subject);
        var filter = new HistoryExportFilter(
            string.IsNullOrWhiteSpace(subject) ? null : subject,
            from,
            to);

        // 单个名单直接给一个文件，多个名单按约定打包成 ZIP。
        var multiple = profileNames.Count > 1;
        var extension = multiple ? "zip" : format == HistoryExportFormat.Csv ? "csv" : "xlsx";

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LR.C_Export,
            SuggestedFileName = BuildSuggestedFileName(kind, profileNames, extension),
            DefaultExtension = extension,
            FileTypeChoices = [new FilePickerFileType(extension.ToUpperInvariant()) { Patterns = [$"*.{extension}"] }]
        });
        if (file is null)
            return;

        var localPath = file.TryGetLocalPath();
        var temporaryPath = localPath ?? Path.Combine(Path.GetTempPath(), $"SecRandom-{Guid.NewGuid():N}.{extension}");

        try
        {
            HistoryExportResult result;
            await using (var output = File.Create(temporaryPath))
            {
                result = await IAppHost.GetService<IHistoryExportService>().ExportAsync(
                    output,
                    new HistoryExportRequest(kind, profileNames, filter, format, sort),
                    BuildExportLabels());
            }

            if (localPath is null)
            {
                await using var source = File.OpenRead(temporaryPath);
                await using var destination = await file.OpenWriteAsync();
                await source.CopyToAsync(destination);
            }

            if (result.FileCount == 0)
            {
                RemoveEmptyExportFile(localPath);
                this.ShowWarningToast(LR.M_NoHistoryToExport);
                return;
            }

            this.ShowSuccessToast(string.Format(LR.M_ExportSuccess, result.RecordCount, result.ProfileCount));
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

    private static bool TryBuildTimeRange(ExportControls controls, out DateTime? from, out DateTime? to)
    {
        from = null;
        to = null;

        switch (GetSelectedKey(controls.Range))
        {
            case RangeLast7Days:
                from = DateTime.Now.Date.AddDays(-6);
                return true;
            case RangeLast30Days:
                from = DateTime.Now.Date.AddDays(-29);
                return true;
            case RangeCustom:
                var start = controls.From.SelectedDate?.Date;
                var end = controls.To.SelectedDate?.Date;
                if (start is null || end is null || start > end)
                    return false;

                from = start;
                to = end.Value.AddDays(1).AddSeconds(-1);
                return true;
            default:
                return true;
        }
    }

    private static string BuildSuggestedFileName(
        HistoryExportKind kind,
        IReadOnlyList<string> profileNames,
        string extension)
    {
        var kindName = kind == HistoryExportKind.RollCall ? LR.C_ExportRollCallName : LR.C_ExportLotteryName;
        var scope = profileNames.Count == 1 ? $"-{profileNames[0]}" : string.Empty;
        var name = $"SecRandom-{kindName}{scope}-{DateTime.Now:yyyyMMdd-HHmmss}";
        foreach (var invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');

        return $"{name}.{extension}";
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
        RollCallFileName = LR.C_ExportRollCallName,
        LotteryFileName = LR.C_ExportLotteryName,
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

    // ============ 清除历史记录 ============

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

    private sealed class ExportOption(string key, string displayName)
    {
        public string Key { get; } = key;
        public string DisplayName { get; } = displayName;

        public override string ToString() => DisplayName;
    }

    private sealed record ExportControls(
        MultiComboBox Profiles,
        ComboBox? Subject,
        FASettingsExpanderItem? SubjectRow,
        ComboBox Range,
        FASettingsExpanderItem FromRow,
        FASettingsExpanderItem ToRow,
        DatePicker From,
        DatePicker To,
        ComboBox Sort,
        ComboBox Format);
}
