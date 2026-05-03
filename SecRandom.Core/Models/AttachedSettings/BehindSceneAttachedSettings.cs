using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Interfaces;
using SecRandom.Shared.Interfaces;

namespace SecRandom.Core.Models.AttachedSettings;

public partial class BehindSceneAttachedSettings : ObservableRecipient, IAttachedSettings
{
    [ObservableProperty] private bool _isAttachSettingsEnabled = false;
    [ObservableProperty] private double _probability = 1.0;
}