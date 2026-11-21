using System.ComponentModel;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.ViewModels;
using SecRandom.Services.Music;

namespace SecRandom.Views.SettingsPages.Picking;

[PageInfo("settings.picking.default", FluentIcons.DocumentBulletListCubeFilled, "settings.picking")]
public partial class DefaultDrawSettingsPage : UserControl
{
    private bool _isSubscribed;
    private bool _repairingDrawMode;

    public DefaultDrawSettingsPage()
    {
        Settings = ViewModel.Config.DefaultDrawSettings;
        MusicLibrary.Refresh();
        // 三个选项的下拉框直接绑 DrawMode：越界值必须在 InitializeComponent 之前修掉，
        // 否则下拉框会把非法索引强制成 -1 再经 TwoWay 写回配置。
        RepairOrchestratedDrawMode();
        DataContext = this;
        InitializeComponent();
        SubscribeSettings();
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public DefaultDrawSettingsConfig Settings { get; }
    public ObservableCollection<MusicSelection> MusicSelections => MusicLibrary.Selections;

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();
    private MusicLibraryService MusicLibrary { get; } = IAppHost.GetService<MusicLibraryService>();

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        MusicLibrary.Refresh();
        SubscribeSettings();

        MusicSettingsExpander.IsExpanded = true;
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
        RepairOrchestratedDrawMode();
        ConfigHandler.Save();
    }

    /// <summary>
    ///     编排抽取已经从「抽取模式」移到各通道的「抽取方式」里，默认抽取设置没有那一行；
    ///     旧配置把第三个选项存成 Orchestrated，或者下拉框把越界索引写回成 -1 时，都退回不重复，
    ///     否则三选项下拉框永远绑在一个不存在的索引上。
    /// </summary>
    private void RepairOrchestratedDrawMode()
    {
        if (_repairingDrawMode || SettingsView.Current?.IsPreviewMode == true
                                || Settings.DrawMode is DrawMode.Repeat or DrawMode.NoRepeat or DrawMode.HalfRepeat)
            return;

        _repairingDrawMode = true;
        try
        {
            Settings.DrawMode = DrawMode.NoRepeat;

            // 修复要在下次启动前落盘：默认抽取设置不参与抽取，但一个存成编排的重复策略会让三个选项的下拉框
            // 每次都绑到不存在的索引。
            ConfigHandler.Save();
        }
        finally
        {
            _repairingDrawMode = false;
        }
    }

    private void SubscribeSettings()
    {
        if (_isSubscribed)
            return;

        Settings.PropertyChanged += SettingsOnPropertyChanged;
        _isSubscribed = true;
    }

    private void BatchAvatarButton_OnClick(object? sender, RoutedEventArgs e)
    {
        SettingsView.Current?.OpenDrawer(new BatchAvatarDrawer());
    }

    private void StudentImageViaRoster_OnClick(object? sender, RoutedEventArgs e)
    {
        SettingsView.Current?.SelectNavigationItemById("settings.listManagement.rollCallList");
    }

    private void StudentImageViaPrize_OnClick(object? sender, RoutedEventArgs e)
    {
        SettingsView.Current?.SelectNavigationItemById("settings.listManagement.lotteryList");
    }
}
