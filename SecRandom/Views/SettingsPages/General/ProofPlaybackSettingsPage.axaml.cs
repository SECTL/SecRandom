using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Services.Desktop;
using SecRandom.Services.Verification;
using SecRandom.Shared.Models.Verification;
using SecRandom.ViewModels.MainPages;
using SecRandom.Views;
using SecRandom.Views.MainPages;
using LR = SecRandom.Langs.SettingsPages.General.Verification.Resources;
using PR = SecRandom.Langs.SettingsPages.Picking.Resources;

namespace SecRandom.Views.SettingsPages.General;

[PageInfo("settings.general.proofPlayback", FluentIcons.VideoClipFilled, "settings.general", isHide: true)]
public partial class ProofPlaybackSettingsPage : UserControl, INotifyPropertyChanged
{
    private const string MainWebsiteUrl = "https://secrandom.sectl.cn";
    private readonly ProofPlaybackService _playbackService = IAppHost.GetService<ProofPlaybackService>();
    private IExternalLauncher ExternalLauncher { get; } = IAppHost.GetService<IExternalLauncher>();
    private ProofPlaybackDateGroup? _selectedProofDateGroup;
    private ProofPlaybackTableRow? _selectedProofRow;
    private bool _isPlaying;
    private CancellationTokenSource? _refreshCancellation;

    public ProofPlaybackSettingsPage()
    {
        DataContext = this;
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ProofPlaybackDateGroup> ProofDateGroups { get; } = [];
    public ObservableCollection<ProofPlaybackTableRow> Rows { get; } = [];

    public ProofPlaybackDateGroup? SelectedProofDateGroup
    {
        get => _selectedProofDateGroup;
        set
        {
            if (ReferenceEquals(_selectedProofDateGroup, value))
                return;

            _selectedProofDateGroup = value;
            Rows.Clear();
            if (value is not null)
            {
                foreach (var document in value.Documents)
                    Rows.Add(new ProofPlaybackTableRow(value.Date, document));
            }

            SelectedProofRow = Rows.FirstOrDefault();
            OnPropertyChanged();
        }
    }

    public ProofPlaybackTableRow? SelectedProofRow
    {
        get => _selectedProofRow;
        set
        {
            if (ReferenceEquals(_selectedProofRow, value))
                return;

            _selectedProofRow = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanStartProofPlayback));
        }
    }

    public bool CanStartProofPlayback => !_isPlaying && SelectedProofRow is not null;

    private void OpenVerificationWebsite_OnClick(object? sender, RoutedEventArgs e) =>
        ExternalLauncher.TryOpenUri(MainWebsiteUrl);

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        await RefreshProofPlaybackFilesAsync();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e) => _refreshCancellation?.Cancel();

    private async Task RefreshProofPlaybackFilesAsync()
    {
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = new CancellationTokenSource();
        var cancellationToken = _refreshCancellation.Token;

        ProofDateGroups.Clear();
        Rows.Clear();
        _selectedProofDateGroup = null;
        _selectedProofRow = null;
        OnPropertyChanged(nameof(SelectedProofDateGroup));
        OnPropertyChanged(nameof(SelectedProofRow));
        OnPropertyChanged(nameof(CanStartProofPlayback));

        try
        {
            var groups = await _playbackService.LoadDateGroupsAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                return;

            foreach (var group in groups)
                ProofDateGroups.Add(group);

            SelectedProofDateGroup = ProofDateGroups.FirstOrDefault();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async void StartProofPlayback_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedProofRow?.Document is not { } summary || _isPlaying)
            return;

        _isPlaying = true;
        OnPropertyChanged(nameof(CanStartProofPlayback));
        using var cancellation = new CancellationTokenSource();
        IDisposable? displayScope = null;
        try
        {
            var document = await Task.Run(() => _playbackService.TryLoad(summary.Path), cancellation.Token);
            if (document is not { IsLocallyVerified: true })
            {
                await new FAContentDialog
                {
                    Title = LR.S_ProofPlayback,
                    Content = LR.M_ProofPlaybackInvalid,
                    CloseButtonText = SecRandom.Langs.Common.Resources.C_Close
                }.ShowAsync(TopLevel.GetTopLevel(this));
                return;
            }

            using var scope = App.EnterProofPlayback(document.MainPageId);
            try
            {
                await App.ShowMainWindowForRemoteDrawAsync(document.MainPageId);
                App.ActivateProofPlayback(document.MainPageId);

                if (document.MainPageId == "main.lottery"
                    && MainView.Current?.ActivePage is LotteryPage lotteryPage)
                {
                    displayScope = lotteryPage.ViewModel.BeginProofPlayback(
                        document.RosterName,
                        document.Candidates.Count,
                        document.Results.Count);
                    await lotteryPage.ViewModel.PlayProofPlaybackAsync(
                        document.Candidates.Select(candidate => candidate.Text).ToArray(),
                        document.Results.Select(result => result.Text).ToArray(),
                        cancellation.Token);
                }
                else if (MainView.Current?.ActivePage is RollCallPage rollCallPage)
                {
                    displayScope = rollCallPage.ViewModel.BeginProofPlayback(
                        document.RosterName,
                        document.Candidates.Count,
                        document.Results.Count,
                        document.RosterEntryCount);
                    await rollCallPage.ViewModel.PlayProofPlaybackAsync(
                        document.Candidates.Select(candidate => candidate.Text).ToArray(),
                        document.Results.Select(result => result.Text).ToArray(),
                        cancellation.Token);
                }

                // Keep the completed result on screen before restoring the previous application state.
                await Task.Delay(TimeSpan.FromSeconds(3), cancellation.Token);
            }
            finally
            {
                displayScope?.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _isPlaying = false;
            OnPropertyChanged(nameof(CanStartProofPlayback));
        }
    }

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record ProofPlaybackTableRow(DateOnly Date, ProofPlaybackSummary Document)
{
    public string DateText => Date.ToString("yyyy-MM-dd");
    public string TimeText => Document.CreatedAtUtc.ToLocalTime().ToString("HH:mm:ss");
    public string KindLabel => Document.Kind switch
    {
        VerificationDrawKind.Student => LR.O_ProofPlaybackStudent,
        VerificationDrawKind.Prize => LR.O_ProofPlaybackPrize,
        VerificationDrawKind.Number => LR.O_ProofPlaybackNumber,
        _ => LR.O_ProofPlaybackDraw
    };
    public string AlgorithmLabel => GetAlgorithmLabel(Document.AlgorithmProfile, Document.AlgorithmId);
    public int CandidateCount => Document.CandidateCount;
    public int ResultCount => Document.ResultCount;
    public string ProofIdText => Document.ProofId.ToString("N");

    private static string GetAlgorithmLabel(string profile, string algorithmId)
    {
        var value = string.IsNullOrWhiteSpace(profile) ? algorithmId : profile;
        var comparison = StringComparison.OrdinalIgnoreCase;
        if (value.Contains("GroupUniform", comparison)
            || algorithmId.Contains("group-uniform", comparison)
            || algorithmId.Contains("student-group-uniform", comparison))
            return LR.C_ProofPlaybackGroupAlgorithm;
        if (value.Contains("Orchestrated", comparison) || algorithmId.Contains("orchestrated", comparison))
            return PR.O_Orchestrated;
        if (value.Contains("Number", comparison) || algorithmId.Contains("number", comparison))
            return LR.C_ProofPlaybackNumberAlgorithm;
        if (value.Contains("Inventory", comparison) || value.Contains("CountInternalRule", comparison)
            || algorithmId.Contains("inventory", comparison))
            return PR.O_AlgorithmInventory;
        if (value.Contains("LotteryPan", comparison) || algorithmId.Contains("lottery-weighted", comparison))
            return AddRepeatLabel(PR.O_AlgorithmWeighted, value, comparison);
        if (value.Contains("Fair", comparison))
            return AddRepeatLabel(PR.O_AlgorithmFair, value, comparison);
        if (value.Contains("Random", comparison))
            return AddRepeatLabel(PR.O_AlgorithmRandom, value, comparison);

        return LR.C_ProofPlaybackUnknownAlgorithm;
    }

    private static string AddRepeatLabel(string algorithm, string profile, StringComparison comparison)
    {
        var repeat = profile.Contains("NoRepeat", comparison)
            ? PR.O_NoRepeat
            : profile.Contains("HalfRepeat", comparison)
                ? PR.O_HalfRepeat
                : profile.Contains("Repeat", comparison)
                    ? PR.O_Repeat
                    : string.Empty;
        return string.IsNullOrEmpty(repeat) ? algorithm : $"{algorithm} - {repeat}";
    }
}
