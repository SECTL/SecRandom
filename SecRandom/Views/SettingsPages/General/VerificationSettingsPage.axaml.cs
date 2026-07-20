using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Icons;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Desktop;
using SecRandom.Services.Verification;
using SecRandom.Shared;
using LR = SecRandom.Langs.SettingsPages.General.Verification.Resources;

namespace SecRandom.Views.SettingsPages.General;

[PageInfo("settings.general.verification", FluentIcons.DocumentCheckmarkFilled, "settings.general")]
public partial class VerificationSettingsPage : UserControl
{
    private static readonly int[] RetentionOptions = [7, 15, 30, 60, 90, 0];
    private static readonly long[] StorageOptions = [16L * 1024 * 1024, 32L * 1024 * 1024, 64L * 1024 * 1024, 128L * 1024 * 1024, 256L * 1024 * 1024, 512L * 1024 * 1024, 1024L * 1024 * 1024];
    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();
    private IExternalLauncher ExternalLauncher { get; } = IAppHost.GetService<IExternalLauncher>();
    private bool _verificationModeSelectionReady;
    private bool _restoringVerificationModeSelection;

    public VerificationSettingsPage()
    {
        DataContext = this;
        InitializeComponent();
        Loaded += (_, _) => _verificationModeSelectionReady = true;
    }

    public int SelectedVerificationModeIndex => (int)ConfigHandler.Data.General.Verification.Mode;

    public int SelectedRetentionIndex
    {
        get => Array.IndexOf(RetentionOptions, ConfigHandler.Data.General.ProofRetention.RetentionDays) is var index && index >= 0
            ? index
            : Array.IndexOf(RetentionOptions, 30);
        set
        {
            if (value < 0 || value >= RetentionOptions.Length)
                return;

            ConfigHandler.Data.General.ProofRetention.RetentionDays = RetentionOptions[value];
            ConfigHandler.Save();
        }
    }

    private void OpenProofFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        var directory = Utils.GetDirectoryPath("proofs");
        ExternalLauncher.TryOpenPath(directory);
    }

    private void OpenVerificationWebsite_OnClick(object? sender, RoutedEventArgs e)
    {
        ExternalLauncher.TryOpenUri("https://fair.sectl.cn/");
    }

    public int SelectedStorageIndex
    {
        get => Array.IndexOf(StorageOptions, ConfigHandler.Data.General.ProofRetention.MaximumStorageBytes) is var index && index >= 0
            ? index
            : Array.IndexOf(StorageOptions, 64L * 1024 * 1024);
        set
        {
            if (value < 0 || value >= StorageOptions.Length)
                return;

            ConfigHandler.Data.General.ProofRetention.MaximumStorageBytes = StorageOptions[value];
            ConfigHandler.Save();
        }
    }

    private async void VerificationMode_OnChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_verificationModeSelectionReady || _restoringVerificationModeSelection || sender is not ComboBox { SelectedIndex: >= 0 } selector)
            return;

        var requestedMode = (VerificationMode)selector.SelectedIndex;
        var currentMode = ConfigHandler.Data.General.Verification.Mode;
        if (requestedMode == currentMode)
            return;

        var content = requestedMode == VerificationMode.Ordinary
            ? LR.M_ModeConfirmLocal
            : LR.M_ModeConfirmFormal;
        var acknowledgement = new CheckBox
        {
            Content = LR.C_ModeReadConfirm,
            Margin = new Avalonia.Thickness(0, 12, 0, 0)
        };
        var dialogContent = new StackPanel { Spacing = 4 };
        dialogContent.Children.Add(new TextBlock { Text = content, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        dialogContent.Children.Add(acknowledgement);
        var dialog = new FAContentDialog
        {
            Title = LR.C_ModeConfirmTitle,
            Content = dialogContent,
            PrimaryButtonText = LR.C_ModeConfirmSwitch,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Close,
            IsPrimaryButtonEnabled = false
        };
        acknowledgement.IsCheckedChanged += (_, _) => dialog.IsPrimaryButtonEnabled = acknowledgement.IsChecked == true;
        var result = await dialog.ShowAsync(TopLevel.GetTopLevel(this));

        if (result == FAContentDialogResult.Primary && acknowledgement.IsChecked == true)
        {
            ConfigHandler.Data.General.Verification.Mode = requestedMode;
            ConfigHandler.Save();
        }

        _restoringVerificationModeSelection = true;
        selector.SelectedIndex = (int)ConfigHandler.Data.General.Verification.Mode;
        _restoringVerificationModeSelection = false;
    }
}
