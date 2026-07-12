namespace SecRandom.Core.Tests;

using SecurityOperation = global::SecRandom.Core.Enums.Configs.SecurityOperation;
using SecuritySettingsConfig = global::SecRandom.Core.Models.SubConfigs.SecuritySettingsConfig;

public class SecuritySettingsConfigTests
{
    [Fact]
    public void Defaults_DoNotProtectOperationsBeforeCredentialsAreConfigured()
    {
        SecuritySettingsConfig settings = new();

        Assert.False(settings.SecurityEnabled);
        Assert.False(settings.PasswordEnabled);
        Assert.False(settings.TotpEnabled);
        Assert.False(settings.UsbBindingEnabled);
        Assert.False(settings.RequireAllSelectedFactors);
        Assert.False(settings.ProtectOpenSettings);
        Assert.False(settings.ProtectRollCallStart);
        Assert.False(settings.ProtectLotteryReset);
    }

    [Fact]
    public void Defaults_KeepEveryProtectedOperationDisabled()
    {
        SecuritySettingsConfig settings = new();

        Assert.False(settings.ProtectOpenSettings);
        Assert.False(settings.ProtectToggleMainWindow);
        Assert.False(settings.ProtectToggleFloatingWindow);
        Assert.False(settings.ProtectRestart);
        Assert.False(settings.ProtectExit);
        Assert.False(settings.ProtectRollCallStart);
        Assert.False(settings.ProtectRollCallReset);
        Assert.False(settings.ProtectQuickDrawStart);
        Assert.False(settings.ProtectQuickDrawReset);
        Assert.False(settings.ProtectLotteryStart);
        Assert.False(settings.ProtectLotteryReset);
        Assert.False(settings.ProtectLinkage);
    }

    [Fact]
    public void PasswordFactor_CanBeSelectedWithoutEnablingProtectedOperations()
    {
        SecuritySettingsConfig settings = new();

        settings.PasswordEnabled = true;

        Assert.True(settings.PasswordEnabled);
        Assert.False(settings.SecurityEnabled);
        Assert.False(settings.ProtectOpenSettings);
        Assert.False(settings.ProtectToggleMainWindow);
    }

    [Fact]
    public void LegacySensitiveOperationsBridge_UpdatesAllDrawStartProtections()
    {
        SecuritySettingsConfig settings = new();

        settings.VerifyBeforeSensitiveOperations = true;

        Assert.True(settings.ProtectRollCallStart);
        Assert.True(settings.ProtectQuickDrawStart);
        Assert.True(settings.ProtectLotteryStart);
        Assert.True(settings.VerifyBeforeSensitiveOperations);
    }

    [Fact]
    public void LegacyLinkageBridge_MapsToDedicatedProtection()
    {
        SecuritySettingsConfig settings = new();

        settings.VerifyBeforeLinkageOperations = true;

        Assert.True(settings.ProtectLinkage);
        Assert.True(settings.VerifyBeforeLinkageOperations);
    }

    [Fact]
    public void Operations_KeepWindowAndDrawActionsDistinct()
    {
        Assert.NotEqual(SecurityOperation.ToggleMainWindow, SecurityOperation.ToggleFloatingWindow);
        Assert.NotEqual(SecurityOperation.RollCallStart, SecurityOperation.RollCallReset);
        Assert.NotEqual(SecurityOperation.QuickDrawStart, SecurityOperation.QuickDrawReset);
        Assert.NotEqual(SecurityOperation.LotteryStart, SecurityOperation.LotteryReset);
    }
}
