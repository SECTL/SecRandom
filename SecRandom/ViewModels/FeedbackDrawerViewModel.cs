using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Feedback;

namespace SecRandom.ViewModels;

public sealed partial class FeedbackDrawerViewModel(
    MainConfigHandler configHandler,
    IUserFeedbackService feedbackService) : ViewModelBase(configHandler)
{
    [ObservableProperty] private string _actualResult = string.Empty;
    [ObservableProperty] private UserFeedbackCategory _category = UserFeedbackCategory.Bug;
    [ObservableProperty] private string _contactEmail = string.Empty;
    [ObservableProperty] private string _crashReport = string.Empty;
    [ObservableProperty] private string _expectedBehavior = string.Empty;
    [ObservableProperty] private string _featureRequest = string.Empty;
    [ObservableProperty] private bool _isSubmitting;
    [ObservableProperty] private string _motivation = string.Empty;
    [ObservableProperty] private string _reproductionSteps = string.Empty;
    [ObservableProperty] private string _title = string.Empty;

    public bool IsBug => Category == UserFeedbackCategory.Bug;
    public bool IsFeature => Category == UserFeedbackCategory.Feature;

    partial void OnCategoryChanged(UserFeedbackCategory value)
    {
        OnPropertyChanged(nameof(IsBug));
        OnPropertyChanged(nameof(IsFeature));
    }

    public void SelectCategory(UserFeedbackCategory category) => Category = category;

    public UserFeedbackSubmission BuildSubmission()
    {
        UserFeedbackSubmission submission = Category == UserFeedbackCategory.Bug
            ? new UserFeedbackSubmission(
                Category,
                Title,
                ExpectedBehavior,
                ActualResult,
                ReproductionSteps,
                CrashReport: string.IsNullOrWhiteSpace(CrashReport) ? null : CrashReport,
                ContactEmail: ContactEmail)
            : new UserFeedbackSubmission(
                Category,
                Title,
                Motivation,
                FeatureRequest,
                ContactEmail: ContactEmail);
        _ = submission.BuildMessage();
        _ = submission.GetValidatedContactEmail();
        return submission;
    }

    public void LoadDraft(UserFeedbackSubmission submission)
    {
        Clear();
        Category = submission.Category;
        ContactEmail = submission.ContactEmail ?? string.Empty;
        Title = submission.Title;
        CrashReport = submission.CrashReport ?? string.Empty;

        if (submission.Category == UserFeedbackCategory.Bug)
        {
            ExpectedBehavior = submission.PrimaryDetail;
            ActualResult = submission.SecondaryDetail;
            ReproductionSteps = submission.ReproductionSteps ?? string.Empty;
        }
        else
        {
            Motivation = submission.PrimaryDetail;
            FeatureRequest = submission.SecondaryDetail;
        }
    }

    public async Task<UserFeedbackSubmissionResult> SubmitAsync(CancellationToken cancellationToken = default)
    {
        IsSubmitting = true;
        try
        {
            return await feedbackService.SubmitAsync(BuildSubmission(), cancellationToken);
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    public void Clear()
    {
        Category = UserFeedbackCategory.Bug;
        ContactEmail = string.Empty;
        CrashReport = string.Empty;
        Title = string.Empty;
        ExpectedBehavior = string.Empty;
        ActualResult = string.Empty;
        ReproductionSteps = string.Empty;
        Motivation = string.Empty;
        FeatureRequest = string.Empty;
    }
}
