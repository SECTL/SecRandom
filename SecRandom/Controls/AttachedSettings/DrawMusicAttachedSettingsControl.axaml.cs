using System.Collections.ObjectModel;
using System.ComponentModel;
using SecRandom.Core;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.AttachedSettings;
using SecRandom.Services.Music;

namespace SecRandom.Controls.AttachedSettings;

[AttachedSettingsUsage(AttachedSettingsTargets.Student | AttachedSettingsTargets.Prize)]
[AttachedSettingsControlInfo(GlobalConstants.DrawMusicAttachedSettings, FluentIcons.Speaker2Filled)]
public partial class DrawMusicAttachedSettingsControl :
    AttachedSettingsControlBase<DrawMusicAttachedSettings>,
    INotifyPropertyChanged
{
    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    public DrawMusicAttachedSettingsControl()
    {
        MusicLibrary.Refresh();
        InitializeComponent();
    }

    public ObservableCollection<MusicSelection> MusicSelections => MusicLibrary.Selections;

    private MusicLibraryService MusicLibrary { get; } = IAppHost.GetService<MusicLibraryService>();

    public string AnimationMusic
    {
        get => Settings.AnimationMusic;
        set
        {
            if (Settings.AnimationMusic == value)
                return;

            Settings.AnimationMusic = value;
            OnPropertyChanged(nameof(AnimationMusic));
        }
    }

    public string ResultMusic
    {
        get => Settings.ResultMusic;
        set
        {
            if (Settings.ResultMusic == value)
                return;

            Settings.ResultMusic = value;
            OnPropertyChanged(nameof(ResultMusic));
        }
    }

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private void OnPropertyChanged(string propertyName)
    {
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
