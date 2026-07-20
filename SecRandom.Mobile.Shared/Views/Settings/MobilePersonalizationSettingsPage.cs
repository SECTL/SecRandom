using Avalonia.Controls;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Icons;
using SecRandom.Core.Services.Config;
using SecRandom.Mobile.Controls;
using SecRandom.Mobile.Services;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views.Settings;

/// <summary>
/// 个性化设置页：主题三档。主题选择立即保存并应用到 RequestedThemeVariant，
/// 页面内容颜色全部走 DynamicResource，由资源系统随主题刷新。
/// </summary>
public sealed partial class MobilePersonalizationSettingsPage : MobileSettingsPageBase
{
    private readonly MainConfigHandler _configHandler;
    private readonly MobileMediaLibraryService _mediaLibrary;
    private readonly MobileDrawMediaService _drawMedia;

    public MobilePersonalizationSettingsPage(
        MainConfigHandler configHandler,
        MobileMediaLibraryService mediaLibrary,
        MobileDrawMediaService drawMedia,
        IMobileNavigator navigator,
        IMobileCapabilities capabilities)
        : base(navigator, capabilities)
    {
        _configHandler = configHandler;
        _mediaLibrary = mediaLibrary;
        _drawMedia = drawMedia;
        InitializeComponent();
        Render();
    }

    private void Render()
    {
        var appearance = _configHandler.Data.Appearance;
        var items = new List<Control>
        {
            new MobileSectionHeader(LR.S_Theme, FluentIcons.ColorFilled),
            MobileSettingRow.Choice(LR.O_ThemeAuto, appearance.Theme == ThemeMode.Auto, () => ApplyTheme(ThemeMode.Auto)),
            MobileSettingRow.Choice(LR.O_ThemeLight, appearance.Theme == ThemeMode.Light, () => ApplyTheme(ThemeMode.Light)),
            MobileSettingRow.Choice(LR.O_ThemeDark, appearance.Theme == ThemeMode.Dark, () => ApplyTheme(ThemeMode.Dark))
        };
        if (_drawMedia.IsSupported)
        {
            items.Add(new MobileSectionHeader(LR.S_AttachedMusic, FluentIcons.Speaker2Filled));
            items.Add(MobileViewFactory.CreateSecondaryButton(LR.C_ImportMusic, () => _ = ImportMusicAsync()));
            foreach (var track in _mediaLibrary.GetTracks())
                items.Add(MobileSettingRow.Simple(track.DisplayName, null,
                    CreateTrackPreview(track.Id), () => RemoveTrack(track.Id)));
        }
        RenderPage(items);
    }

    private void ApplyTheme(ThemeMode theme)
    {
        _configHandler.Data.Appearance.Theme = theme;
        _configHandler.Save();
        MobileResources.Apply(theme);
        Render();
    }

    private async Task ImportMusicAsync()
    {
        var storage = Avalonia.Controls.TopLevel.GetTopLevel(this)?.StorageProvider;
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
        Render();
    }

    private async void RemoveTrack(string id)
    {
        var result = await new FluentAvalonia.UI.Controls.FAContentDialog
        {
            Title = LR.C_Remove,
            Content = id,
            PrimaryButtonText = LR.C_Remove,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FluentAvalonia.UI.Controls.FAContentDialogButton.Primary
        }.ShowAsync(Avalonia.Controls.TopLevel.GetTopLevel(this));
        if (result == FluentAvalonia.UI.Controls.FAContentDialogResult.Primary && !_mediaLibrary.DeleteMusic(id))
            await ShowMediaErrorAsync();
        Render();
    }

    private Control CreateTrackPreview(string id)
    {
        var button = MobileViewFactory.CreateSecondaryButton(LR.C_Preview, () => _ = _drawMedia.PreviewAsync(id));
        button.MinHeight = 40;
        return button;
    }

    private async Task ShowMediaErrorAsync()
    {
        await new FluentAvalonia.UI.Controls.FAContentDialog
        {
            Title = LR.S_AttachedMusic,
            Content = LR.M_MediaImportFailed,
            CloseButtonText = LR.C_Close,
            DefaultButton = FluentAvalonia.UI.Controls.FAContentDialogButton.Close
        }.ShowAsync(Avalonia.Controls.TopLevel.GetTopLevel(this));
    }
}
