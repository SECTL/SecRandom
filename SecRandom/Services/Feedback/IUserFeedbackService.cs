using System.Threading;
using System.Threading.Tasks;

namespace SecRandom.Services.Feedback;

public interface IUserFeedbackService
{
    Task<UserFeedbackSubmissionResult> SubmitAsync(
        UserFeedbackSubmission submission,
        CancellationToken cancellationToken = default);
}

public interface ISentryFeedbackClient
{
    Task<SentryFeedbackCaptureResult> CaptureAsync(
        UserFeedbackSubmission submission,
        string? diagnosticArchivePath,
        CancellationToken cancellationToken = default);
}
