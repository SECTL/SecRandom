using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SecRandom.Core;
using SecRandom.Core.Abstraction.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.AttachedSettings;
using SecRandom.Shared;
using SecRandom.Shared.DataRoot;

namespace SecRandom.Controls.AttachedSettings;

[AttachedSettingsUsage(AttachedSettingsTargets.Student | AttachedSettingsTargets.Prize)]
[AttachedSettingsControlInfo(GlobalConstants.DrawImageAttachedSettings, FluentIcons.GlobeSearchFilled)]
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
        {
            // Data-root-relative whenever the picture lives inside it: moving the data root must not orphan the
            // reference, and an external picture keeps its absolute path untouched.
            ImagePath = DataPathReference.ToStored(path, Utils.DataRoot);
            return;
        }

        var file = files.FirstOrDefault();
        if (file is null)
            return;

        var extension = Path.GetExtension(file.Name).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp"))
            return;

        var managedPath = Path.Combine(Utils.GetDirectoryPath("images"), $"{Guid.NewGuid():N}{extension}");
        await using var source = await file.OpenReadAsync();
        await using var target = File.Create(managedPath);
        await source.CopyToAsync(target);
        ImagePath = DataPathReference.ToStored(managedPath, Utils.DataRoot);
    }

    private void OnPropertyChanged(string? propertyName = null)
    {
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
