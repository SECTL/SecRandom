namespace SecRandom.Core.Tests;

public sealed class CrashRecoveryFeedbackMarkupTests
{
    [Fact]
    public void CrashRecoveryView_UsesTheSharedFeedbackDrawerWithAPrefilledBugDraft()
    {
        string markup = File.ReadAllText(GetRepositoryPath("SecRandom/Views/CrashRecoveryView.axaml"));
        string source = File.ReadAllText(GetRepositoryPath("SecRandom/Views/CrashRecoveryView.axaml.cs"));
        string drawerSource = File.ReadAllText(GetRepositoryPath("SecRandom/Views/FeedbackDrawer.axaml.cs"));
        string viewModelSource = File.ReadAllText(GetRepositoryPath("SecRandom/ViewModels/FeedbackDrawerViewModel.cs"));
        string appSource = File.ReadAllText(GetRepositoryPath("SecRandom/App.axaml.cs"));

        Assert.Contains("C_ReportInApp", markup, StringComparison.Ordinal);
        Assert.Contains("C_ReportGitHub", markup, StringComparison.Ordinal);
        Assert.Contains("DrawerHost", markup, StringComparison.Ordinal);
        Assert.Contains("ReportInApp_OnClick", markup, StringComparison.Ordinal);
        Assert.Contains("drawer.LoadDraft", source, StringComparison.Ordinal);
        Assert.Contains("CrashReport: CrashReport", source, StringComparison.Ordinal);
        Assert.Contains("drawer.DraftCleared", source, StringComparison.Ordinal);
        Assert.Contains("drawer.SubmissionSucceeded", source, StringComparison.Ordinal);
        Assert.Contains("_restartPausedByFeedback", source, StringComparison.Ordinal);
        Assert.Contains("LoadDraft(UserFeedbackSubmission submission)", drawerSource, StringComparison.Ordinal);
        Assert.Contains("SubmissionSucceeded?.Invoke", drawerSource, StringComparison.Ordinal);
        Assert.Contains("CrashReport = submission.CrashReport", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("BuildHost(PlatformStartupContext.Current);", appSource, StringComparison.Ordinal);
        Assert.Contains("services.AddTransient<FeedbackDrawer>();", appSource, StringComparison.Ordinal);
    }

    private static string GetRepositoryPath(string relativePath) => Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../..")),
        relativePath);
}
