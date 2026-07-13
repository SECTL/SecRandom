using System;
using System.ComponentModel;
using SecRandom.Core;
using SecRandom.Core.Abstraction.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.AttachedSettings;

namespace SecRandom.Controls.AttachedSettings;

[AttachedSettingsUsage(AttachedSettingsTargets.Student | AttachedSettingsTargets.Prize)]
[AttachedSettingsControlInfo(GlobalConstants.BehindSceneAttachedSettings, FluentIcons.BookNumberFilled)]
public partial class BehindSceneAttachedSettingsControl : AttachedSettingsControlBase<BehindSceneAttachedSettings>,
    INotifyPropertyChanged
{
    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    public BehindSceneAttachedSettingsControl()
    {
        InitializeComponent();
    }

    public double? ProbabilityValue
    {
        get => Settings.Probability;
        set
        {
            var probability = Math.Clamp(value ?? 0, 0, 100);
            if (Math.Abs(Settings.Probability - probability) < double.Epsilon)
                return;

            Settings.Probability = probability;
            OnPropertyChanged(nameof(ProbabilityValue));
        }
    }

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private void OnPropertyChanged(string? propertyName = null)
    {
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
