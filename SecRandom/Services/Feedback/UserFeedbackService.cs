using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SecRandom.Services.ImportExport;
using SecRandom.Shared;

namespace SecRandom.Services.Feedback;

public sealed class UserFeedbackService(
    IImportExportService importExportService,
    ISentryFeedbackClient sentryFeedbackClient,
    ILogger<UserFeedbackService> logger) : IUserFeedbackService
{
    public const long MaxDiagnosticArchiveBytes = 19L * 1024 * 1024;

    public async Task<UserFeedbackSubmissionResult> SubmitAsync(
        UserFeedbackSubmission submission,
        CancellationToken cancellationToken = default)
    {
        string? diagnosticArchivePath = null;

        try
        {
            if (submission.Category == UserFeedbackCategory.Bug)
            {
                diagnosticArchivePath = CreateDiagnosticArchivePath();
                await importExportService.ExportDiagnosticAsync(
                    diagnosticArchivePath,
                    includeExtendedData: false,
                    cancellationToken).ConfigureAwait(false);
                if (new FileInfo(diagnosticArchivePath).Length > MaxDiagnosticArchiveBytes)
                    return new UserFeedbackSubmissionResult(
                        false,
                        UserFeedbackSubmissionResult.DiagnosticArchiveTooLarge);
            }

            SentryFeedbackCaptureResult captureResult = await sentryFeedbackClient
                .CaptureAsync(submission, diagnosticArchivePath, cancellationToken)
                .ConfigureAwait(false);
            return captureResult.Succeeded
                ? UserFeedbackSubmissionResult.Success
                : new UserFeedbackSubmissionResult(false, captureResult.FailureReason);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "提交用户反馈失败。 Category={Category}", submission.Category);
            return new UserFeedbackSubmissionResult(false, exception.Message);
        }
        finally
        {
            TryDeleteDiagnosticArchive(diagnosticArchivePath);
        }
    }

    private static string CreateDiagnosticArchivePath()
    {
        string directory = Utils.GetDirectoryPath("feedback");
        return Path.Combine(directory, $"SecRandom_diagnostic_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.zip");
    }

    private static void TryDeleteDiagnosticArchive(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}
