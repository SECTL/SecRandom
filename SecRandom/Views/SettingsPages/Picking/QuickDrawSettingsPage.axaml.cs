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

namespace SecRandom.Views.SettingsPages.Picking;

[PageInfo("settings.picking.quickDraw", FluentIcons.FlashFilled, "settings.picking")]
public partial class QuickDrawSettingsPage : UserControl, INotifyPropertyChanged
{
    private bool _normalizingSettings;
    private bool _isSubscribed;
    private bool _refreshingHistoryGroupOptions;
    private string _lastKnownDefaultClass = string.Empty;

    public QuickDrawSettingsPage()
    {
        Settings = ViewModel.Config.QuickDrawSettings;
        _lastKnownDefaultClass = Settings.DefaultClass;
        RefreshHistoryGroupOptions();
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
    ///     「不使用记录组」固定占据列表首项，和点名页的记录组选择器一致。
    /// </summary>
    private static string NoneHistoryGroupOption => SecRandom.Langs.SettingsPages.Picking.Resources.O_QuickDrawHistoryGroupNone;

    /// <summary>闪抽默认记录组的候选值：首项永远是「不使用记录组」。</summary>
    public ObservableCollection<string> HistoryGroupOptions { get; } = [NoneHistoryGroupOption];

    /// <summary>
    ///     闪抽默认锁定的历史记录组；选「不使用记录组」时把配置写回空串。
    /// </summary>
    public string SelectedHistoryGroup
    {
        get => string.IsNullOrWhiteSpace(Settings.HistoryGroup) ? NoneHistoryGroupOption : Settings.HistoryGroup;
        set
        {
            // 重建 ItemsSource 时下拉框会先把选中项清成 null 再回写，用户并没有做任何选择：
            // 这里直接忽略，否则每次进这个页面都会把已经存下的记录组抹掉。
            if (_refreshingHistoryGroupOptions || value is null)
                return;

            var resolved = value == NoneHistoryGroupOption ? string.Empty : value.Trim();
            if (string.Equals(Settings.HistoryGroup, resolved, StringComparison.Ordinal))
                return;
            if (SettingsView.Current?.IsPreviewMode == true)
                return;
            Settings.HistoryGroup = resolved;
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedHistoryGroup)));
        }
    }

    /// <summary>历史记录组总开关关闭时整项不可选，只显示为什么不可用。</summary>
    public bool IsHistoryGroupSelectorVisible => HistoryGroups.Enabled;

    private HistoryGroupSettingsConfig HistoryGroups => ViewModel.Config.General.HistoryGroups;

    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();
    private IHistoryGroupCatalog HistoryGroupCatalog { get; } = IAppHost.GetService<IHistoryGroupCatalog>();
    private MusicLibraryService MusicLibrary { get; } = IAppHost.GetService<MusicLibraryService>();

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        MusicLibrary.Refresh();
        RefreshHistoryGroupOptions();
        RefreshHistoryGroupVisibility();
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

        RefreshHistoryGroupVisibility();
        RefreshHistoryGroupOptions();
    }

    /// <summary>
    ///     重建记录组候选值；读取失败时只降级成「不使用记录组」，闪抽仍然可用。
    /// </summary>
    private void RefreshHistoryGroupOptions()
    {
        _refreshingHistoryGroupOptions = true;
        try
        {
            HistoryGroupOptions.Clear();
            HistoryGroupOptions.Add(NoneHistoryGroupOption);

            // 总开关关闭时不去读记录组：此时配置里那个组名要原样留着，等开关重新打开就能接着用。
            IReadOnlyList<string>? groups = null;
            if (HistoryGroups.Enabled)
            {
                try
                {
                    groups = HistoryGroupCatalog.GetGroupNames(HistoryGroupKind.RollCall);
                }
                catch (Exception)
                {
                    // 读不到组只降级成「不使用记录组」，这一页和闪抽本身都还能用。
                    groups = null;
                }
            }

            if (groups is not null)
            {
                foreach (var group in groups)
                {
                    if (!string.IsNullOrWhiteSpace(group) && !HistoryGroupOptions.Contains(group))
                        HistoryGroupOptions.Add(group);
                }

                // 组可能已被删除或改名：选中项回落为「不使用记录组」，绝不把一个不存在的组名留在界面上。
                var configured = Settings.HistoryGroup;
                if (!string.IsNullOrWhiteSpace(configured)
                    && !groups.Contains(configured, StringComparer.Ordinal)
                    && SettingsView.Current?.IsPreviewMode != true)
                    Settings.HistoryGroup = string.Empty;
            }
        }
        finally
        {
            _refreshingHistoryGroupOptions = false;
        }

        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedHistoryGroup)));
    }

    private void RefreshHistoryGroupVisibility()
    {
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsHistoryGroupSelectorVisible)));
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuickDrawSettingsConfig.AlgorithmId))
            SynchronizeLegacyDrawType();

        if (e.PropertyName == nameof(QuickDrawSettingsConfig.HistoryGroup))
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedHistoryGroup)));

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
