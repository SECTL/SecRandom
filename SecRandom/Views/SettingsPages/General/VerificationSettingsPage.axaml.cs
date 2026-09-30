using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
    private DrawProofAttestationService AttestationService { get; } = IAppHost.GetService<DrawProofAttestationService>();
    private ProofIntegrityVerifier IntegrityVerifier { get; } = IAppHost.GetService<ProofIntegrityVerifier>();
    private IExternalLauncher ExternalLauncher { get; } = IAppHost.GetService<IExternalLauncher>();
    private bool _verificationModeSelectionReady;
    private bool _restoringVerificationModeSelection;

    public VerificationSettingsPage()
    {
        DataContext = this;
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _verificationModeSelectionReady = true;
        AttestationService.StatusChanged += AttestationStatus_OnChanged;
        RefreshProofQueueStatus();
        if (string.IsNullOrEmpty(ProofIntegrityStatusText.Text))
            ProofIntegrityStatusText.Text = LR.M_ProofIntegrityIdle;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        AttestationService.StatusChanged -= AttestationStatus_OnChanged;
    }

    private void AttestationStatus_OnChanged(object? sender, EventArgs e)
    {
        // Submission runs on a background worker, so the refresh must be marshaled to the UI thread.
        if (Dispatcher.UIThread.CheckAccess())
            RefreshProofQueueStatus();
        else
            Dispatcher.UIThread.Post(RefreshProofQueueStatus);
    }

    private void RefreshProofQueueStatus() => ApplyProofQueueStatus(AttestationService.GetStatus());

    private void ApplyProofQueueStatus(ProofAttestationStatus status)
    {
        List<string> parts = [];
        if (status.PendingCount > 0)
            parts.Add(string.Format(CultureInfo.CurrentCulture, LR.M_ProofQueuePending, status.PendingCount));
        if (status.ReceiptFailedCount > 0)
            parts.Add(string.Format(CultureInfo.CurrentCulture, LR.M_ProofQueueFailed, status.ReceiptFailedCount));
        if (status.TimestampFailedCount > 0)
            parts.Add(string.Format(CultureInfo.CurrentCulture, LR.M_ProofQueueTimestampFailed, status.TimestampFailedCount));
        if (status.ChainAlertCount > 0)
            parts.Add(string.Format(CultureInfo.CurrentCulture, LR.M_ProofQueueChainAlert, status.ChainAlertCount));
        if (status.ConflictCount > 0)
            parts.Add(string.Format(CultureInfo.CurrentCulture, LR.M_ProofQueueConflict, status.ConflictCount));

        ProofQueueStatusText.Text = parts.Count == 0 ? LR.M_ProofQueueIdle : string.Join(" · ", parts);
        RetryProofQueueButton.IsEnabled = status.HasOutstandingWork;
    }

    private async void RetryProofQueue_OnClick(object? sender, RoutedEventArgs e)
    {
        RetryProofQueueButton.IsEnabled = false;
        try
        {
            ApplyProofQueueStatus(await AttestationService.RetryNowAsync());
        }
        finally
        {
            RetryProofQueueButton.IsEnabled = AttestationService.GetStatus().HasOutstandingWork;
        }
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

    private void VerifyProofs_OnClick(object? sender, RoutedEventArgs e)
    {
        VerifyProofsButton.IsEnabled = false;
        try
        {
            ProofIntegrityStatusText.Text = FormatIntegrityReport(IntegrityVerifier.Verify());
        }
        finally
        {
            VerifyProofsButton.IsEnabled = true;
        }
    }

    private static string FormatIntegrityReport(ProofIntegrityReport report)
    {
        var counts = string.Format(
            CultureInfo.CurrentCulture, LR.M_ProofIntegrityCounts, report.Total, report.Chained, report.Unchained);
        List<string> lines = [counts, LR.M_ProofIntegrityLimits];
        if (report.IsHealthy && report.MissingTail == 0)
        {
            lines.Add(LR.M_ProofIntegrityHealthy);
            return string.Join(Environment.NewLine, lines);
        }

        lines.Add(string.Format(
            CultureInfo.CurrentCulture,
            LR.M_ProofIntegrityProblems,
            report.Gaps,
            report.BrokenLinks,
            report.Modified,
            report.BeyondHead,
            report.MissingTail,
            report.Expired));
        lines.AddRange(report.Issues
            .Take(5)
            .Select(issue => $"{IssueLabel(issue.Kind)}：{Path.GetFileName(issue.Path)}"));
        return string.Join(Environment.NewLine, lines);
    }

    private static string IssueLabel(ProofIntegrityIssueKind kind) => kind switch
    {
        ProofIntegrityIssueKind.Unreadable => LR.M_ProofIssue_Unreadable,
        ProofIntegrityIssueKind.Modified => LR.M_ProofIssue_Modified,
        ProofIntegrityIssueKind.UnsupportedVersion => LR.M_ProofIssue_UnsupportedVersion,
        ProofIntegrityIssueKind.BrokenLink => LR.M_ProofIssue_BrokenLink,
        ProofIntegrityIssueKind.Gap => LR.M_ProofIssue_Gap,
        ProofIntegrityIssueKind.BeyondHead => LR.M_ProofIssue_BeyondHead,
        _ => LR.M_ProofIssue_MissingTail
    };

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
