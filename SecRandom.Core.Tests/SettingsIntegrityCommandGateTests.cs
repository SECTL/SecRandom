using SecRandom.Services.Security;

namespace SecRandom.Core.Tests;

public sealed class SettingsIntegrityCommandGateTests
{
    [Fact]
    public void TryRun_WhenConfirmationIsPending_RejectsWithoutRunningCommand()
    {
        var ran = false;

        var allowed = SettingsIntegrityCommandGate.TryRun(
            confirmationPending: true,
            () => ran = true);

        Assert.False(allowed);
        Assert.False(ran);
    }

    [Fact]
    public void TryRun_WhenConfirmationIsNotPending_RunsCommand()
    {
        var ran = false;

        var allowed = SettingsIntegrityCommandGate.TryRun(
            confirmationPending: false,
            () => ran = true);

        Assert.True(allowed);
        Assert.True(ran);
    }
}
