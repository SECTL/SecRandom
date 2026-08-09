using System;
using FeedbackResources = SecRandom.Langs.SettingsView.Resources;

namespace SecRandom.Services.Feedback;

public enum UserFeedbackCategory
{
    Bug,
    Feature
}

public sealed record UserFeedbackSubmission(
    UserFeedbackCategory Category,
    string Title,
    string PrimaryDetail,
    string SecondaryDetail,
    string? ReproductionSteps = null,
    string? CrashReport = null,
    string? ContactEmail = null)
{
    public string? GetValidatedContactEmail()
    {
        if (string.IsNullOrWhiteSpace(ContactEmail))
            return null;

        string email = ContactEmail.Trim();
        if (!System.Net.Mail.MailAddress.TryCreate(email, out var address) ||
            !string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Contact email is invalid.", nameof(ContactEmail));

        return address.Address;
    }

    public string BuildMessage()
    {
        string normalizedTitle = Require(Title, nameof(Title));
        string primaryDetail = Require(PrimaryDetail, nameof(PrimaryDetail));
        string secondaryDetail = Require(SecondaryDetail, nameof(SecondaryDetail));

        return Category switch
        {
            UserFeedbackCategory.Bug => BuildBugMessage(normalizedTitle, primaryDetail, secondaryDetail),
            UserFeedbackCategory.Feature => $"# {normalizedTitle}\n\n## {FeedbackResources.C_Feedback_Motivation}\n{primaryDetail}\n\n## {FeedbackResources.C_Feedback_Request}\n{secondaryDetail}",
            _ => throw new ArgumentOutOfRangeException(nameof(Category), Category, "Unsupported feedback category.")
        };
    }

    private string BuildBugMessage(string title, string expectedBehavior, string actualResult)
    {
        string reproduction = Require(ReproductionSteps, nameof(ReproductionSteps));
        string report = string.IsNullOrWhiteSpace(CrashReport)
            ? string.Empty
            : $"\n\n## {FeedbackResources.C_Feedback_CrashReport}\n```text\n{CrashReport}\n```";
        return $"# {title}\n\n## {FeedbackResources.C_Feedback_Expected}\n{expectedBehavior}\n\n## {FeedbackResources.C_Feedback_Actual}\n{actualResult}\n\n## {FeedbackResources.C_Feedback_Reproduce}\n{reproduction}{report}";
    }

    private static string Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Feedback fields cannot be empty.", name);

        return value.Trim();
    }
}

public sealed record UserFeedbackSubmissionResult(bool Succeeded, string? FailureReason = null)
{
    public const string DiagnosticArchiveTooLarge = "diagnostic_archive_too_large";

    public static UserFeedbackSubmissionResult Success { get; } = new(true);
}

public sealed record SentryFeedbackCaptureResult(bool Succeeded, string? FailureReason = null)
{
    public static SentryFeedbackCaptureResult Success { get; } = new(true);
}
