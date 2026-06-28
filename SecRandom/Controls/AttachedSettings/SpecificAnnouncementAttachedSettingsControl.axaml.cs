using System.ComponentModel;
using SecRandom.Core;
using SecRandom.Core.Abstraction.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Models.AttachedSettings;

namespace SecRandom.Controls.AttachedSettings;

[AttachedSettingsUsage(AttachedSettingsTargets.Student | AttachedSettingsTargets.Prize)]
[AttachedSettingsControlInfo(GlobalConstants.SpecificAnnouncementAttachedSettings, "\uE9F9")]
public partial class SpecificAnnouncementAttachedSettingsControl :
    AttachedSettingsControlBase<SpecificAnnouncementAttachedSettings>,
    INotifyPropertyChanged
{
    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    public SpecificAnnouncementAttachedSettingsControl()
    {
        InitializeComponent();
    }

    public string TtsAlias
    {
        get => Settings.TtsAlias;
        set
        {
            if (Settings.TtsAlias == value)
                return;

            Settings.TtsAlias = value;
            OnPropertyChanged(nameof(TtsAlias));
        }
    }

    public string Prefix
    {
        get => Settings.Prefix;
        set
        {
            if (Settings.Prefix == value)
                return;

            Settings.Prefix = value;
            OnPropertyChanged(nameof(Prefix));
        }
    }

    public string Suffix
    {
        get => Settings.Suffix;
        set
        {
            if (Settings.Suffix == value)
                return;

            Settings.Suffix = value;
            OnPropertyChanged(nameof(Suffix));
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
