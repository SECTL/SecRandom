using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Shared.Interfaces;

namespace SecRandom.Core.Models.AttachedSettings;

public partial class SpecificAnnouncementAttachedSettings : ObservableRecipient, IAttachedSettings
{
    [ObservableProperty] private bool _isAttachSettingsEnabled;
    [ObservableProperty] private string _ttsAlias = string.Empty;
    [ObservableProperty] private string _prefix = string.Empty;
    [ObservableProperty] private string _suffix = string.Empty;
}
