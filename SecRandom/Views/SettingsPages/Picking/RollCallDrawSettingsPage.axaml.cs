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

[PageInfo("settings.picking.rollCall", FluentIcons.PersonFilled, "settings.picking")]
public partial class RollCallDrawSettingsPage : UserControl
{
    private bool _normalizingSettings;
    private bool _isSubscribed;
    private bool _syncingHistoryGroupSelection;
    private string _lastKnownDefaultClass = string.Empty;

    public RollCallDrawSettingsPage()
    {
        Settings = ViewModel.Config.RollCallSettings;
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
    public RollCallSettingsConfig Settings { get; }
    public ObservableCollection<string> StudentListNames { get; } = [];
    public IReadOnlyList<RollCallAlgorithmOption> Algorithms { get; } =
        RollCallAlgorithmRegistryService.RegisteredAlgorithms
            .Select(x => new RollCallAlgorithmOption(x.Id, x.Name)).ToArray();

    /// <summary>
    ///     点名页的默认记录组：页面打开时选择器先落到这里，用户当场换组仍然只改界面状态、不写配置。
    /// </summary>
    public DrawHistoryGroupSetting HistoryGroup { get; }

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

    public ObservableCollection<MusicSelection> MusicSelections => MusicLibrary.Selections;

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();
    private IHistoryGroupCatalog HistoryGroupCatalog { get; } = IAppHost.GetService<IHistoryGroupCatalog>();
    private MusicLibraryService MusicLibrary { get; } = IAppHost.GetService<MusicLibraryService>();
    private HistoryGroupSettingsConfig HistoryGroups => ViewModel.Config.General.HistoryGroups;

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
        if (e.PropertyName == nameof(RollCallSettingsConfig.AlgorithmId))
            SynchronizeLegacyDrawType();

        if (e.PropertyName == nameof(RollCallSettingsConfig.HistoryGroup))
        {
            HistoryGroup.NotifySelectionChanged();
            SyncHistoryGroupSelection();
        }

        if (e.PropertyName == nameof(RollCallSettingsConfig.DefaultClass))
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

public sealed record RollCallAlgorithmOption(string Id, string Name);
