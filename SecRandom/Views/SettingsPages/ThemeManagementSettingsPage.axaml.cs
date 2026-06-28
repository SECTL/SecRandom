using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.ViewModels;
using LR = SecRandom.Langs.SettingsPages.ThemeManagement.Resources;

namespace SecRandom.Views.SettingsPages;

[PageInfo("settings.personalized.theme", FluentIcons.PaintBrushRegular, "settings.personalized")]
public partial class ThemeManagementSettingsPage : UserControl
{
    public ThemeManagementSettingsPage()
    {
        Settings = ViewModel.Config.ThemeManagementSettings;
        DataContext = this;
        InitializeComponent();
        UpdateBackgroundSettingsVisibility();
        Settings.PropertyChanged += SettingsOnPropertyChanged;
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public ThemeManagementSettingsConfig Settings { get; }

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        Settings.PropertyChanged -= SettingsOnPropertyChanged;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null
            || e.PropertyName.EndsWith("BackgroundMode", StringComparison.Ordinal)
            || e.PropertyName.EndsWith("BackgroundBlurEnable", StringComparison.Ordinal))
        {
            UpdateBackgroundSettingsVisibility();
        }

        ConfigHandler.Save();
    }

    private async void SelectMainWindowBackgroundImageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var path = await PickImagePathAsync();
        if (path is not null)
            Settings.MainWindowBackgroundImage = path;
    }

    private async void SelectSettingsWindowBackgroundImageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var path = await PickImagePathAsync();
        if (path is not null)
            Settings.SettingsWindowBackgroundImage = path;
    }

    private async void SelectNotificationFloatingWindowBackgroundImageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var path = await PickImagePathAsync();
        if (path is not null)
            Settings.NotificationFloatingWindowBackgroundImage = path;
    }

    private async System.Threading.Tasks.Task<string?> PickImagePathAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LR.C_SelectImage,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(LR.C_ImageFileType)
                {
                    Patterns = ["*.jpg", "*.jpeg", "*.png", "*.bmp", "*.gif", "*.webp"],
                    MimeTypes = ["image/jpeg", "image/png", "image/bmp", "image/gif", "image/webp"]
                }
            ]
        });

        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private void UpdateBackgroundSettingsVisibility()
    {
        ApplyBackgroundVisibility(
            Settings.MainWindowBackgroundMode,
            Settings.MainWindowBackgroundBlurEnable,
            S_MainWindowBackground_Color,
            S_MainWindowBackground_Gradient,
            S_MainWindowBackground_GradientDirection,
            S_MainWindowBackground_Image,
            S_MainWindowBackground_Brightness,
            S_MainWindowBackground_Blur,
            S_MainWindowBackground_BlurRadius);

        ApplyBackgroundVisibility(
            Settings.SettingsWindowBackgroundMode,
            Settings.SettingsWindowBackgroundBlurEnable,
            S_SettingsWindowBackground_Color,
            S_SettingsWindowBackground_Gradient,
            S_SettingsWindowBackground_GradientDirection,
            S_SettingsWindowBackground_Image,
            S_SettingsWindowBackground_Brightness,
            S_SettingsWindowBackground_Blur,
            S_SettingsWindowBackground_BlurRadius);

        ApplyBackgroundVisibility(
            Settings.NotificationFloatingWindowBackgroundMode,
            Settings.NotificationFloatingWindowBackgroundBlurEnable,
            S_NotificationFloatingWindowBackground_Color,
            S_NotificationFloatingWindowBackground_Gradient,
            S_NotificationFloatingWindowBackground_GradientDirection,
            S_NotificationFloatingWindowBackground_Image,
            S_NotificationFloatingWindowBackground_Brightness,
            S_NotificationFloatingWindowBackground_Blur,
            S_NotificationFloatingWindowBackground_BlurRadius);
    }

    private static void ApplyBackgroundVisibility(
        int mode,
        bool blurEnabled,
        Control color,
        Control gradient,
        Control gradientDirection,
        Control image,
        Control brightness,
        Control blur,
        Control blurRadius)
    {
        var isSolid = mode == 1;
        var isGradient = mode == 2;
        var isImage = mode == 3;

        color.IsVisible = isSolid;
        gradient.IsVisible = isGradient;
        gradientDirection.IsVisible = isGradient;
        image.IsVisible = isImage;
        brightness.IsVisible = isSolid || isGradient || isImage;
        blur.IsVisible = isImage;
        blurRadius.IsVisible = isImage && blurEnabled;
    }
}
