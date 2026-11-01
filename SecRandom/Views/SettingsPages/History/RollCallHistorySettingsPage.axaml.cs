using System.ComponentModel;
using System.Linq;
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
    }

    public RollCallHistoryViewModel ViewModel { get; }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged += ViewModelOnPropertyChanged;
        ViewModel.Config.LinkageSettings.PropertyChanged += LinkageSettingsOnPropertyChanged;
        ViewModel.RefreshSubjectFiltering();
        UpdateColumns();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        ViewModel.Config.LinkageSettings.PropertyChanged -= LinkageSettingsOnPropertyChanged;
        DataContext = null;
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RollCallHistoryViewModel.SelectedMode)
                           or nameof(RollCallHistoryViewModel.HasWeightRows)
                           or nameof(RollCallHistoryViewModel.ShouldShowSubjectColumn)
                           or nameof(RollCallHistoryViewModel.ShouldShowHistoryGroupColumn))
            UpdateColumns();
    }

    private void LinkageSettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "SubjectHistoryFilterEnabled" or "DataSource")
            ViewModel.RefreshSubjectFiltering();
    }

    /// <summary>表格选中项变化 → 同步给 ViewModel，作为「移到另一组」的作用对象。</summary>
    private void OnHistoryGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        ViewModel.SetSelectedRows(HistoryGrid.SelectedItems.OfType<HistoryDisplayRow>());
    }

    private void OnConfirmMoveHistoryGroup(object? sender, RoutedEventArgs e)
    {
        // 命令已由 Command 绑定执行，这里只负责收起 Flyout
        MoveHistoryGroupButton.Flyout?.Hide();
    }

    /// <summary>
    ///     列可见性按索引设置，所以列的增删必须同步改索引：
    ///     原来 0..11（点名时间/学号/姓名/学生性别/学生小组/点名次数/点名模式/点名人数/选择性别/选择小组/科目/权重），
    ///     现在 0..12，其中 11 是新增的「记录组」，权重后移到 12。
    /// </summary>
    private void UpdateColumns()
    {
        var cols = HistoryGrid.Columns;
        if (cols.Count < 13) return;

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
        cols[8].IsVisible = isPersonal;               // 选择性别
        cols[9].IsVisible = isPersonal;               // 选择小组
        cols[10].IsVisible = ViewModel.ShouldShowSubjectColumn; // 科目
        cols[11].IsVisible = ViewModel.ShouldShowHistoryGroupColumn; // 记录组
        cols[12].IsVisible = ViewModel.HasWeightRows; // 权重
    }
}
