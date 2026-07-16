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
using SecRandom.ViewModels;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;
using History = SecRandom.Shared.Models.Profile.History;
using ProfileHistory = SecRandom.Shared.Models.Profile.History;

namespace SecRandom.ViewModels.SettingsPages.History;

public sealed partial class LotteryHistoryViewModel : ViewModelBase
{
    private readonly ILogger<LotteryHistoryViewModel> _logger =
        IAppHost.GetService<ILogger<LotteryHistoryViewModel>>();

    private PrizeHistory? _history;
    private PrizeList? _prizeList;
    private Dictionary<string, Prize> _prizeByKey = [];
    private Dictionary<string, PrizeInfo> _prizeInfoByKey = [];
    private HashSet<string> _uniqueLegacyKeys = [];
    private int _prizeIdPadWidth;

    [ObservableProperty] private string? _selectedPoolName;
    [ObservableProperty] private string _selectedMode = HistoryMode.Overview;

    public LotteryHistoryViewModel(MainConfigHandler configHandler) : base(configHandler)
    {
        RefreshCommand = new RelayCommand(Refresh);
        RefreshPoolNames();
        SelectedPoolName = ResolveInitial(PoolNames, Config.HistoryManagementSettings.SelectedPoolName);
    }

    public ObservableCollection<string> PoolNames { get; } = [];
    public ObservableCollection<HistoryModeOption> ModeOptions { get; } = [];
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
        foreach (var name in EnumerateProfileNames("list", "lottery_list", "history", "lottery_history"))
            PoolNames.Add(name);
    }

    private static IEnumerable<string> EnumerateProfileNames(
        string listRoot,
        string listSubDir,
        string historyRoot,
        string historySubDir)
    {
        HashSet<string> names = [];
        foreach (var file in Directory.GetFiles(Utils.GetDirectoryPath(listRoot, listSubDir), "*.json")
                     .Concat(Directory.GetFiles(Utils.GetDirectoryPath(historyRoot, historySubDir), "*.json")))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name);
        }

        return names.OrderBy(name => name, StringComparer.Ordinal);
    }

    partial void OnSelectedPoolNameChanged(string? value) => Load();
    partial void OnSelectedModeChanged(string value) => BuildRows();

    private void Load()
    {
        Rows.Clear();
        _history = null;
        _prizeList = null;
        _prizeByKey = [];
        _prizeInfoByKey = [];
        _uniqueLegacyKeys = [];
        _prizeIdPadWidth = 0;

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
            _prizeList = new PrizeListConfig(SelectedPoolName).Data;
            _uniqueLegacyKeys = ProfileRecordIdentity.BuildUniquePrizeLegacyKeySet(_prizeList.Prizes);
            _prizeByKey = BuildPrizeMap(_prizeList.Prizes, _uniqueLegacyKeys);
            _prizeInfoByKey = BuildPrizeInfoMap(_prizeList.Prizes, _uniqueLegacyKeys);
            _prizeIdPadWidth = CalculateNumericPadWidth(_prizeList.Prizes.Select(prize => prize.Id));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取奖品列表失败，抽奖历史将只显示历史键：奖池={PoolName}。", SelectedPoolName);
        }

        RebuildModeOptions();
        BuildRows();
    }

    private void RebuildModeOptions()
    {
        var current = SelectedMode;
        ModeOptions.Clear();
        ModeOptions.Add(new HistoryModeOption { Key = HistoryMode.Overview, DisplayName = SecRandom.Langs.MainPages.History.Resources.C_ModeOverview });
        ModeOptions.Add(new HistoryModeOption { Key = HistoryMode.Records, DisplayName = SecRandom.Langs.MainPages.History.Resources.C_ModeRecords });

        foreach (var prize in GetVisiblePrizes())
        {
            var key = ProfileRecordIdentity.EnsureRecordId(prize);
            ModeOptions.Add(new HistoryModeOption { Key = key, DisplayName = FormatPrizeName(prize) });
        }

        if (_history != null)
            foreach (var key in _history.Prizes.Keys.Where(key => !ModeOptions.Any(option => option.Key == key)))
                ModeOptions.Add(new HistoryModeOption { Key = key, DisplayName = ResolvePrizeInfo(key).Name });

        if (!ModeOptions.Any(option => option.Key == current))
            SelectedMode = HistoryMode.Overview;
    }

    private void BuildRows()
    {
        Rows.Clear();
        if (_history == null) return;

        var mode = SelectedMode;
        if (mode == HistoryMode.Overview)
        {
            foreach (var prize in GetVisiblePrizes())
                Rows.Add(BuildOverviewRow(prize));

            AddOrphanOverviewRows();
        }
        else if (mode == HistoryMode.Records)
        {
            foreach (var prize in GetVisiblePrizes())
                AddHistoryRows(prize);

            AddOrphanHistoryRows();
            SortByTimeDesc(Rows);
        }
        else if (_prizeByKey.TryGetValue(mode, out var prize))
        {
            var history = ResolveHistory(prize);
            if (history is null)
                return;

            var info = PrizeInfo.From(prize);
            foreach (var item in history.Histories)
                Rows.Add(BuildEventRow(info, item, _prizeIdPadWidth));
            SortByTimeDesc(Rows);
        }
        else if (_history.Prizes.TryGetValue(mode, out var target))
        {
            var info = ResolvePrizeInfo(mode);
            foreach (var item in target.Histories)
                Rows.Add(BuildEventRow(info, item, _prizeIdPadWidth));
            SortByTimeDesc(Rows);
        }
    }

    private IEnumerable<Prize> GetVisiblePrizes()
    {
        return _prizeList?.Prizes.Where(prize => prize.IsCandidate) ?? [];
    }

    private HistoryDisplayRow BuildOverviewRow(Prize prize)
    {
        var history = ResolveHistory(prize);
        return new HistoryDisplayRow
        {
            Id = FormatNumericId(prize.Id, _prizeIdPadWidth),
            Name = prize.Name,
            TotalCount = history?.TotalCount ?? 0,
            Weight = FormatWeight(prize.Weight)
        };
    }

    private void AddHistoryRows(Prize prize)
    {
        var history = ResolveHistory(prize);
        if (history is null)
            return;

        var info = PrizeInfo.From(prize);
        foreach (var item in history.Histories)
            Rows.Add(BuildEventRow(info, item, _prizeIdPadWidth));
    }

    private ProfileHistory? ResolveHistory(Prize prize)
    {
        if (_history is null)
            return null;

        return ProfileRecordIdentity.GetPrizeHistory(_history, prize, _uniqueLegacyKeys.Contains);
    }

    private void AddOrphanOverviewRows()
    {
        if (_history is null)
            return;

        var knownKeys = BuildKnownPrizeHistoryKeys();
        foreach (var (key, history) in _history.Prizes.Where(pair => !knownKeys.Contains(pair.Key)))
        {
            var info = ResolvePrizeInfo(key);
            Rows.Add(new HistoryDisplayRow
            {
                Id = FormatNumericId(info.Id, _prizeIdPadWidth),
                Name = info.Name,
                TotalCount = history.TotalCount,
                Weight = FormatLatestWeight(history)
            });
        }
    }

    private void AddOrphanHistoryRows()
    {
        if (_history is null)
            return;

        var knownKeys = BuildKnownPrizeHistoryKeys();
        foreach (var (key, history) in _history.Prizes.Where(pair => !knownKeys.Contains(pair.Key)))
        {
            var info = ResolvePrizeInfo(key);
            foreach (var item in history.Histories)
                Rows.Add(BuildEventRow(info, item, _prizeIdPadWidth));
        }
    }

    private HashSet<string> BuildKnownPrizeHistoryKeys()
    {
        HashSet<string> keys = [];
        foreach (var prize in GetVisiblePrizes())
        {
            keys.Add(ProfileRecordIdentity.EnsureRecordId(prize));
            foreach (var key in ProfileRecordIdentity.GetLegacyPrizeHistoryKeys(prize).Where(_uniqueLegacyKeys.Contains))
                keys.Add(key);
        }

        return keys;
    }

    private PrizeInfo ResolvePrizeInfo(string historyKey)
    {
        return _prizeInfoByKey.GetValueOrDefault(historyKey) ?? PrizeInfo.Unknown(historyKey);
    }

    private static Dictionary<string, PrizeInfo> BuildPrizeInfoMap(
        IEnumerable<Prize> prizes,
        ISet<string> uniqueLegacyKeys)
    {
        Dictionary<string, PrizeInfo> result = [];
        foreach (var prize in prizes)
        {
            var info = PrizeInfo.From(prize);
            result[ProfileRecordIdentity.EnsureRecordId(prize)] = info;
            foreach (var key in ProfileRecordIdentity.GetLegacyPrizeHistoryKeys(prize).Where(uniqueLegacyKeys.Contains))
                result.TryAdd(key, info);
        }

        return result;
    }

    private static Dictionary<string, Prize> BuildPrizeMap(
        IEnumerable<Prize> prizes,
        ISet<string> uniqueLegacyKeys)
    {
        Dictionary<string, Prize> result = [];
        foreach (var prize in prizes)
        {
            result[ProfileRecordIdentity.EnsureRecordId(prize)] = prize;
            foreach (var key in ProfileRecordIdentity.GetLegacyPrizeHistoryKeys(prize).Where(uniqueLegacyKeys.Contains))
                result.TryAdd(key, prize);
        }

        return result;
    }

    private static HistoryDisplayRow BuildEventRow(PrizeInfo info, HistoryItem item, int idPadWidth) =>
        new()
        {
            Id = FormatNumericId(string.IsNullOrWhiteSpace(item.RecordNumber) ? info.Id : item.RecordNumber, idPadWidth),
            Name = string.IsNullOrWhiteSpace(item.RecordName) ? info.Name : item.RecordName,
            DrawTime = item.DrawTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture),
            DrawNumbers = item.DrawNumbers,
            Weight = FormatWeight(item.Weight),
            SortTime = item.DrawTime
        };

    private static string FormatPrizeName(Prize prize)
    {
        return string.IsNullOrWhiteSpace(prize.Id) ? prize.Name : $"{prize.Id} {prize.Name}";
    }

    private static int CalculateNumericPadWidth(IEnumerable<string> values)
    {
        return values
            .Where(value => int.TryParse(value, out _))
            .Select(value => value.Trim().Length)
            .DefaultIfEmpty(0)
            .Max();
    }

    private static string FormatNumericId(string value, int width)
    {
        var trimmed = value.Trim();
        return width > 0 && int.TryParse(trimmed, out var number)
            ? number.ToString($"D{width}", CultureInfo.CurrentCulture)
            : trimmed;
    }

    private static string FormatWeight(double weight)
    {
        return weight.ToString("0.00", CultureInfo.CurrentCulture);
    }

    private static string FormatLatestWeight(ProfileHistory history) =>
        history.Histories.LastOrDefault() is { } last
            ? FormatWeight(last.Weight)
            : string.Empty;

    private static void SortByTimeDesc(ObservableCollection<HistoryDisplayRow> rows)
    {
        var sorted = rows.OrderByDescending(r => r.SortTime).ToList();
        rows.Clear();
        foreach (var row in sorted) rows.Add(row);
    }

    private sealed record PrizeInfo(string Name, string Id)
    {
        public static PrizeInfo From(Prize prize) => new(prize.Name, prize.Id);
        public static PrizeInfo Unknown(string key) => new(key, string.Empty);
    }
}
