using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Services.Config;
using SecRandom.Models;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;
using SR = SecRandom.Langs.MainPages.History.Resources;

namespace SecRandom.ViewModels;

public sealed partial class LotteryHistoryViewModel : ViewModelBase
{
    private readonly ILogger<LotteryHistoryViewModel> _logger =
        IAppHost.GetService<ILogger<LotteryHistoryViewModel>>();

    private PrizeHistory? _history;
    private Dictionary<string, string> _prizeIdMap = [];  // name → id

    [ObservableProperty] private string? _selectedPoolName;
    [ObservableProperty] private string _selectedMode = HistoryMode.Overview;

    public LotteryHistoryViewModel(MainConfigHandler configHandler) : base(configHandler)
    {
        RefreshCommand = new RelayCommand(Refresh);
        RefreshPoolNames();
        SelectedPoolName = ResolveInitial(PoolNames, Config.HistoryManagementSettings.SelectedPoolName);
    }

    public ObservableCollection<string> PoolNames { get; } = [];
    public ObservableCollection<string> ModeOptions { get; } = [];
    public ObservableCollection<HistoryDisplayRow> Rows { get; } = [];

    public IRelayCommand RefreshCommand { get; }

    private static string? ResolveInitial(IReadOnlyList<string> names, string preferred)
    {
        if (names.Count == 0) return null;
        return names.Contains(preferred) ? preferred : names[0];
    }

    public void Refresh()
    {
        RefreshPoolNames();
        Load();
    }

    private void RefreshPoolNames()
    {
        PoolNames.Clear();
        var dir = Utils.GetDirectoryPath("history", "lottery_history");
        foreach (var f in Directory.GetFiles(dir, "*.json").OrderBy(Path.GetFileName))
            PoolNames.Add(Path.GetFileNameWithoutExtension(f));
    }

    partial void OnSelectedPoolNameChanged(string? value) => Load();
    partial void OnSelectedModeChanged(string value) => BuildRows();

    private void Load()
    {
        Rows.Clear();
        _history = null;
        _prizeIdMap = [];

        if (string.IsNullOrWhiteSpace(SelectedPoolName))
        {
            RebuildModeOptions();
            return;
        }

        try
        {
            _history = new PrizeHistoryConfig(SelectedPoolName).Data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取抽奖历史失败：奖池={PoolName}。", SelectedPoolName);
        }

        try
        {
            var list = new PrizeListConfig(SelectedPoolName).Data;
            _prizeIdMap = list.Prizes
                .Where(p => !string.IsNullOrEmpty(p.Id))
                .ToDictionary(p => p.Name, p => p.Id, StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取奖品列表失败，序号列将为空：奖池={PoolName}。", SelectedPoolName);
        }

        RebuildModeOptions();
        BuildRows();
    }

    private void RebuildModeOptions()
    {
        var current = SelectedMode;
        ModeOptions.Clear();
        ModeOptions.Add(HistoryMode.Overview);
        ModeOptions.Add(HistoryMode.Records);

        if (_history != null)
            foreach (var key in _history.Prizes.Keys)
                ModeOptions.Add(key);

        if (!ModeOptions.Contains(current))
            SelectedMode = HistoryMode.Overview;
    }

    private void BuildRows()
    {
        Rows.Clear();
        if (_history == null) return;

        var prizes = _history.Prizes;
        var mode = SelectedMode;

        if (mode == HistoryMode.Overview)
        {
            foreach (var (name, h) in prizes)
                Rows.Add(new HistoryDisplayRow
                {
                    Id         = _prizeIdMap.GetValueOrDefault(name, string.Empty),
                    Name       = name,
                    TotalCount = h.TotalCount,
                    Weight     = h.Histories.LastOrDefault() is { } last
                                     ? last.Weight.ToString("0.##", CultureInfo.CurrentCulture)
                                     : string.Empty
                });
        }
        else if (mode == HistoryMode.Records)
        {
            foreach (var (name, h) in prizes)
                foreach (var item in h.Histories)
                    Rows.Add(BuildEventRow(name, item, _prizeIdMap.GetValueOrDefault(name, string.Empty)));
            SortByTimeDesc(Rows);
        }
        else if (prizes.TryGetValue(mode, out var target))
        {
            foreach (var item in target.Histories)
                Rows.Add(BuildEventRow(mode, item, _prizeIdMap.GetValueOrDefault(mode, string.Empty)));
            SortByTimeDesc(Rows);
        }
    }

    private static HistoryDisplayRow BuildEventRow(string name, HistoryItem item, string id = "") =>
        new()
        {
            Id          = id,
            Name        = name,
            DrawTime    = item.DrawTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture),
            DrawNumbers = item.DrawNumbers,
            Weight      = item.Weight.ToString("0.##", CultureInfo.CurrentCulture),
            SortTime    = item.DrawTime
        };

    private static void SortByTimeDesc(ObservableCollection<HistoryDisplayRow> rows)
    {
        var sorted = rows.OrderByDescending(r => r.SortTime).ToList();
        rows.Clear();
        foreach (var row in sorted) rows.Add(row);
    }
}
