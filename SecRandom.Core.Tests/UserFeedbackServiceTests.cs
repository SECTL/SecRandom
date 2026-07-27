using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Services.Archive;
using SecRandom.Services.Feedback;
using SecRandom.Services.ImportExport;

namespace SecRandom.Core.Tests;

public class UserFeedbackServiceTests
{
    [Fact]
    public async Task SubmitAsync_BugExportsStandardDiagnosticAndDeletesTemporaryArchive()
    {
        var exportService = new RecordingImportExportService();
        var sentryClient = new RecordingSentryFeedbackClient();
        var service = CreateService(exportService, sentryClient);

        var result = await service.SubmitAsync(CreateBugSubmission(), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.False(exportService.IncludeExtendedData);
        Assert.NotNull(sentryClient.DiagnosticArchivePath);
        Assert.True(sentryClient.DiagnosticArchiveExistedWhenCaptured);
        Assert.False(File.Exists(sentryClient.DiagnosticArchivePath));
    }

    [Fact]
    public async Task SubmitAsync_FeatureDoesNotCreateDiagnosticArchive()
    {
        var exportService = new RecordingImportExportService();
        var sentryClient = new RecordingSentryFeedbackClient();
        var service = CreateService(exportService, sentryClient);

        var result = await service.SubmitAsync(new UserFeedbackSubmission(
            UserFeedbackCategory.Feature,
            "导出体验优化",
            "需要更快地导出设置。",
            "在设置中增加进度显示。"), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Null(exportService.DiagnosticArchivePath);
        Assert.Null(sentryClient.DiagnosticArchivePath);
    }

    [Fact]
    public async Task SubmitAsync_CaptureFailureStillDeletesTemporaryArchive()
    {
        var exportService = new RecordingImportExportService();
        var sentryClient = new RecordingSentryFeedbackClient(
            new SentryFeedbackCaptureResult(false, "network unavailable"));
        var service = CreateService(exportService, sentryClient);

        var result = await service.SubmitAsync(CreateBugSubmission(), TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.NotNull(exportService.DiagnosticArchivePath);
        Assert.False(File.Exists(exportService.DiagnosticArchivePath));
    }

    [Fact]
    public async Task SubmitAsync_OversizedDiagnosticArchiveSkipsCaptureAndDeletesTemporaryArchive()
    {
        var exportService = new RecordingImportExportService(UserFeedbackService.MaxDiagnosticArchiveBytes + 1);
        var sentryClient = new RecordingSentryFeedbackClient();
        var service = CreateService(exportService, sentryClient);

        UserFeedbackSubmissionResult result = await service.SubmitAsync(
            CreateBugSubmission(),
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(UserFeedbackSubmissionResult.DiagnosticArchiveTooLarge, result.FailureReason);
        Assert.Equal(0, sentryClient.CaptureCount);
        Assert.NotNull(exportService.DiagnosticArchivePath);
        Assert.False(File.Exists(exportService.DiagnosticArchivePath));
    }

    [Fact]
    public void BuildMessage_BugUsesIssueTemplateHeadings()
    {
        string message = CreateBugSubmission().BuildMessage();

        Assert.Contains("## 期望行为", message);
        Assert.Contains("## 实际结果", message);
        Assert.Contains("## 重现步骤", message);
    }

    [Fact]
    public void GetValidatedContactEmail_NormalizesValidEmail()
    {
        UserFeedbackSubmission submission = CreateBugSubmission() with { ContactEmail = "feedback@example.com" };

        Assert.Equal("feedback@example.com", submission.GetValidatedContactEmail());
    }

    [Fact]
    public void GetValidatedContactEmail_RejectsInvalidEmail()
    {
        UserFeedbackSubmission submission = CreateBugSubmission() with { ContactEmail = "not-an-email" };

        Assert.Throws<ArgumentException>(() => submission.GetValidatedContactEmail());
    }

    private static UserFeedbackService CreateService(
        RecordingImportExportService exportService,
        RecordingSentryFeedbackClient sentryClient)
    {
        return new UserFeedbackService(exportService, sentryClient, NullLogger<UserFeedbackService>.Instance);
    }

    private static UserFeedbackSubmission CreateBugSubmission()
    {
        return new UserFeedbackSubmission(
            UserFeedbackCategory.Bug,
            "抽取结果窗口没有显示",
            "抽取后显示结果窗口。",
            "抽取完成后窗口没有出现。",
            "1. 打开点名。\n2. 点击抽取。\n3. 等待结果。");
    }

    private sealed class RecordingSentryFeedbackClient(SentryFeedbackCaptureResult? captureResult = null)
        : ISentryFeedbackClient
    {
        public int CaptureCount { get; private set; }
        public string? DiagnosticArchivePath { get; private set; }
        public bool DiagnosticArchiveExistedWhenCaptured { get; private set; }

        public Task<SentryFeedbackCaptureResult> CaptureAsync(
            UserFeedbackSubmission submission,
            string? diagnosticArchivePath,
            CancellationToken cancellationToken = default)
        {
            CaptureCount++;
            DiagnosticArchivePath = diagnosticArchivePath;
            DiagnosticArchiveExistedWhenCaptured = diagnosticArchivePath is not null && File.Exists(diagnosticArchivePath);
            return Task.FromResult(captureResult ?? SentryFeedbackCaptureResult.Success);
        }
    }

    private sealed class RecordingImportExportService(long diagnosticArchiveBytes = 0) : IImportExportService
    {
        public string? DiagnosticArchivePath { get; private set; }
        public bool IncludeExtendedData { get; private set; }

        public Task<string> ExportDiagnosticAsync(
            string destinationPath,
            bool includeExtendedData = false,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            using (FileStream archive = File.Create(destinationPath))
            {
                if (diagnosticArchiveBytes > 0)
                    archive.SetLength(diagnosticArchiveBytes);
                else
                    archive.Write("diagnostic"u8);
            }
            DiagnosticArchivePath = destinationPath;
            IncludeExtendedData = includeExtendedData;
            return Task.FromResult(destinationPath);
        }

        public Task<string> ExportSettingsAsync(string destinationPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> ExportAllDataAsync(string destinationPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ImportInspection> InspectSettingsAsync(string sourcePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ImportInspection> InspectAllDataAsync(string sourcePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ImportResult> ImportSettingsAsync(string sourcePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ImportResult> ImportAllDataAsync(string sourcePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public string CreateManualBackup(IReadOnlyCollection<string> roots) => throw new NotSupportedException();
        public string CreateAutomaticBackup(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ImportResult> RestoreBackupAsync(string sourcePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
