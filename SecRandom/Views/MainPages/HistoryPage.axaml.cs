using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Models;
using SecRandom.ViewModels;

namespace SecRandom.Views.MainPages;

[PageInfo("main.history", FluentIcons.HistoryRegular, hidePageTitle: true)]
public partial class HistoryPage : UserControl
{
    public HistoryPage()
    {
        RollCallVM = IAppHost.GetService<RollCallHistoryViewModel>();
        LotteryVM  = IAppHost.GetService<LotteryHistoryViewModel>();
        DataContext = this;
        InitializeComponent();

        RollCallVM.PropertyChanged += OnRollCallVMChanged;
        LotteryVM.PropertyChanged  += OnLotteryVMChanged;
        UpdateRollCallColumns();
        UpdateLotteryColumns();
    }

    public RollCallHistoryViewModel RollCallVM { get; }
    public LotteryHistoryViewModel  LotteryVM  { get; }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        RollCallVM.PropertyChanged -= OnRollCallVMChanged;
        DataContext = null;
    }

    private void OnRollCallVMChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RollCallHistoryViewModel.SelectedMode)
                           or nameof(RollCallHistoryViewModel.ShowWeight))
            UpdateRollCallColumns();
    }

    private void OnLotteryVMChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LotteryHistoryViewModel.SelectedMode))
            UpdateLotteryColumns();
    }

    private void UpdateRollCallColumns()
    {
        var cols = RollCallGrid.Columns;
        if (cols.Count < 8) return;

        var mode       = RollCallVM.SelectedMode;
        var showWeight = RollCallVM.ShowWeight;
        var isOverview = mode == HistoryMode.Overview;
        var isRecords  = mode == HistoryMode.Records;
        var isPersonal = !isOverview && !isRecords;

        cols[0].IsVisible = isRecords || isPersonal; // 点名时间
        cols[1].IsVisible = isOverview || isRecords; // 姓名
        cols[2].IsVisible = isPersonal;              // 点名模式
        cols[3].IsVisible = isPersonal;              // 点名人数
        cols[4].IsVisible = isPersonal;              // 性别限制
        cols[5].IsVisible = isPersonal;              // 小组限制
        cols[6].IsVisible = isOverview;              // 点名次数
        cols[7].IsVisible = showWeight;              // 权重
    }

    private void UpdateLotteryColumns()
    {
        var cols = LotteryGrid.Columns;
        if (cols.Count < 4) return;

        var mode       = LotteryVM.SelectedMode;
        var isOverview = mode == HistoryMode.Overview;
        var isRecords  = mode == HistoryMode.Records;
        var isPersonal = !isOverview && !isRecords;

        cols[0].IsVisible = isRecords || isPersonal; // 抽奖时间
        cols[1].IsVisible = isOverview || isRecords; // 名称
        cols[2].IsVisible = isPersonal;              // 抽取数量
        cols[3].IsVisible = isOverview;              // 中奖次数
    }
}
