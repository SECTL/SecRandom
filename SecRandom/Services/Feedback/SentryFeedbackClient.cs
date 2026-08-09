using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SecRandom.Services.ImportExport;
using SecRandom.Services.Telemetry;
using Sentry;

namespace SecRandom.Services.Feedback;

public sealed class SentryFeedbackClient(ILogger<SentryFeedbackClient> logger) : ISentryFeedbackClient
{
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(5);

    public async Task<SentryFeedbackCaptureResult> CaptureAsync(
        UserFeedbackSubmission submission,
        string? diagnosticArchivePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var options = new SentryOptions();
            SentryTelemetrySdkAdapter.ConfigureFeedbackOptions(options);

            using var client = new SentryClient(options);
            var scope = new Scope(options);
            scope.SetTag("feedback.category", submission.Category.ToString().ToLowerInvariant());

            var hint = new SentryHint();
            if (!string.IsNullOrWhiteSpace(diagnosticArchivePath))
                hint.AddAttachment(diagnosticArchivePath, AttachmentType.Default, "application/zip");

            UserFeedbackSubmission sanitizedSubmission = string.IsNullOrWhiteSpace(submission.CrashReport)
                ? submission
                : submission with { CrashReport = DiagnosticTextRedactor.Redact(submission.CrashReport) };
            var feedback = new SentryFeedback(
                sanitizedSubmission.BuildMessage(),
                contactEmail: sanitizedSubmission.GetValidatedContactEmail(),
                name: null,
                url: null,
                replayId: null,
                associatedEventId: null);

            client.CaptureFeedback(feedback, out var result, scope, hint);
            if (result != CaptureFeedbackResult.Success)
                return new SentryFeedbackCaptureResult(false, result.ToString());

            await client.FlushAsync(FlushTimeout).WaitAsync(cancellationToken).ConfigureAwait(false);
            return SentryFeedbackCaptureResult.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "发送 Sentry 用户反馈失败。 Category={Category}", submission.Category);
            return new SentryFeedbackCaptureResult(false, exception.Message);
        }
    }
}
