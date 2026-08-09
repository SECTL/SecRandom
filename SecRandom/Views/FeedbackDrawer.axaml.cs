using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Helpers.UI;
using SecRandom.Services.Desktop;
using SecRandom.Services.Feedback;
using SecRandom.ViewModels;

namespace SecRandom.Views;

public partial class FeedbackDrawer : UserControl
{
    private const string GitHubIssuesUrl = "https://github.com/SECTL/SecRandom/issues";
    private Action? _closeDrawer;

    public FeedbackDrawer()
        : this(IAppHost.GetService<FeedbackDrawerViewModel>(), IAppHost.GetService<IExternalLauncher>())
    {
    }

    public FeedbackDrawer(FeedbackDrawerViewModel viewModel, IExternalLauncher externalLauncher)
    {
        ViewModel = viewModel;
        ExternalLauncher = externalLauncher;
        DataContext = this;
        InitializeComponent();
    }

    public FeedbackDrawerViewModel ViewModel { get; }
    public event EventHandler? DraftCleared;
    public event EventHandler? SubmissionSucceeded;
    private IExternalLauncher ExternalLauncher { get; }

    public void Configure(Action closeDrawer)
    {
        _closeDrawer = closeDrawer;
    }

    public void LoadDraft(UserFeedbackSubmission submission)
    {
        ViewModel.LoadDraft(submission);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void BugCategory_OnClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.SelectCategory(UserFeedbackCategory.Bug);
    }

    private void FeatureCategory_OnClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.SelectCategory(UserFeedbackCategory.Feature);
    }

    private void CloseTemporarily_OnClick(object? sender, RoutedEventArgs e)
    {
        _closeDrawer?.Invoke();
    }

    private void CancelFeedback_OnClick(object? sender, RoutedEventArgs e)
    {
        ClearDraft();
        _closeDrawer?.Invoke();
    }

    private void GitHub_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!ExternalLauncher.TryOpenUri(GitHubIssuesUrl))
            this.ShowErrorToast(Langs.SettingsView.Resources.M_Feedback_Failed);
    }

    private async void SubmitFeedback_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var result = await ViewModel.SubmitAsync();
            if (!result.Succeeded)
            {
                string message = result.FailureReason == UserFeedbackSubmissionResult.DiagnosticArchiveTooLarge
                    ? Langs.SettingsView.Resources.M_Feedback_DiagnosticTooLarge
                    : Langs.SettingsView.Resources.M_Feedback_Failed;
                this.ShowErrorToast(message);
                return;
            }

            ClearDraft();
            SubmissionSucceeded?.Invoke(this, EventArgs.Empty);
            _closeDrawer?.Invoke();
            this.ShowSuccessToast(Langs.SettingsView.Resources.M_Feedback_Success);
        }
        catch (ArgumentException exception) when (exception.ParamName == "ContactEmail")
        {
            this.ShowWarningToast(Langs.SettingsView.Resources.M_Feedback_InvalidEmail);
        }
        catch (ArgumentException)
        {
            this.ShowWarningToast(Langs.SettingsView.Resources.M_Feedback_Required);
        }
    }

    private void ClearDraft()
    {
        ViewModel.Clear();
        DraftCleared?.Invoke(this, EventArgs.Empty);
    }
}
