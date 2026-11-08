using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs.General;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Shared;
using SecRandom.ViewModels;
using SecRandom.Services.Music;
using PickingResources = SecRandom.Langs.SettingsPages.Picking.Resources;

namespace SecRandom.Views.SettingsPages.Picking;

[PageInfo("settings.picking.quickDraw", FluentIcons.FlashFilled, "settings.picking")]
public partial class QuickDrawSettingsPage : UserControl
{
    private bool _normalizingSettings;
    private bool _isSubscribed;
    private bool _syncingHistoryGroupSelection;
    private string _lastKnownDefaultClass = string.Empty;

    public QuickDrawSettingsPage()
    {
        Settings = ViewModel.Config.QuickDrawSettings;
        _lastKnownDefaultClass = Settings.DefaultClass;
        HistoryGroup = new DrawHistoryGroupSetting(
            PickingResources.O_HistoryGroupDefault,
            HistoryGroupKind.RollCall,
            HistoryGroupCatalog,
            () => HistoryGroups.Enabled,
            () => SettingsView.Current?.IsPreviewMode == true,
            () => Settings.HistoryGroup,
            value => Settings.HistoryGroup = value);
        HistoryGroup.Refresh();
        MusicLibrary.Refresh();
        RefreshStudentLists();
        DataContext = this;
        InitializeComponent();
        SubscribeSettings();
        NormalizeDrawSettings();
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public QuickDrawSettingsConfig Settings { get; }
    public IReadOnlyList<RollCallAlgorithmOption> Algorithms { get; } =
        RollCallAlgorithmRegistryService.RegisteredAlgorithms
            .Select(x => new RollCallAlgorithmOption(x.Id, x.Name)).ToArray();

    public RollCallAlgorithmOption? SelectedAlgorithm
    {
        get => Algorithms.FirstOrDefault(x => string.Equals(x.Id, Settings.AlgorithmId, StringComparison.OrdinalIgnoreCase))
               ?? Algorithms.FirstOrDefault();
        set
        {
            if (value is null || string.Equals(Settings.AlgorithmId, value.Id, StringComparison.OrdinalIgnoreCase))
                return;
            Settings.AlgorithmId = value.Id;
            SynchronizeLegacyDrawType();
        }
    }
    public ObservableCollection<string> StudentListNames { get; } = [];
    public ObservableCollection<MusicSelection> MusicSelections => MusicLibrary.Selections;

    /// <summary>
    ///     闪抽默认记录组。闪抽没有自己的选择器，所以这一行就是它的默认值：锁定后权重只读该组历史，
    ///     写出的记录也只进该组；选「默认记录组」表示只算未分组的记录。
    /// </summary>
    public DrawHistoryGroupSetting HistoryGroup { get; }

    private HistoryGroupSettingsConfig HistoryGroups => ViewModel.Config.General.HistoryGroups;

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();
    private IHistoryGroupCatalog HistoryGroupCatalog { get; } = IAppHost.GetService<IHistoryGroupCatalog>();
    private MusicLibraryService MusicLibrary { get; } = IAppHost.GetService<MusicLibraryService>();

    /// <summary>
    ///     用户在这一行里换了组：写回配置。重建候选值期间下拉框会先清空选中项，那不是用户选择。
    /// </summary>
    private void HistoryGroupSelectorOnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingHistoryGroupSelection || HistoryGroupSelector is null)
            return;

        if (HistoryGroupSelector.SelectedItem is string selectedGroup)
            HistoryGroup.Selected = selectedGroup;
    }

    /// <summary>重建候选值并把选中项重新落回下拉框。</summary>
    private void RefreshHistoryGroupSelection()
    {
        _syncingHistoryGroupSelection = true;
        try
        {
            HistoryGroup.Refresh();
            ApplyHistoryGroupSelection();
        }
        finally
        {
            _syncingHistoryGroupSelection = false;
        }
    }

    /// <summary>配置里的组名变了（导入、另一页改、组被删），把选中项同步到下拉框。</summary>
    private void SyncHistoryGroupSelection()
    {
        _syncingHistoryGroupSelection = true;
        try
        {
            ApplyHistoryGroupSelection();
        }
        finally
        {
            _syncingHistoryGroupSelection = false;
        }
    }

    private void ApplyHistoryGroupSelection()
    {
        if (HistoryGroupSelector is null)
            return;

        HistoryGroupSelector.SelectedIndex = HistoryGroup.SelectedIndex;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        MusicLibrary.Refresh();
        RefreshHistoryGroupSelection();
        SubscribeSettings();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (!_isSubscribed)
            return;

        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        HistoryGroups.PropertyChanged -= HistoryGroupsOnPropertyChanged;
        _isSubscribed = false;
    }

    private void HistoryGroupsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(HistoryGroupSettingsConfig.Enabled) or null or ""))
            return;

        HistoryGroup.Refresh();
        ApplyHistoryGroupSelection();
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuickDrawSettingsConfig.AlgorithmId))
            SynchronizeLegacyDrawType();

        if (e.PropertyName == nameof(QuickDrawSettingsConfig.HistoryGroup))
        {
            HistoryGroup.NotifySelectionChanged();
            SyncHistoryGroupSelection();
        }

        if (e.PropertyName == nameof(QuickDrawSettingsConfig.DefaultClass))
        {
            if (string.IsNullOrWhiteSpace(Settings.DefaultClass))
            {
                // 下拉框在 ItemsSource 刷新时会先清空选中项并回写空值，恢复用户之前的选择
                // 而不是清空或回退到第一项，避免默认名单被瞬时空掉后又被兜底逻辑覆盖。
                if (!string.IsNullOrWhiteSpace(_lastKnownDefaultClass))
                    Settings.DefaultClass = _lastKnownDefaultClass;
                return;
            }

            _lastKnownDefaultClass = Settings.DefaultClass;
        }

        NormalizeDrawSettings();
        ConfigHandler.Save();
    }

    private void SubscribeSettings()
    {
        if (_isSubscribed)
            return;

        Settings.PropertyChanged += SettingsOnPropertyChanged;
        HistoryGroups.PropertyChanged += HistoryGroupsOnPropertyChanged;
        _isSubscribed = true;
    }

    private void RefreshStudentLists()
    {
        StudentListNames.Clear();
        foreach (var file in Directory.GetFiles(Utils.GetDirectoryPath("list", "roll_call_list"), "*.json")
                     .OrderBy(Path.GetFileName))
            StudentListNames.Add(Path.GetFileNameWithoutExtension(file));

        if (StudentListNames.Count > 0
            && string.IsNullOrWhiteSpace(Settings.DefaultClass)
            && SettingsView.Current?.IsPreviewMode != true)
        {
            Settings.DefaultClass = StudentListNames[0];
            ConfigHandler.Save();
        }
    }

    private void NormalizeDrawSettings()
    {
        if (SettingsView.Current?.IsPreviewMode == true || _normalizingSettings)
            return;

        _normalizingSettings = true;
        try
        {
            SynchronizeLegacyDrawType();

            Settings.HalfRepeat = Settings.DrawMode switch
            {
                DrawMode.Repeat => 0,
                DrawMode.NoRepeat => 1,
                DrawMode.HalfRepeat => System.Math.Clamp(Settings.HalfRepeat, 2, 100),
                _ => Settings.HalfRepeat
            };

            Settings.DisableAfterClick = System.Math.Clamp(Settings.DisableAfterClick, 0, 60);
        }
        finally
        {
            _normalizingSettings = false;
        }
    }

    private void SynchronizeLegacyDrawType()
    {
        if (SettingsView.Current?.IsPreviewMode == true)
            return;

        Settings.DrawType = string.Equals(Settings.AlgorithmId, "builtin.random", StringComparison.OrdinalIgnoreCase)
            ? DrawType.Random
            : DrawType.Fair;
    }
}
