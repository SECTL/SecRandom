using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Models;
using SecRandom.ViewModels.SettingsPages.History;

namespace SecRandom.Views.SettingsPages.History;

[PageInfo("settings.history.lottery", FluentIcons.LotteryRegular, "settings.history")]
public partial class LotteryHistorySettingsPage : UserControl
{
    public LotteryHistorySettingsPage()
    {
        ViewModel = IAppHost.GetService<LotteryHistoryViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModelOnPropertyChanged;
        UpdateColumns();
    }

    public LotteryHistoryViewModel ViewModel { get; }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        DataContext = null;
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LotteryHistoryViewModel.SelectedMode))
            UpdateColumns();
    }

    private void UpdateColumns()
    {
        var cols = HistoryGrid.Columns;
        if (cols.Count < 6) return;

        var mode       = ViewModel.SelectedMode;
        var isOverview = mode == HistoryMode.Overview;
        var isRecords  = mode == HistoryMode.Records;
        var isPersonal = !isOverview && !isRecords;

        cols[0].IsVisible = isRecords || isPersonal;  // 抽奖时间
        cols[1].IsVisible = isOverview || isRecords;  // 序号
        cols[2].IsVisible = isOverview || isRecords;  // 名称
        cols[3].IsVisible = isPersonal;               // 抽取数量
        cols[4].IsVisible = isOverview;               // 中奖次数
        cols[5].IsVisible = true;                      // 权重（所有模式）
    }
}
