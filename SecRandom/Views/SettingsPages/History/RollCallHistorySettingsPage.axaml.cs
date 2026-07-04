using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Models;
using SecRandom.ViewModels;
using SR = SecRandom.Langs.MainPages.History.Resources;

namespace SecRandom.Views.SettingsPages.History;

[PageInfo("settings.history.rollCall", FluentIcons.PersonRegular, "settings.history")]
public partial class RollCallHistorySettingsPage : UserControl
{
    public RollCallHistorySettingsPage()
    {
        ViewModel = IAppHost.GetService<RollCallHistoryViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModelOnPropertyChanged;
        UpdateColumns();
    }

    public RollCallHistoryViewModel ViewModel { get; }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        DataContext = null;
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RollCallHistoryViewModel.SelectedMode)
                           or nameof(RollCallHistoryViewModel.ShowWeight))
            UpdateColumns();
    }

    private void UpdateColumns()
    {
        var cols = HistoryGrid.Columns;
        if (cols.Count < 9) return;

        var mode       = ViewModel.SelectedMode;
        var showWeight = ViewModel.ShowWeight;
        var isOverview = mode == HistoryMode.Overview;
        var isRecords  = mode == HistoryMode.Records;
        var isPersonal = !isOverview && !isRecords;

        cols[0].IsVisible = isRecords || isPersonal;  // 点名时间
        cols[1].IsVisible = isOverview || isRecords;  // 学号
        cols[2].IsVisible = isOverview || isRecords;  // 姓名
        cols[3].IsVisible = true;                      // 性别 / 性别限制
        cols[4].IsVisible = true;                      // 小组 / 小组限制
        cols[5].IsVisible = isOverview;               // 点名次数
        cols[6].IsVisible = isPersonal;               // 点名模式
        cols[7].IsVisible = isPersonal;               // 点名人数
        cols[8].IsVisible = showWeight;               // 权重

        cols[3].Header = isPersonal ? (object)SR.H_GenderLimit : SR.H_Gender;
        cols[4].Header = isPersonal ? (object)SR.H_GroupLimit  : SR.H_Group;
    }
}
