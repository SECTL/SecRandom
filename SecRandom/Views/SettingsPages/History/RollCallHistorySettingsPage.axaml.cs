using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Models;
using SecRandom.ViewModels.SettingsPages.History;
using SR = SecRandom.Langs.MainPages.History.Resources;

namespace SecRandom.Views.SettingsPages.History;

[PageInfo("settings.history.rollCall", FluentIcons.PersonFilled, "settings.history")]
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
                           or nameof(RollCallHistoryViewModel.HasWeightRows))
            UpdateColumns();
    }

    private void UpdateColumns()
    {
        var cols = HistoryGrid.Columns;
        if (cols.Count < 11) return;

        var mode       = ViewModel.SelectedMode;
        var isOverview = mode == HistoryMode.Overview;
        var isRecords  = mode == HistoryMode.Records;
        var isPersonal = !isOverview && !isRecords;

        cols[0].IsVisible = isRecords || isPersonal;  // 点名时间
        cols[1].IsVisible = isOverview || isRecords;  // 学号
        cols[2].IsVisible = isOverview || isRecords;  // 姓名
        cols[3].IsVisible = true;                     // 学生性别
        cols[4].IsVisible = true;                     // 学生小组
        cols[5].IsVisible = isOverview;               // 点名次数
        cols[6].IsVisible = isPersonal;               // 点名模式
        cols[7].IsVisible = isPersonal;               // 点名人数
        cols[8].IsVisible = isRecords || isPersonal;  // 选择性别
        cols[9].IsVisible = isRecords || isPersonal;  // 选择小组
        cols[10].IsVisible = ViewModel.HasWeightRows; // 权重
    }
}
