using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Models;
using SecRandom.ViewModels;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;
using SR = SecRandom.Langs.MainPages.History.Resources;

namespace SecRandom.ViewModels.MainPages;

/// <summary>
///     主窗口历史记录页 ViewModel。
///     通过 Core 查询服务读取历史，不经过 IProfileService，避免切换当前活跃档案的副作用。
/// </summary>
public partial class HistoryPageViewModel : ViewModelBase
{
    private readonly IHistoryQueryService _historyQueryService;

    private StudentHistory? _rollCallHistory;
    private PrizeHistory? _lotteryHistory;

    [ObservableProperty] private string? _selectedRollCallClassName;
    [ObservableProperty] private string _selectedRollCallMode = HistoryMode.Overview;
    [ObservableProperty] private string? _selectedLotteryPoolName;
    [ObservableProperty] private string _selectedLotteryMode = HistoryMode.Overview;

    public HistoryPageViewModel(MainConfigHandler configHandler, IHistoryQueryService historyQueryService) : base(configHandler)
    {
        _historyQueryService = historyQueryService;
        RefreshCommand = new RelayCommand(RefreshAll);

        RefreshNames();

        SelectedRollCallClassName = ResolveInitialName(RollCallClassNames, Settings.SelectedClassName);
        SelectedLotteryPoolName = ResolveInitialName(LotteryPoolNames, Settings.SelectedPoolName);
    }

    public Core.Models.SubConfigs.HistoryManagementSettingsConfig Settings => Config.HistoryManagementSettings;

    public bool ShowWeight => Settings.SelectWeight;

    public ObservableCollection<string> RollCallClassNames { get; } = [];
    public ObservableCollection<string> RollCallModeOptions { get; } = [];
    public ObservableCollection<HistoryDisplayRow> RollCallRows { get; } = [];

    public ObservableCollection<string> LotteryPoolNames { get; } = [];
    public ObservableCollection<string> LotteryModeOptions { get; } = [];
    public ObservableCollection<HistoryDisplayRow> LotteryRows { get; } = [];

    public IRelayCommand RefreshCommand { get; }

    private static string? ResolveInitialName(IReadOnlyList<string> names, string preferred)
    {
        if (names.Count == 0) return null;
        return names.Contains(preferred) ? preferred : names[0];
    }

    public void RefreshAll()
    {
        RefreshNames();
        LoadRollCall();
        LoadLottery();
    }

    private void RefreshNames()
    {
        UpdateNames(RollCallClassNames, _historyQueryService.GetStudentHistoryNames());
        UpdateNames(LotteryPoolNames, _historyQueryService.GetPrizeHistoryNames());
    }

    private static void UpdateNames(ObservableCollection<string> target, IReadOnlyList<string> names)
    {
        target.Clear();
        foreach (var name in names)
            target.Add(name);
    }

    // ============ 点名历史 ============

    partial void OnSelectedRollCallClassNameChanged(string? value) => LoadRollCall();
    partial void OnSelectedRollCallModeChanged(string value) => BuildRollCallRows();

    private void LoadRollCall()
    {
        RollCallRows.Clear();
        _rollCallHistory = null;

        if (string.IsNullOrWhiteSpace(SelectedRollCallClassName))
        {
            RebuildRollCallModeOptions();
            return;
        }

        _rollCallHistory = _historyQueryService.LoadStudentHistory(SelectedRollCallClassName);

        RebuildRollCallModeOptions();
        BuildRollCallRows();
    }

    private void RebuildRollCallModeOptions()
    {
        var current = SelectedRollCallMode;
        RollCallModeOptions.Clear();
        RollCallModeOptions.Add(HistoryMode.Overview);
        RollCallModeOptions.Add(HistoryMode.Records);

        if (_rollCallHistory != null)
            foreach (var key in _rollCallHistory.Students.Keys)
                RollCallModeOptions.Add(key);

        if (!RollCallModeOptions.Contains(current))
            SelectedRollCallMode = HistoryMode.Overview;
    }

    private void BuildRollCallRows()
    {
        RollCallRows.Clear();
        if (_rollCallHistory == null) return;

        var students = _rollCallHistory.Students;
        var mode = SelectedRollCallMode;

        if (mode == HistoryMode.Overview)
        {
            foreach (var (name, history) in students)
                RollCallRows.Add(new HistoryDisplayRow
                {
                    Name = name,
                    TotalCount = history.TotalCount,
                    Weight = FormatLatestWeight(history)
                });
        }
        else if (mode == HistoryMode.Records)
        {
            foreach (var (name, history) in students)
                foreach (var item in history.Histories)
                    RollCallRows.Add(BuildEventRow(name, item));

            SortByTimeDesc(RollCallRows);
        }
        else if (students.TryGetValue(mode, out var target))
        {
            foreach (var item in target.Histories)
                RollCallRows.Add(BuildEventRow(mode, item));

            SortByTimeDesc(RollCallRows);
        }
    }

    // ============ 抽奖历史 ============

    partial void OnSelectedLotteryPoolNameChanged(string? value) => LoadLottery();
    partial void OnSelectedLotteryModeChanged(string value) => BuildLotteryRows();

    private void LoadLottery()
    {
        LotteryRows.Clear();
        _lotteryHistory = null;

        if (string.IsNullOrWhiteSpace(SelectedLotteryPoolName))
        {
            RebuildLotteryModeOptions();
            return;
        }

        _lotteryHistory = _historyQueryService.LoadPrizeHistory(SelectedLotteryPoolName);

        RebuildLotteryModeOptions();
        BuildLotteryRows();
    }

    private void RebuildLotteryModeOptions()
    {
        var current = SelectedLotteryMode;
        LotteryModeOptions.Clear();
        LotteryModeOptions.Add(HistoryMode.Overview);
        LotteryModeOptions.Add(HistoryMode.Records);

        if (_lotteryHistory != null)
            foreach (var key in _lotteryHistory.Prizes.Keys)
                LotteryModeOptions.Add(key);

        if (!LotteryModeOptions.Contains(current))
            SelectedLotteryMode = HistoryMode.Overview;
    }

    private void BuildLotteryRows()
    {
        LotteryRows.Clear();
        if (_lotteryHistory == null) return;

        var prizes = _lotteryHistory.Prizes;
        var mode = SelectedLotteryMode;

        if (mode == HistoryMode.Overview)
        {
            foreach (var (name, history) in prizes)
                LotteryRows.Add(new HistoryDisplayRow
                {
                    Name = name,
                    TotalCount = history.TotalCount,
                    Weight = FormatLatestWeight(history)
                });
        }
        else if (mode == HistoryMode.Records)
        {
            foreach (var (name, history) in prizes)
                foreach (var item in history.Histories)
                    LotteryRows.Add(BuildEventRow(name, item));

            SortByTimeDesc(LotteryRows);
        }
        else if (prizes.TryGetValue(mode, out var target))
        {
            foreach (var item in target.Histories)
                LotteryRows.Add(BuildEventRow(mode, item));

            SortByTimeDesc(LotteryRows);
        }
    }

    // ============ 公共辅助 ============

    private static HistoryDisplayRow BuildEventRow(string name, HistoryItem item)
    {
        return new HistoryDisplayRow
        {
            Name = name,
            Gender = item.DrawGender,
            Group = item.DrawGroup,
            DrawTime = item.DrawTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture),
            DrawMethod = FormatDrawMethod(item.DrawMethod),
            DrawNumbers = item.DrawNumbers,
            Weight = item.Weight.ToString("0.##", CultureInfo.CurrentCulture),
            SortTime = item.DrawTime
        };
    }

    private static string FormatDrawMethod(int method)
    {
        return method == 0 ? SR.C_MethodRandom : SR.C_MethodWeight;
    }

    private static string FormatLatestWeight(History history)
    {
        var last = history.Histories.LastOrDefault();
        return last == null ? string.Empty : last.Weight.ToString("0.##", CultureInfo.CurrentCulture);
    }

    private static void SortByTimeDesc(ObservableCollection<HistoryDisplayRow> rows)
    {
        var sorted = rows.OrderByDescending(r => r.SortTime).ToList();
        rows.Clear();
        foreach (var row in sorted)
            rows.Add(row);
    }
}
