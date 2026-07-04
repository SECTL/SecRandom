using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Shared.Interfaces;

namespace SecRandom.Core.Models.AttachedSettings;

public partial class DrawImageAttachedSettings : ObservableRecipient, IAttachedSettings
{
    [ObservableProperty] private bool _isAttachSettingsEnabled;
    [ObservableProperty] private string _imagePath = string.Empty;
}
