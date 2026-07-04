using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SecRandom.Core;
using SecRandom.Core.Abstraction.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Models.AttachedSettings;

namespace SecRandom.Controls.AttachedSettings;

[AttachedSettingsUsage(AttachedSettingsTargets.Student | AttachedSettingsTargets.Prize)]
[AttachedSettingsControlInfo(GlobalConstants.DrawImageAttachedSettings, "\uE91B")]
public partial class DrawImageAttachedSettingsControl : AttachedSettingsControlBase<DrawImageAttachedSettings>,
    INotifyPropertyChanged
{
    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    public DrawImageAttachedSettingsControl()
    {
        InitializeComponent();
    }

    public string ImagePath
    {
        get => Settings.ImagePath;
        set
        {
            if (Settings.ImagePath == value)
                return;

            Settings.ImagePath = value;
            OnPropertyChanged(nameof(ImagePath));
        }
    }

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private async void BrowseImage_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择展示图片",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("图片文件")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp"]
                },
                FilePickerFileTypes.All
            ]
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            ImagePath = path;
    }

    private void OnPropertyChanged(string? propertyName = null)
    {
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
