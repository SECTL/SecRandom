using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Models;
using SecRandom.ViewModels.SettingsPages.History;

namespace SecRandom.Views.SettingsPages.History;

[PageInfo("settings.history.lottery", FluentIcons.LotteryFilled, "settings.history")]
public partial class LotteryHistorySettingsPage : UserControl
{
    public LotteryHistorySettingsPage()
    {
        ViewModel = IAppHost.GetService<LotteryHistoryViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModelOnPropertyChanged;
    }

    public LotteryHistoryViewModel ViewModel { get; }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        UpdateColumns();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        DataContext = null;
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LotteryHistoryViewModel.SelectedMode)
            or nameof(LotteryHistoryViewModel.ShouldShowHistoryGroupColumn))
            UpdateColumns();
    }

    private void OnHistoryGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        ViewModel.SetSelectedRows(HistoryGrid.SelectedItems.OfType<HistoryDisplayRow>());
    }

    private void OnConfirmMoveHistoryGroup(object? sender, RoutedEventArgs e)
    {
        // 命令已经执行完移动，这里只负责收起 Flyout（本页不用对话框）
        MoveHistoryGroupButton.Flyout?.Hide();
    }

    private void UpdateColumns()
    {
        var cols = HistoryGrid.Columns;
        if (cols.Count < 7) return;

        var mode       = ViewModel.SelectedMode;
        var isOverview = mode == HistoryMode.Overview;
        var isRecords  = mode == HistoryMode.Records;
        var isPersonal = !isOverview && !isRecords;

        // 列索引是硬编码的，插入列必须同步维护这里的顺序：
        // 原 0..5（抽奖时间/序号/名称/抽取数量/中奖次数/权重）→ 现 0..6，其中新插入的 5 是记录组，权重后移到 6
        cols[0].IsVisible = isRecords || isPersonal;               // 抽奖时间
        cols[1].IsVisible = isOverview || isRecords;               // 序号
        cols[2].IsVisible = isOverview || isRecords;               // 名称
        cols[3].IsVisible = isPersonal;                            // 抽取数量
        cols[4].IsVisible = isOverview;                            // 中奖次数
        cols[5].IsVisible = ViewModel.ShouldShowHistoryGroupColumn; // 记录组
        cols[6].IsVisible = true;                                  // 权重（所有模式）
    }
}
