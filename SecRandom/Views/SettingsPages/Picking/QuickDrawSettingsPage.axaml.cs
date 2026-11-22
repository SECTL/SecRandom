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

    public QuickDrawSettingsPage()
    {
        Settings = ViewModel.Config.QuickDrawSettings;
        DefaultClassSource = new DefaultRosterSettingSource(
            "roll_call_list",
            ProfileCatalogManager,
            PickingResources.O_NoList,
            () => Settings.DefaultClass,
            value => Settings.DefaultClass = value,
            () => SettingsView.Current?.IsPreviewMode == true);
        HistoryGroup = new DrawHistoryGroupSetting(
            PickingResources.O_HistoryGroupDefault,
            HistoryGroupKind.RollCall,
            HistoryGroupCatalog,
            () => SettingsView.Current?.IsPreviewMode == true,
            () => Settings.HistoryGroup,
            value => Settings.HistoryGroup = value);
        HistoryGroup.Refresh();
        MusicLibrary.Refresh();
        DataContext = this;
        InitializeComponent();
        ApplyFairDrawModeVisibility();
        SubscribeSettings();
        RefreshStudentLists();
        NormalizeDrawSettings();
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public QuickDrawSettingsConfig Settings { get; }
    public DefaultRosterSettingSource DefaultClassSource { get; }
    /// <summary>随机抽取排第一位、公平抽取第二位，其余算法保持注册顺序，编排抽取固定在最后。</summary>
    public IReadOnlyList<RollCallAlgorithmOption> Algorithms { get; } =
        OrderAlgorithms(RollCallAlgorithmRegistryService.RegisteredAlgorithms
                .Select(x => new RollCallAlgorithmOption(x.Id, x.Name)))
            .Append(OrchestratedOption)
            .ToArray();

    /// <summary>「编排抽取」不是注册的抽样算法，只是「抽取方式」里的第三个选项。</summary>
    private static readonly RollCallAlgorithmOption OrchestratedOption =
        new(DrawModeCatalog.OrchestratedOptionId, PickingResources.O_Orchestrated);

    private static IReadOnlyList<RollCallAlgorithmOption> OrderAlgorithms(
        IEnumerable<RollCallAlgorithmOption> algorithms) =>
        algorithms.OrderBy(option => option.Id switch
        {
            "builtin.random" => 0,
            "builtin.fair" => 1,
            _ => 2
        }).ToArray();

    public RollCallAlgorithmOption? SelectedAlgorithm
    {
        get => DrawModeSelection.IsOrchestrated(Settings.DrawMode)
            ? OrchestratedOption
            : Algorithms.FirstOrDefault(x => string.Equals(x.Id, Settings.AlgorithmId, StringComparison.OrdinalIgnoreCase))
              ?? Algorithms.FirstOrDefault(x => x.Id == "builtin.fair")
              ?? Algorithms.FirstOrDefault();
        set
        {
            if (value is null)
                return;

            if (string.Equals(value.Id, DrawModeCatalog.OrchestratedOptionId, StringComparison.OrdinalIgnoreCase))
            {
                DrawModeSelection.SelectOrchestrated(Settings);
                return;
            }

            if (string.Equals(Settings.AlgorithmId, value.Id, StringComparison.OrdinalIgnoreCase)
                && !DrawModeSelection.IsOrchestrated(Settings.DrawMode))
                return;

            // 改回抽样算法：先把进入编排前的重复策略还回去，否则抽取端还以为这个通道在编排。
            DrawModeSelection.LeaveOrchestrated(Settings);
            Settings.AlgorithmId = value.Id;
            SynchronizeLegacyDrawType();
        }
    }
    public ObservableCollection<string> DefaultClassOptions => DefaultClassSource.Options;
    public ObservableCollection<MusicSelection> MusicSelections => MusicLibrary.Selections;

    /// <summary>
    ///     公平抽取策略：0 = 均匀优先（平均次数硬门槛），1 = 随机优先（零概率软门槛）。
    ///     这是全局公平策略，闪抽声明的算法仍决定是否使用公平抽取。
    /// </summary>
    public int FairDrawModeIndex
    {
        get => ViewModel.Config.FairDrawSettings.Mode == FairDrawMode.RandomFirst ? 1 : 0;
        set
        {
            if (SettingsView.Current?.IsPreviewMode == true || value is < 0 or > 1)
                return;

            var mode = value == 1 ? FairDrawMode.RandomFirst : FairDrawMode.UniformFirst;
            if (ViewModel.Config.FairDrawSettings.Mode == mode)
                return;

            ViewModel.Config.FairDrawSettings.Mode = mode;
            ConfigHandler.Save();
        }
    }

    /// <summary>
    ///     闪抽默认记录组。闪抽没有自己的选择器，所以这一行就是它的默认值：锁定后权重只读该组历史，
    ///     写出的记录也只进该组；选「默认记录组」表示只算未分组的记录。
    /// </summary>
    public DrawHistoryGroupSetting HistoryGroup { get; }

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();
    private IProfileCatalogManager ProfileCatalogManager { get; } = IAppHost.GetService<IProfileCatalogManager>();
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
        ApplyFairDrawModeVisibility();
        ApplyDefaultClassCardVisibility();
        SubscribeSettings();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (!_isSubscribed)
            return;

        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        _isSubscribed = false;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuickDrawSettingsConfig.AlgorithmId))
        {
            SynchronizeLegacyDrawType();
            ApplyFairDrawModeVisibility();
        }

        if (e.PropertyName == nameof(QuickDrawSettingsConfig.DrawMode))
        {
            // 抽取方式换了（含进出编排）：只有非编排时，记住的重复策略才跟着 DrawMode 走。
            DrawModeSelection.SynchronizeRepeatPolicy(Settings);
            ApplyFairDrawModeVisibility();
        }

        // 「抽取模式」下拉框绑的是 SampledDrawMode：非编排时它才是真正生效的重复策略。
        if (e.PropertyName == nameof(QuickDrawSettingsConfig.SampledDrawMode))
            DrawModeSelection.SetRepeatPolicy(Settings, Settings.SampledDrawMode);

        if (e.PropertyName == nameof(QuickDrawSettingsConfig.HistoryGroup))
        {
            HistoryGroup.NotifySelectionChanged();
            SyncHistoryGroupSelection();
        }

        if (e.PropertyName == nameof(QuickDrawSettingsConfig.DefaultClass))
            DefaultClassSource.Sync(DefaultClassSelector);

        NormalizeDrawSettings();
        ConfigHandler.Save();
    }

    private void SubscribeSettings()
    {
        if (_isSubscribed)
            return;

        Settings.PropertyChanged += SettingsOnPropertyChanged;
        _isSubscribed = true;
    }

    /// <summary>重建默认名单候选值，并把选中项落回配置（未配置过时落到第一份并写回）。</summary>
    private void RefreshStudentLists()
    {
        DefaultClassSource.Refresh(DefaultClassSelector);
        ApplyDefaultClassCardVisibility();
    }

    private void DefaultClassSelectorOnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        => DefaultClassSource.OnSelectionChanged(sender, e);

    private void NormalizeDrawSettings()
    {
        if (SettingsView.Current?.IsPreviewMode == true || _normalizingSettings)
            return;

        _normalizingSettings = true;
        try
        {
            SynchronizeLegacyDrawType();

            // 「抽取模式」下拉框绑的是 SampledDrawMode，每一项都必须是 0..2 的重复策略：
            // 手改的配置、旧存档或控制面下发可能写成编排，三个选项的下拉框还会先把越界索引
            // 压成 -1 再经 TwoWay 写回，所以任何非重复策略都要修回当前应显示的那一档。
            var repeatPolicy = DrawModeSelection.ResolveRepeatPolicy(Settings);
            Settings.SampledDrawMode = repeatPolicy;

            // 半重复档位按「抽取模式」下拉框显示的那一档来夹：编排期间显示的是记住的重复策略。
            Settings.HalfRepeat = repeatPolicy switch
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

    /// <summary>公平抽取策略只对公平抽取有意义：抽取方式选随机抽取或编排抽取时整行隐藏。</summary>
    private void ApplyFairDrawModeVisibility()
    {
        if (this.FindControl<FluentAvalonia.UI.Controls.FASettingsExpander>("S_FairDrawMode") is { } card)
            card.IsVisible = !DrawModeSelection.IsOrchestrated(Settings.DrawMode)
                             && !string.Equals(Settings.AlgorithmId, "builtin.random", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>名单可以全部删除：没有名单时这张默认名单卡没有可选项，整卡隐藏。</summary>
    private void ApplyDefaultClassCardVisibility()
    {
        if (this.FindControl<FluentAvalonia.UI.Controls.FASettingsExpander>("S_DefaultClass") is { } card)
            card.IsVisible = DefaultClassSource.HasRosters;
    }

}
