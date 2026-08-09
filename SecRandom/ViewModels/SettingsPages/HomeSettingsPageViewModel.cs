using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Shared.Models.Profile;
using Resources = SecRandom.Langs.SettingsPages.Home.Resources;

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

    private readonly IHistoryQueryService _historyQueryService;
    private readonly IProfileCatalogManager _catalogManager;

    public HomeSettingsPageViewModel(
        MainConfigHandler configHandler,
        IHistoryQueryService historyQueryService,
        IProfileCatalogManager catalogManager) : base(configHandler)
    {
        _historyQueryService = historyQueryService;
        _catalogManager = catalogManager;
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
        var cards = _catalogManager.GetStudentListNames()
            .Select(name =>
            {
                var list = _catalogManager.LoadStudentList(name);
                var history = _historyQueryService.LoadStudentHistory(name);
                return new HomeProfileCard(
                    name,
                    list?.Students.Count(student => student.IsCandidate) ?? 0,
                    history?.TotalRounds ?? 0,
                    history?.TotalStats ?? 0,
                    FormatLastDrawnTime(history?.Students.Values.Select(item => item.LastDrawnTime) ?? []));
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
        var cards = _catalogManager.GetPrizeListNames()
            .Select(name =>
            {
                var list = _catalogManager.LoadPrizeList(name);
                var history = _historyQueryService.LoadPrizeHistory(name);
                return new HomeProfileCard(
                    name,
                    list?.Prizes.Count(prize => prize.IsCandidate) ?? 0,
                    history?.TotalRounds ?? 0,
                    history?.TotalStats ?? 0,
                    FormatLastDrawnTime(history?.Prizes.Values.Select(item => item.LastDrawnTime) ?? []));
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

    private static string FormatLastDrawnTime(IEnumerable<DateTime> times)
    {
        var lastDrawnTime = times.DefaultIfEmpty(DateTime.MinValue).Max();
        if (lastDrawnTime == DateTime.MinValue)
            return Resources.M_NoDrawHistory;

        var elapsed = DateTime.Now - lastDrawnTime;
        if (elapsed < TimeSpan.FromMinutes(1))
            return Resources.M_JustNow;
        if (elapsed < TimeSpan.FromHours(1))
            return string.Format(Resources.M_MinutesAgo, (int)elapsed.TotalMinutes);
        if (lastDrawnTime.Date == DateTime.Today)
            return string.Format(Resources.M_TodayAt, lastDrawnTime.ToString("t", CultureInfo.CurrentCulture));
        if (lastDrawnTime.Date == DateTime.Today.AddDays(-1))
            return string.Format(Resources.M_YesterdayAt, lastDrawnTime.ToString("t", CultureInfo.CurrentCulture));

        return lastDrawnTime.ToString("g", CultureInfo.CurrentCulture);
    }
}

public record HomeProfileCard(
    string Name,
    int RecordCount,
    int TotalRounds,
    int TotalDrawnCount,
    string LastDrawnTime);
