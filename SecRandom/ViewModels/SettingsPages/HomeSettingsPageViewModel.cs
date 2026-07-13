using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Shared;
using SecRandom.Core.Services.Config;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.ViewModels.SettingsPages;

public sealed partial class HomeSettingsPageViewModel : ViewModelBase
{
    [ObservableProperty] private int _rollCallListCount;
    [ObservableProperty] private int _rollCallTotalRounds;
    [ObservableProperty] private int _rollCallTotalDrawnCount;
    [ObservableProperty] private int _lotteryPoolCount;
    [ObservableProperty] private int _lotteryTotalRounds;
    [ObservableProperty] private int _lotteryTotalDrawnCount;
    [ObservableProperty] private bool _hasRollCallLists;
    [ObservableProperty] private bool _hasLotteryPools;

    public bool HasNoRollCallLists => !HasRollCallLists;
    public bool HasNoLotteryPools => !HasLotteryPools;

    partial void OnHasRollCallListsChanged(bool value) => OnPropertyChanged(nameof(HasNoRollCallLists));
    partial void OnHasLotteryPoolsChanged(bool value) => OnPropertyChanged(nameof(HasNoLotteryPools));

    public HomeSettingsPageViewModel(MainConfigHandler configHandler) : base(configHandler)
    {
    }

    public ObservableCollection<HomeProfileCard> RollCallLists { get; } = [];
    public ObservableCollection<HomeProfileCard> LotteryPools { get; } = [];

    public void Refresh()
    {
        RefreshRollCallLists();
        RefreshLotteryPools();
    }

    private void RefreshRollCallLists()
    {
        var cards = EnumerateProfileNames("roll_call_list")
            .Select(name =>
            {
                var list = new StudentListConfig(name).Data;
                var history = new StudentHistoryConfig(name).Data;
                return new HomeProfileCard(
                    name,
                    list.Students.Count(student => student.IsCandidate),
                    history.TotalRounds,
                    history.TotalStats,
                    FormatLastDrawnTime(history.Students.Values.Select(item => item.LastDrawnTime)));
            })
            .ToList();

        RollCallLists.Clear();
        foreach (var card in cards)
            RollCallLists.Add(card);

        RollCallListCount = cards.Count;
        RollCallTotalRounds = cards.Sum(card => card.TotalRounds);
        RollCallTotalDrawnCount = cards.Sum(card => card.TotalDrawnCount);
        HasRollCallLists = cards.Count > 0;
    }

    private void RefreshLotteryPools()
    {
        var cards = EnumerateProfileNames("lottery_list")
            .Select(name =>
            {
                var list = new PrizeListConfig(name).Data;
                var history = new PrizeHistoryConfig(name).Data;
                return new HomeProfileCard(
                    name,
                    list.Prizes.Count(prize => prize.Exists),
                    history.TotalRounds,
                    history.TotalStats,
                    FormatLastDrawnTime(history.Prizes.Values.Select(item => item.LastDrawnTime)));
            })
            .ToList();

        LotteryPools.Clear();
        foreach (var card in cards)
            LotteryPools.Add(card);

        LotteryPoolCount = cards.Count;
        LotteryTotalRounds = cards.Sum(card => card.TotalRounds);
        LotteryTotalDrawnCount = cards.Sum(card => card.TotalDrawnCount);
        HasLotteryPools = cards.Count > 0;
    }

    private static IEnumerable<string> EnumerateProfileNames(string directory)
    {
        return Directory.GetFiles(Utils.GetDirectoryPath("list", directory), "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.Ordinal)!;
    }

    private static string FormatLastDrawnTime(IEnumerable<DateTime> times)
    {
        var lastDrawnTime = times.DefaultIfEmpty(DateTime.MinValue).Max();
        if (lastDrawnTime == DateTime.MinValue)
            return "暂无记录";

        var elapsed = DateTime.Now - lastDrawnTime;
        if (elapsed < TimeSpan.FromMinutes(1))
            return "刚刚";
        if (elapsed < TimeSpan.FromHours(1))
            return $"{(int)elapsed.TotalMinutes} 分钟前";
        if (lastDrawnTime.Date == DateTime.Today)
            return $"今天 {lastDrawnTime:HH:mm}";
        if (lastDrawnTime.Date == DateTime.Today.AddDays(-1))
            return $"昨天 {lastDrawnTime:HH:mm}";

        return lastDrawnTime.ToString("yyyy-MM-dd HH:mm");
    }
}

public record HomeProfileCard(
    string Name,
    int RecordCount,
    int TotalRounds,
    int TotalDrawnCount,
    string LastDrawnTime);
