using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Icons;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Mobile;
using LR = SecRandom.Langs.Mobile.Resources;

namespace SecRandom.Views.Mobile.Settings;

/// <summary>
/// Mobile appearance and managed music-library settings. AXAML owns the stable form;
/// code-behind only projects the runtime music collection and StorageProvider actions.
/// </summary>
[PageInfo(MobilePageIds.Personalization, FluentIcons.ColorFilled, "settings.mobile.preferences")]
public sealed partial class MobilePersonalizationSettingsPage : MobileSettingsPageBase
{
    private readonly MainConfigHandler _configHandler;
    private readonly MobileMediaLibraryService _mediaLibrary;
    private readonly MobileDrawMediaService _drawMedia;
    private bool _synchronizing;

    public MobilePersonalizationSettingsPage(
        MainConfigHandler configHandler,
        MobileMediaLibraryService mediaLibrary,
        MobileDrawMediaService drawMedia,
        IMobileCapabilities capabilities)
        : base(capabilities)
    {
        _configHandler = configHandler;
        _mediaLibrary = mediaLibrary;
        _drawMedia = drawMedia;
        InitializeComponent();
        RefreshSurface();
    }

    private void RefreshSurface()
    {
        _synchronizing = true;
        try
        {
            ThemeSelector.SelectedItem = ThemeSelector.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag is ThemeMode mode && mode == _configHandler.Data.Appearance.Theme);
            MusicLibrarySection.IsVisible = _drawMedia.IsSupported;
            if (!_drawMedia.IsSupported)
            {
                TrackItems.ItemsSource = null;
                return;
            }

            TrackItems.ItemsSource = _mediaLibrary.GetTracks();
        }
        finally
        {
            _synchronizing = false;
        }
    }

    private void ThemeSelector_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizing || (ThemeSelector.SelectedItem as ComboBoxItem)?.Tag is not ThemeMode theme ||
            theme == _configHandler.Data.Appearance.Theme)
            return;

        _configHandler.Data.Appearance.Theme = theme;
        _configHandler.Save();
    }

    private async void ImportMusic_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
            return;

        var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter =
            [
                new Avalonia.Platform.Storage.FilePickerFileType(LR.S_AttachedMusic)
                {
                    Patterns = ["*.mp3", "*.wav", "*.flac"]
                }
            ]
        });
        if (files.FirstOrDefault() is { } file && await _mediaLibrary.ImportMusicAsync(file) is null)
            await ShowMediaErrorAsync();
        RefreshSurface();
    }

    private async void PreviewTrack_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is string id)
            await _drawMedia.PreviewAsync(id);
    }

    private async void RemoveTrack_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is string id)
            await RemoveTrackAsync(id);
    }

    private async Task RemoveTrackAsync(string id)
    {
        var result = await new FAContentDialog
        {
            Title = LR.C_Remove,
            Content = id,
            PrimaryButtonText = LR.C_Remove,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Primary
        }.ShowAsync(TopLevel.GetTopLevel(this));
        if (result == FAContentDialogResult.Primary && !_mediaLibrary.DeleteMusic(id))
            await ShowMediaErrorAsync();
        RefreshSurface();
    }

    private Task ShowMediaErrorAsync() => new FAContentDialog
    {
        Title = LR.S_AttachedMusic,
        Content = LR.M_MediaImportFailed,
        CloseButtonText = LR.C_Close,
        DefaultButton = FAContentDialogButton.Close
    }.ShowAsync(TopLevel.GetTopLevel(this));
}
