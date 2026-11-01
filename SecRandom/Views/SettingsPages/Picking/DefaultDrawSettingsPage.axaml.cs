using System.ComponentModel;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs.General;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;
using SecRandom.ViewModels;
using SecRandom.Services.Music;
using PickingResources = SecRandom.Langs.SettingsPages.Picking.Resources;

namespace SecRandom.Views.SettingsPages.Picking;

[PageInfo("settings.picking.default", FluentIcons.DocumentBulletListCubeFilled, "settings.picking")]
public partial class DefaultDrawSettingsPage : UserControl
{
    private bool _isSubscribed;
    private bool _isHistoryGroupsSubscribed;
    private IHistoryGroupCatalog? _historyGroupCatalog;

    public DefaultDrawSettingsPage()
    {
        Settings = ViewModel.Config.DefaultDrawSettings;
        HistoryGroups = IAppHost.GetService<MainConfigHandler>().Data.General.HistoryGroups;
        MusicLibrary.Refresh();
        DataContext = this;
        InitializeComponent();
        SubscribeSettings();
        SubscribeHistoryGroups();
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public DefaultDrawSettingsConfig Settings { get; }
    public HistoryGroupSettingsConfig HistoryGroups { get; }
    public ObservableCollection<MusicSelection> MusicSelections => MusicLibrary.Selections;
    public ObservableCollection<HistoryGroupRow> HistoryGroupRows { get; } = [];

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();
    private MusicLibraryService MusicLibrary { get; } = IAppHost.GetService<MusicLibraryService>();
    private IHistoryGroupCatalog? HistoryGroupCatalog => _historyGroupCatalog ??= IAppHost.TryGetService<IHistoryGroupCatalog>();

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        MusicLibrary.Refresh();
        SubscribeSettings();
        SubscribeHistoryGroups();
        RefreshHistoryGroups();

        MusicSettingsExpander.IsExpanded = true;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        UnsubscribeHistoryGroups();

        if (!_isSubscribed)
            return;

        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        _isSubscribed = false;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ConfigHandler.Save();
    }

    private void SubscribeSettings()
    {
        if (_isSubscribed)
            return;

        Settings.PropertyChanged += SettingsOnPropertyChanged;
        _isSubscribed = true;
    }

    private void HistoryGroupsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The page's own save boundary: property changes on this sub-config are persisted immediately.
        ConfigHandler.Save();

        if (e.PropertyName == nameof(HistoryGroupSettingsConfig.Enabled))
            SyncHistoryGroupSection();
    }

    private void SubscribeHistoryGroups()
    {
        if (_isHistoryGroupsSubscribed)
            return;

        HistoryGroups.PropertyChanged += HistoryGroupsOnPropertyChanged;
        _isHistoryGroupsSubscribed = true;
    }

    private void UnsubscribeHistoryGroups()
    {
        if (!_isHistoryGroupsSubscribed)
            return;

        HistoryGroups.PropertyChanged -= HistoryGroupsOnPropertyChanged;
        _isHistoryGroupsSubscribed = false;
    }

    private void SyncHistoryGroupSection()
    {
        if (!HistoryGroups.Enabled)
        {
            ShowHistoryGroupMessage(string.Empty);
            return;
        }

        RefreshHistoryGroups();
    }

    /// <summary>
    /// Re-reads the roll-call history groups from the catalog and rebuilds the row list.
    /// </summary>
    public void RefreshHistoryGroups()
    {
        HistoryGroupRows.Clear();

        var catalog = HistoryGroupCatalog;
        if (catalog is not null)
        {
            try
            {
                foreach (var name in catalog.GetGroupNames(HistoryGroupKind.RollCall))
                {
                    HistoryGroupRows.Add(new HistoryGroupRow(name)
                    {
                        Count = catalog.CountRecords(HistoryGroupKind.RollCall, name)
                    });
                }
            }
            catch (Exception)
            {
                ShowHistoryGroupMessage(MapHistoryGroupError(null));
            }
        }

        HistoryGroupEmptyHint.IsVisible = HistoryGroupRows.Count == 0;
    }

    private void CreateHistoryGroup_OnClick(object? sender, RoutedEventArgs e)
    {
        var catalog = HistoryGroupCatalog;
        if (catalog is null)
        {
            ShowHistoryGroupMessage(MapHistoryGroupError("group_profile_unavailable"));
            return;
        }

        var name = (NewHistoryGroupNameBox.Text ?? string.Empty).Trim();

        try
        {
            if (catalog.TryCreate(HistoryGroupKind.RollCall, name, out var error))
            {
                NewHistoryGroupNameBox.Text = string.Empty;
                ShowHistoryGroupMessage(string.Empty);
                RefreshHistoryGroups();
                return;
            }

            ShowHistoryGroupMessage(MapHistoryGroupError(error));
        }
        catch (Exception)
        {
            ShowHistoryGroupMessage(MapHistoryGroupError("group_save_failed"));
        }
    }

    private void RenameHistoryGroup_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not HistoryGroupRow row)
            return;

        foreach (var other in HistoryGroupRows)
        {
            other.IsDeleting = false;

            if (!ReferenceEquals(other, row))
                other.IsRenaming = false;
        }

        row.RenameText = row.Name;
        row.IsRenaming = true;
        ShowHistoryGroupMessage(string.Empty);
    }

    private void SaveHistoryGroupRename_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not HistoryGroupRow row)
            return;

        var catalog = HistoryGroupCatalog;
        if (catalog is null)
        {
            ShowHistoryGroupMessage(MapHistoryGroupError("group_profile_unavailable"));
            return;
        }

        try
        {
            if (catalog.TryRename(HistoryGroupKind.RollCall, row.Name, (row.RenameText ?? string.Empty).Trim(), out var error))
            {
                row.IsRenaming = false;
                ShowHistoryGroupMessage(string.Empty);
                RefreshHistoryGroups();
                return;
            }

            ShowHistoryGroupMessage(MapHistoryGroupError(error));
        }
        catch (Exception)
        {
            ShowHistoryGroupMessage(MapHistoryGroupError("group_save_failed"));
        }
    }

    private void CancelHistoryGroupRename_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not HistoryGroupRow row)
            return;

        row.RenameText = row.Name;
        row.IsRenaming = false;
        ShowHistoryGroupMessage(string.Empty);
    }

    private void DeleteHistoryGroup_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not HistoryGroupRow row)
            return;

        // Two-step inline confirmation: the first click only arms this row.
        if (!row.IsDeleting)
        {
            foreach (var other in HistoryGroupRows)
            {
                other.IsDeleting = false;
                other.IsRenaming = false;
            }

            row.IsDeleting = true;
            ShowHistoryGroupMessage(PickingResources.S_HistoryGroups_DeleteHint);
            return;
        }

        var catalog = HistoryGroupCatalog;
        if (catalog is null)
        {
            row.IsDeleting = false;
            ShowHistoryGroupMessage(MapHistoryGroupError("group_profile_unavailable"));
            return;
        }

        try
        {
            catalog.Delete(HistoryGroupKind.RollCall, row.Name);
            ShowHistoryGroupMessage(string.Empty);
        }
        catch (Exception)
        {
            ShowHistoryGroupMessage(MapHistoryGroupError("group_save_failed"));
        }

        RefreshHistoryGroups();
    }

    private void ShowHistoryGroupMessage(string message)
    {
        HistoryGroupMessageText.Text = message;
        HistoryGroupMessageText.IsVisible = !string.IsNullOrEmpty(message);
    }

    private static string MapHistoryGroupError(string? error) => error switch
    {
        "group_name_empty" => PickingResources.S_HistoryGroups_Error_NameEmpty,
        "group_name_too_long" => PickingResources.S_HistoryGroups_Error_NameTooLong,
        "group_name_control_chars" => PickingResources.S_HistoryGroups_Error_NameControlChars,
        "group_name_reserved" => PickingResources.S_HistoryGroups_Error_NameReserved,
        "group_already_exists" => PickingResources.S_HistoryGroups_Error_AlreadyExists,
        "group_not_found" => PickingResources.S_HistoryGroups_Error_NotFound,
        "group_name_unchanged" => PickingResources.S_HistoryGroups_Error_NameUnchanged,
        "group_profile_unavailable" => PickingResources.S_HistoryGroups_Error_ProfileUnavailable,
        "group_save_failed" => PickingResources.S_HistoryGroups_Error_SaveFailed,
        _ => PickingResources.S_HistoryGroups_Error_SaveFailed
    };

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

public sealed class HistoryGroupRow : ObservableObject
{
    private string _name;
    private int _count;
    private bool _isRenaming;
    private bool _isDeleting;
    private string _renameText;

    public HistoryGroupRow(string name)
    {
        _name = name;
        _renameText = name;
    }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
                OnPropertyChanged(nameof(CountText));
        }
    }

    public int Count
    {
        get => _count;
        set
        {
            if (SetProperty(ref _count, value))
                OnPropertyChanged(nameof(CountText));
        }
    }

    public string CountText =>
        string.Format(PickingResources.S_HistoryGroups_RecordCount, Count);

    public bool IsRenaming
    {
        get => _isRenaming;
        set => SetProperty(ref _isRenaming, value);
    }

    public bool IsDeleting
    {
        get => _isDeleting;
        set => SetProperty(ref _isDeleting, value);
    }

    public string RenameText
    {
        get => _renameText;
        set => SetProperty(ref _renameText, value);
    }
}
