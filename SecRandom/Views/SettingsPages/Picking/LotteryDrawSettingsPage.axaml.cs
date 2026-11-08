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
using SecRandom.Core.Helpers;
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

[PageInfo("settings.picking.lottery", FluentIcons.LotteryFilled, "settings.picking")]
public partial class LotteryDrawSettingsPage : UserControl
{
    private bool _normalizingSettings;
    private bool _isSubscribed;
    private bool _syncingHistoryGroupSelection;

    public LotteryDrawSettingsPage()
    {
        Settings = ViewModel.Config.LotterySettings;
        DefaultPoolSource = new DefaultRosterSettingSource(
            "lottery_list",
            PickingResources.O_NoList,
            () => Settings.DefaultPool,
            value => Settings.DefaultPool = value,
            () => SettingsView.Current?.IsPreviewMode == true);
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
        DataContext = this;
        InitializeComponent();
        SubscribeSettings();
        RefreshPrizeLists();
        NormalizeDrawSettings();
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public LotterySettingsConfig Settings { get; }
    public DefaultRosterSettingSource DefaultPoolSource { get; }
    public ObservableCollection<string> DefaultPoolOptions => DefaultPoolSource.Options;
    public IReadOnlyList<LotteryAlgorithmOption> Algorithms { get; } =
        LotteryAlgorithmRegistryService.RegisteredAlgorithms
            .Select(x => new LotteryAlgorithmOption(x.Id, x.Name)).ToArray();

    /// <summary>
    ///     抽奖页的默认记录组：页面打开时选择器先落到这里，用户当场换组仍然只改界面状态、不写配置。
    /// </summary>
    /// <remarks>抽奖的权重来自学生侧历史，所以组名按 <see cref="HistoryGroupKind.RollCall" /> 登记。</remarks>
    public DrawHistoryGroupSetting HistoryGroup { get; }

    public LotteryAlgorithmOption? SelectedAlgorithm
    {
        get => Algorithms.FirstOrDefault(x => string.Equals(x.Id, Settings.AlgorithmId, StringComparison.OrdinalIgnoreCase))
               ?? Algorithms.FirstOrDefault();
        set
        {
            if (value is null || string.Equals(Settings.AlgorithmId, value.Id, StringComparison.OrdinalIgnoreCase))
                return;
            Settings.AlgorithmId = value.Id;
            if (string.Equals(value.Id, "builtin.inventory", StringComparison.OrdinalIgnoreCase))
                Settings.DrawType = LotteryDrawType.Count;
            else if (string.Equals(value.Id, "builtin.weighted", StringComparison.OrdinalIgnoreCase))
                Settings.DrawType = LotteryDrawType.Pan;
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
        if (e.PropertyName == nameof(LotterySettingsConfig.HistoryGroup))
        {
            HistoryGroup.NotifySelectionChanged();
            SyncHistoryGroupSelection();
        }

        if (e.PropertyName == nameof(LotterySettingsConfig.DefaultPool))
            DefaultPoolSource.Sync(DefaultPoolSelector);

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

    /// <summary>重建默认奖池候选值，并把选中项落回配置（未配置过时落到第一份并写回）。</summary>
    private void RefreshPrizeLists() => DefaultPoolSource.Refresh(DefaultPoolSelector);

    private void DefaultPoolSelectorOnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        => DefaultPoolSource.OnSelectionChanged(sender, e);

    private void NormalizeDrawSettings()
    {
        if (SettingsView.Current?.IsPreviewMode == true || _normalizingSettings)
            return;

        _normalizingSettings = true;
        try
        {
            Settings.HalfRepeat = Settings.DrawMode switch
            {
                DrawMode.Repeat => 0,
                DrawMode.NoRepeat => 1,
                DrawMode.HalfRepeat => System.Math.Clamp(Settings.HalfRepeat, 2, 100),
                _ => Settings.HalfRepeat
            };
            Settings.CustomLotteryShowRandomFormat = LotteryProcessDisplayFormatter.NormalizeTemplate(
                Settings.CustomLotteryShowRandomFormat);
        }
        finally
        {
            _normalizingSettings = false;
        }
    }

    private void InsertCustomLotteryShowRandomFormatTokenOnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string token })
            return;

        var textBox = CustomLotteryShowRandomFormatTextBox;
        var template = Settings.CustomLotteryShowRandomFormat;
        var selectionStart = System.Math.Clamp(textBox.SelectionStart, 0, template.Length);
        var selectionEnd = System.Math.Clamp(textBox.SelectionEnd, selectionStart, template.Length);
        var updated = template[..selectionStart] + token + template[selectionEnd..];

        Settings.CustomLotteryShowRandomFormat = LotteryProcessDisplayFormatter.NormalizeTemplate(updated);
        var caret = System.Math.Min(selectionStart + token.Length, Settings.CustomLotteryShowRandomFormat.Length);
        textBox.SelectionStart = caret;
        textBox.SelectionEnd = caret;
        textBox.Focus();
    }
}

public sealed record LotteryAlgorithmOption(string Id, string Name);
