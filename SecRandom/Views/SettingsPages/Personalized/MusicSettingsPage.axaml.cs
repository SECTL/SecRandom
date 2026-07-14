using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Services.Draw;
using SecRandom.Services.Music;
using LR = SecRandom.Langs.SettingsPages.Personalized.Music.Resources;

namespace SecRandom.Views.SettingsPages.Personalized;

[PageInfo("settings.personalized.music", FluentIcons.Speaker2Filled, "settings.personalized")]
public partial class MusicSettingsPage : UserControl, INotifyPropertyChanged
{
    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    public MusicSettingsPage()
    {
        Library.Refresh();
        Library.Tracks.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasTracks));
        };
        DataContext = this;
        InitializeComponent();
    }

    public MusicLibraryService Library { get; } = IAppHost.GetService<MusicLibraryService>();
    private DrawAudioService DrawAudio { get; } = IAppHost.GetService<DrawAudioService>();
    public bool IsEmpty => Library.Tracks.Count == 0;
    public bool HasTracks => !IsEmpty;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private async void Import_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LR.C_Import,
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType(LR.S_Library) { Patterns = ["*.mp3", "*.wav", "*.flac"] }
            ]
        });

        try
        {
            var imported = Library.Import(files.Select(file => file.TryGetLocalPath()).OfType<string>());
            if (imported.Count > 0)
                this.ShowSuccessToast(string.Format(LR.M_Imported, imported.Count));
            else if (files.Count > 0)
                this.ShowErrorToast(LR.M_ImportFailed);
        }
        catch (Exception)
        {
            this.ShowErrorToast(LR.M_ImportFailed);
        }
    }

    private async void Preview_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is MusicTrack track)
        {
            if (!await DrawAudio.PreviewAsync(track.Id))
                this.ShowErrorToast(LR.M_PreviewFailed);
        }
    }

    private async void StopPreview_OnClick(object? sender, RoutedEventArgs e)
    {
        await DrawAudio.StopAsync();
    }

    private async void Delete_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not MusicTrack track)
            return;

        var result = await new FAContentDialog
        {
            Title = LR.M_DeleteTitle,
            Content = string.Format(LR.M_DeleteContent, track.DisplayName),
            PrimaryButtonText = LR.M_DeletePrimary,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));
        if (result != FAContentDialogResult.Primary)
            return;

        await DrawAudio.StopAsync();
        try
        {
            if (Library.Delete(track))
                this.ShowSuccessToast(string.Format(LR.M_DeleteSuccess, track.DisplayName));
            else
                this.ShowErrorToast(LR.M_DeleteFailed);
        }
        catch (Exception)
        {
            this.ShowErrorToast(LR.M_DeleteFailed);
        }
    }

    private void OnPropertyChanged(string propertyName)
    {
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
