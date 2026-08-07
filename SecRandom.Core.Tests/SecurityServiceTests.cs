using Avalonia.Controls;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Security;
using System.Reflection;
using System.Security.Cryptography;

namespace SecRandom.Core.Tests;

public sealed class SecurityServiceTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(Path.GetTempPath(), "SecRandom", "security-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SecurityCredentialStore_WhenOpenedByAnotherHost_UnlocksWithTheSamePassword()
    {
        var path = Path.Combine(_temporaryRoot, "security", "credentials.json");
        var firstHost = new SecurityCredentialStore(path, CredentialKdfParameters.Test);
        using (var context = firstHost.Create("secret1"))
        {
            context.Credentials.TotpSecret = "JBSWY3DPEHPK3PXP";
            firstHost.Save(context);
        }

        var secondHost = new SecurityCredentialStore(path, CredentialKdfParameters.Test);
        var result = secondHost.TryUnlock("secret1", out var unlocked);

        Assert.Equal(CredentialUnlockResult.Succeeded, result);
        using (unlocked)
            Assert.Equal("JBSWY3DPEHPK3PXP", unlocked!.Credentials.TotpSecret);
    }

    [Fact]
    public void SecurityCredentialStore_WhenOnlyLegacyFileExists_DoesNotReadIt()
    {
        var path = Path.Combine(_temporaryRoot, "security", "credentials.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, "credentials.v1.json"), "{\"Version\":1}");

        var metadata = new SecurityCredentialStore(path, CredentialKdfParameters.Test).LoadMetadata();

        Assert.Null(metadata.Password);
        Assert.True(metadata.IsReadable);
    }

    [Fact]
    public void SecurityCredentialStore_WhenAuthenticatedMetadataIsTampered_RejectsUnlock()
    {
        var path = Path.Combine(_temporaryRoot, "security", "credentials.json");
        var store = new SecurityCredentialStore(path, CredentialKdfParameters.Test);
        using (var context = store.Create("secret1"))
        {
            context.Credentials.TotpSecret = "JBSWY3DPEHPK3PXP";
            store.Save(context);
        }

        var contents = File.ReadAllText(path).Replace("\"HasTotp\": true", "\"HasTotp\": false", StringComparison.Ordinal);
        File.WriteAllText(path, contents);

        var result = store.TryUnlock("secret1", out var contextAfterTamper);

        Assert.Equal(CredentialUnlockResult.Unavailable, result);
        Assert.Null(contextAfterTamper);
    }

    [Fact]
    public void SecurityCredentialStore_WhenPasswordChanges_ReencryptsExistingSecrets()
    {
        var path = Path.Combine(_temporaryRoot, "security", "credentials.json");
        var store = new SecurityCredentialStore(path, CredentialKdfParameters.Test);
        using (var context = store.Create("secret1"))
        {
            context.Credentials.TotpSecret = "JBSWY3DPEHPK3PXP";
            store.Save(context);
        }

        Assert.Equal(CredentialUnlockResult.Succeeded, store.TryUnlock("secret1", out var changeContext));
        using (var context = Assert.IsType<SecurityCredentialContext>(changeContext))
        {
            store.Rekey(context, "newsecret");
            store.Save(context);
        }

        Assert.Equal(CredentialUnlockResult.InvalidPassword, store.TryUnlock("secret1", out _));
        Assert.Equal(CredentialUnlockResult.Succeeded, store.TryUnlock("newsecret", out var unlocked));
        using (unlocked)
            Assert.Equal("JBSWY3DPEHPK3PXP", unlocked!.Credentials.TotpSecret);
    }

    [Fact]
    public async Task SetPasswordAsync_WhenCredentialSaveFails_ReturnsFalseWithoutEnablingPassword()
    {
        var writeFault = new ThrowOnNthCredentialWrite { ThrowOnWrite = 1 };
        var fixture = CreateFixture(Password("secret1"), writeFault);

        var saved = await fixture.Service.SetPasswordAsync("secret1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(saved);
        Assert.False(fixture.Service.GetUiState().HasPassword);
        Assert.False(fixture.ConfigHandler.Data.SecuritySettings.PasswordEnabled);
    }

    [Fact]
    public async Task UpdateSecuritySettingsAsync_WhenPasswordIsAccepted_EnablesProtectionAndSaves()
    {
        var fixture = CreateFixture(Password("secret1"));
        await fixture.Service.SetPasswordAsync("secret1", cancellationToken: TestContext.Current.CancellationToken);
        fixture.ConfigService.ResetSaveCount();

        var updated = await fixture.Service.UpdateSecuritySettingsAsync(
            null!,
            () => fixture.ConfigHandler.Data.SecuritySettings.SecurityEnabled = true,
            TestContext.Current.CancellationToken);

        Assert.True(updated);
        Assert.True(fixture.ConfigHandler.Data.SecuritySettings.SecurityEnabled);
        Assert.Equal(1, fixture.ConfigService.SaveCount);
        Assert.Equal([SecurityFactor.Password], Assert.Single(fixture.Prompt.Requests).RequiredFactors);
    }

    [Fact]
    public async Task UpdateSecuritySettingsAsync_WhenPasswordIsRejected_DoesNotChangeOrSaveProtection()
    {
        var fixture = CreateFixture(Password("wrong"));
        await fixture.Service.SetPasswordAsync("secret1", cancellationToken: TestContext.Current.CancellationToken);
        fixture.ConfigService.ResetSaveCount();

        var updated = await fixture.Service.UpdateSecuritySettingsAsync(
            null!,
            () => fixture.ConfigHandler.Data.SecuritySettings.SecurityEnabled = true,
            TestContext.Current.CancellationToken);

        Assert.False(updated);
        Assert.False(fixture.ConfigHandler.Data.SecuritySettings.SecurityEnabled);
        Assert.Equal(0, fixture.ConfigService.SaveCount);
    }

    [Fact]
    public async Task TryUpdateSettings_WhenSecurityIsEnabled_RejectsUnverifiedMutation()
    {
        var fixture = CreateFixture(Password("secret1"));
        await fixture.Service.SetPasswordAsync("secret1", cancellationToken: TestContext.Current.CancellationToken);
        fixture.ConfigHandler.Data.SecuritySettings.SecurityEnabled = true;
        fixture.ConfigService.ResetSaveCount();

        var updated = fixture.Service.TryUpdateSettings(() =>
            fixture.ConfigHandler.Data.SecuritySettings.ProtectExit = true);

        Assert.False(updated);
        Assert.False(fixture.ConfigHandler.Data.SecuritySettings.ProtectExit);
        Assert.Equal(0, fixture.ConfigService.SaveCount);
        Assert.Empty(fixture.Prompt.Requests);
    }

    [Fact]
    public async Task TryUpdateSettings_WhenLegacyMutationEnablesSecurity_RejectsAndRestoresState()
    {
        var fixture = CreateFixture(Password("secret1"));
        await fixture.Service.SetPasswordAsync("secret1", cancellationToken: TestContext.Current.CancellationToken);
        fixture.ConfigService.ResetSaveCount();

        var updated = fixture.Service.TryUpdateSettings(() =>
        {
            fixture.ConfigHandler.Data.SecuritySettings.SecurityEnabled = true;
            fixture.ConfigHandler.Data.SecuritySettings.ProtectExit = true;
        });

        Assert.False(updated);
        Assert.False(fixture.ConfigHandler.Data.SecuritySettings.SecurityEnabled);
        Assert.False(fixture.ConfigHandler.Data.SecuritySettings.ProtectExit);
        Assert.Equal(0, fixture.ConfigService.SaveCount);
        Assert.Empty(fixture.Prompt.Requests);
    }

    [Fact]
    public async Task BeginTotpSetupAsync_WhenPasswordIsRejected_DoesNotRevealANewSecret()
    {
        var fixture = CreateFixture(Password("wrong"));
        await fixture.Service.SetPasswordAsync("secret1", cancellationToken: TestContext.Current.CancellationToken);

        var secret = await fixture.Service.BeginTotpSetupAsync(null!, TestContext.Current.CancellationToken);

        Assert.Null(secret);
        Assert.Equal([SecurityFactor.Password], Assert.Single(fixture.Prompt.Requests).RequiredFactors);
    }

    [Fact]
    public async Task RemovePasswordAsync_WhenPasswordManagementIsLocked_RejectsTheRemoval()
    {
        var fixture = CreateFixture(Password("secret1"));
        await fixture.Service.SetPasswordAsync("secret1", cancellationToken: TestContext.Current.CancellationToken);

        for (var attempt = 0; attempt < 5; attempt++)
            Assert.False(await fixture.Service.SetPasswordAsync("newsecret", "wrong", TestContext.Current.CancellationToken));

        var removed = await fixture.Service.RemovePasswordAsync("secret1", TestContext.Current.CancellationToken);

        Assert.False(removed);
        Assert.True(fixture.Service.GetUiState().HasPassword);
        Assert.NotNull(fixture.Service.GetUiState().LockoutRemaining);
    }

    [Fact]
    public async Task RemovePasswordAsync_WhenUsbIsBound_DeletesTheBindingKey()
    {
        var usbRoot = CreateUsbDirectory("remove-password");
        var fixture = CreateFixture(
            Password("secret1"),
            new UsbDriveInfo("H:", "Remove password USB", "volume:remove-password", usbRoot));
        await fixture.Service.SetPasswordAsync("secret1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await fixture.Service.BindUsbAsync(null!, "volume:remove-password", TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Combine(usbRoot, ".SecRandom.safety.key")));

        var removed = await fixture.Service.RemovePasswordAsync("secret1", TestContext.Current.CancellationToken);

        Assert.True(removed);
        Assert.False(File.Exists(Path.Combine(usbRoot, ".SecRandom.safety.key")));
    }

    [Fact]
    public async Task CancelTotpSetupAsync_WhenConfirmationIsCancelled_CannotActivateThePendingSecret()
    {
        var fixture = CreateFixture(Password("secret1"));
        await fixture.Service.SetPasswordAsync("secret1", cancellationToken: TestContext.Current.CancellationToken);
        var secret = await fixture.Service.BeginTotpSetupAsync(null!, TestContext.Current.CancellationToken);
        Assert.NotNull(secret);

        await fixture.Service.CancelTotpSetupAsync(secret, TestContext.Current.CancellationToken);
        var saved = await fixture.Service.ConfirmTotpAsync(secret, CreateTotpCode(secret), TestContext.Current.CancellationToken);

        Assert.False(saved);
        Assert.False(fixture.Service.GetUiState().HasTotp);
    }

    [Fact]
    public async Task ConfirmTotpAsync_WhenCredentialSaveFails_ReturnsFalseAndKeepsThePendingSetup()
    {
        var writeFault = new ThrowOnNthCredentialWrite();
        var fixture = CreateFixture(Password("secret1"), writeFault);
        await fixture.Service.SetPasswordAsync("secret1", cancellationToken: TestContext.Current.CancellationToken);
        var secret = await fixture.Service.BeginTotpSetupAsync(null!, TestContext.Current.CancellationToken);
        Assert.NotNull(secret);
        writeFault.ThrowOnWrite = writeFault.WriteCalls + 1;

        var saved = await fixture.Service.ConfirmTotpAsync(secret, CreateTotpCode(secret), TestContext.Current.CancellationToken);
        var retried = await fixture.Service.ConfirmTotpAsync(secret, CreateTotpCode(secret), TestContext.Current.CancellationToken);

        Assert.False(saved);
        Assert.True(retried);
    }

    [Fact]
    public async Task VerifyAsync_WhenCredentialSaveFails_RejectsAuthorization()
    {
        var writeFault = new ThrowOnNthCredentialWrite();
        var fixture = CreateFixture(Password("secret1"), writeFault);
        await fixture.Service.SetPasswordAsync("secret1", cancellationToken: TestContext.Current.CancellationToken);
        writeFault.ThrowOnWrite = writeFault.WriteCalls + 1;

        var result = await fixture.Service.VerifyAsync(Password("secret1"), TestContext.Current.CancellationToken);

        Assert.False(result.IsAuthorized);
        Assert.Equal(SecurityVerificationFailure.FactorUnavailable, result.Failure);
    }

    [Fact]
    public async Task VerifyAsync_WhenAnySelectedFactorModeHasBoundUsb_AuthorizesWithoutPassword()
    {
        var usbRoot = CreateUsbDirectory("usb-any-factor");
        var fixture = CreateFixture(
            Password("secret1"),
            new UsbDriveInfo("P:", "Authorization USB", "volume:usb-any", usbRoot));
        await fixture.Service.SetPasswordAsync("secret1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await fixture.Service.BindUsbAsync(null!, "volume:usb-any", TestContext.Current.CancellationToken));
        fixture.ConfigHandler.Data.SecuritySettings.SecurityEnabled = true;
        fixture.ConfigHandler.Data.SecuritySettings.UsbBindingEnabled = true;
        fixture.ConfigHandler.Data.SecuritySettings.RequireAllSelectedFactors = false;

        var result = await fixture.Service.VerifyAsync(
            new SecurityVerificationResponse(string.Empty, string.Empty, UsbPresent: true),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsAuthorized);
    }

    [Fact]
    public async Task VerifyAsync_WhenAllSelectedFactorModeHasOnlyUsb_RejectsAuthorization()
    {
        var usbRoot = CreateUsbDirectory("usb-all-factors");
        var fixture = CreateFixture(
            Password("secret1"),
            new UsbDriveInfo("Q:", "Authorization USB", "volume:usb-all", usbRoot));
        await fixture.Service.SetPasswordAsync("secret1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await fixture.Service.BindUsbAsync(null!, "volume:usb-all", TestContext.Current.CancellationToken));
        fixture.ConfigHandler.Data.SecuritySettings.SecurityEnabled = true;
        fixture.ConfigHandler.Data.SecuritySettings.UsbBindingEnabled = true;
        fixture.ConfigHandler.Data.SecuritySettings.RequireAllSelectedFactors = true;

        var result = await fixture.Service.VerifyAsync(
            new SecurityVerificationResponse(string.Empty, string.Empty, UsbPresent: true),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsAuthorized);
        Assert.Equal(SecurityVerificationFailure.InvalidCredentials, result.Failure);
    }

    [Fact]
    public void SecurityVerificationEligibility_RequiresAnyOrAllSelectedFactorInput()
    {
        var anyFactorRequest = new SecurityVerificationRequest(
            [SecurityFactor.Password, SecurityFactor.Totp, SecurityFactor.Usb],
            RequireAllSelectedFactors: false,
            LockoutRemaining: null);
        var allFactorsRequest = anyFactorRequest with { RequireAllSelectedFactors = true };

        Assert.True(SecurityVerificationEligibility.CanSubmit(anyFactorRequest, string.Empty, string.Empty, usbPresent: true));
        Assert.False(SecurityVerificationEligibility.CanSubmit(allFactorsRequest, string.Empty, string.Empty, usbPresent: true));
        Assert.True(SecurityVerificationEligibility.CanSubmit(allFactorsRequest, "secret1", "123456", usbPresent: true));
        Assert.False(SecurityVerificationEligibility.CanSubmit(
            allFactorsRequest with { LockoutRemaining = TimeSpan.FromSeconds(1) }, "secret1", "123456", usbPresent: true));
    }

    [Fact]
    public async Task GetUsbDevicesAsync_ProjectsBoundAndUnboundRemovableDevices()
    {
        var firstRoot = CreateUsbDirectory("first");
        var secondRoot = CreateUsbDirectory("second");
        var fixture = CreateFixture(
            Password("secret1"),
            new UsbDriveInfo("E:", "Class USB", "volume:E", firstRoot)
            {
                HardwareName = "UFSD-I"
            },
            new UsbDriveInfo("F:", "Backup USB", "volume:F", secondRoot));
        await fixture.Service.SetPasswordAsync("secret1", cancellationToken: TestContext.Current.CancellationToken);

        var bound = await fixture.Service.BindUsbAsync(null!, "volume:E", TestContext.Current.CancellationToken);
        var devices = await fixture.Service.GetUsbDevicesAsync(TestContext.Current.CancellationToken);

        Assert.True(bound);
        Assert.Contains(devices, device => device.DriveLetter == "E:" && device.DisplayName == "Class USB" &&
                                          device.DeviceId == "volume:E" && device.IsBound);
        Assert.Contains(devices, device => device.DriveLetter == "F:" && device.DisplayName == "Backup USB" &&
                                           device.DeviceId == "volume:F" && !device.IsBound);
        Assert.Equal("UFSD-I", Assert.Single(devices, device => device.DriveLetter == "E:").HardwareName);
    }

    [Fact]
    public async Task GetUsbDevicesAsync_WhenBoundVolumeMoves_RecognizesItByDeviceId()
    {
        var originalRoot = CreateUsbDirectory("volume-original");
        var movedRoot = Path.Combine(_temporaryRoot, "volume-moved");
        var fixture = CreateFixture(
            Password("secret1"),
            new UsbDriveInfo("E:", "Portable USB", "volume:4A2B", originalRoot));
        await fixture.Service.SetPasswordAsync("secret1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await fixture.Service.BindUsbAsync(null!, "volume:4A2B", TestContext.Current.CancellationToken));

        Directory.Move(originalRoot, movedRoot);
        fixture.UsbCatalog.SetDevices(new UsbDriveInfo("F:", "Portable USB", "volume:4A2B", movedRoot));

        var device = Assert.Single(await fixture.Service.GetUsbDevicesAsync(TestContext.Current.CancellationToken));

        Assert.True(device.IsBound);
        Assert.True(device.IsPresent);
        Assert.Equal("F:", device.DriveLetter);
    }

    [Fact]
    public async Task GetUsbDevicesAsync_WhenMarkerIsMissing_DoesNotProjectTheVolumeAsBound()
    {
        var usbRoot = CreateUsbDirectory("missing-marker");
        var fixture = CreateFixture(
            Password("secret1"),
            new UsbDriveInfo("E:", "Marker USB", "volume:missing-marker", usbRoot));
        await fixture.Service.SetPasswordAsync("secret1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await fixture.Service.BindUsbAsync(null!, "volume:missing-marker", TestContext.Current.CancellationToken));

        File.Delete(Path.Combine(usbRoot, ".SecRandom.safety.key"));

        var device = Assert.Single(await fixture.Service.GetUsbDevicesAsync(TestContext.Current.CancellationToken));
        Assert.False(device.IsBound);
        Assert.True(device.IsPresent);

        Assert.True(await fixture.Service.BindUsbAsync(null!, "volume:missing-marker", TestContext.Current.CancellationToken));
        Assert.Single(await fixture.Service.GetUsbBindingsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BindUsbAsync_WhenPasswordIsRejected_DoesNotWriteABindingKey()
    {
        var usbRoot = CreateUsbDirectory("rejected");
        var fixture = CreateFixture(
            Password("wrong"),
            new UsbDriveInfo("G:", "Rejected USB", "volume:G", usbRoot));
        await fixture.Service.SetPasswordAsync("secret1", cancellationToken: TestContext.Current.CancellationToken);

        var bound = await fixture.Service.BindUsbAsync(null!, "volume:G", TestContext.Current.CancellationToken);

        Assert.False(bound);
        Assert.False(File.Exists(Path.Combine(usbRoot, ".SecRandom.safety.key")));
        Assert.Empty(await fixture.Service.GetUsbBindingsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BindUsbAsync_WhenCallerSuppliesAPathInsteadOfADeviceId_RejectsTheRequest()
    {
        var usbRoot = CreateUsbDirectory("path-input");
        var fixture = CreateFixture(
            Password("secret1"),
            new UsbDriveInfo("I:", "Path input USB", "volume:path-input", usbRoot));
        await fixture.Service.SetPasswordAsync("secret1", cancellationToken: TestContext.Current.CancellationToken);

        var bound = await fixture.Service.BindUsbAsync(null!, usbRoot, TestContext.Current.CancellationToken);

        Assert.False(bound);
        Assert.False(File.Exists(Path.Combine(usbRoot, ".SecRandom.safety.key")));
    }

    [Fact]
    public async Task BindUsbAsync_WhenCredentialSaveFails_RemovesTheWrittenUsbKey()
    {
        var usbRoot = CreateUsbDirectory("credential-save-failure");
        var writeFault = new ThrowOnNthCredentialWrite();
        var fixture = CreateFixture(
            Password("secret1"),
            writeFault,
            new UsbDriveInfo("J:", "Persistence USB", "volume:save-failure", usbRoot));
        await fixture.Service.SetPasswordAsync("secret1", cancellationToken: TestContext.Current.CancellationToken);
        writeFault.ThrowOnWrite = writeFault.WriteCalls + 2;

        var bound = await fixture.Service.BindUsbAsync(null!, "volume:save-failure", TestContext.Current.CancellationToken);

        Assert.False(bound);
        Assert.False(File.Exists(Path.Combine(usbRoot, ".SecRandom.safety.key")));
        Assert.Empty(await fixture.Service.GetUsbBindingsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BindUsbAsync_WhenUsbAlreadyContainsASafetyKey_RejectsAndPreservesTheKey()
    {
        var usbRoot = CreateUsbDirectory("existing-key-save-failure");
        var existingKeyPath = Path.Combine(usbRoot, ".SecRandom.safety.key");
        File.WriteAllText(existingKeyPath, "existing-token", System.Text.Encoding.ASCII);
        var fixture = CreateFixture(
            Password("secret1"),
            new UsbDriveInfo("M:", "Existing key USB", "volume:existing-key", usbRoot));
        await fixture.Service.SetPasswordAsync("secret1", cancellationToken: TestContext.Current.CancellationToken);

        var bound = await fixture.Service.BindUsbAsync(null!, "volume:existing-key", TestContext.Current.CancellationToken);

        Assert.False(bound);
        Assert.Equal("existing-token", File.ReadAllText(existingKeyPath, System.Text.Encoding.ASCII));
        Assert.Empty(await fixture.Service.GetUsbBindingsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnbindUsbAsync_WhenCredentialSaveFails_KeepsTheExistingUsbKey()
    {
        var usbRoot = CreateUsbDirectory("unbind-save-failure");
        var writeFault = new ThrowOnNthCredentialWrite();
        var fixture = CreateFixture(
            Password("secret1"),
            writeFault,
            new UsbDriveInfo("K:", "Unbind persistence USB", "volume:unbind-save-failure", usbRoot));
        await fixture.Service.SetPasswordAsync("secret1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await fixture.Service.BindUsbAsync(null!, "volume:unbind-save-failure", TestContext.Current.CancellationToken));
        var binding = Assert.Single(await fixture.Service.GetUsbBindingsAsync(TestContext.Current.CancellationToken));
        writeFault.ThrowOnWrite = writeFault.WriteCalls + 2;

        var unbound = await fixture.Service.UnbindUsbAsync(null!, binding.Id, TestContext.Current.CancellationToken);

        Assert.False(unbound);
        Assert.True(File.Exists(Path.Combine(usbRoot, ".SecRandom.safety.key")));
        Assert.Single(await fixture.Service.GetUsbBindingsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BindUsbAsync_AfterUnbindWhileVolumeIsMissing_ReplacesItsPendingMarker()
    {
        var usbRoot = CreateUsbDirectory("pending-marker-replacement");
        var drive = new UsbDriveInfo("N:", "Pending marker USB", "volume:pending-marker", usbRoot);
        var fixture = CreateFixture(Password("secret1"), drive);
        await fixture.Service.SetPasswordAsync("secret1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await fixture.Service.BindUsbAsync(null!, drive.DeviceId, TestContext.Current.CancellationToken));

        var markerPath = Path.Combine(usbRoot, ".SecRandom.safety.key");
        var originalToken = File.ReadAllText(markerPath, System.Text.Encoding.ASCII);
        var binding = Assert.Single(await fixture.Service.GetUsbBindingsAsync(TestContext.Current.CancellationToken));
        fixture.UsbCatalog.SetDevices();

        Assert.True(await fixture.Service.UnbindUsbAsync(null!, binding.Id, TestContext.Current.CancellationToken));
        fixture.UsbCatalog.SetDevices(drive);

        Assert.True(await fixture.Service.BindUsbAsync(null!, drive.DeviceId, TestContext.Current.CancellationToken));
        Assert.NotEqual(originalToken, File.ReadAllText(markerPath, System.Text.Encoding.ASCII));
        Assert.Single(await fixture.Service.GetUsbBindingsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemovePasswordAsync_WhenCredentialSaveFails_KeepsTheBoundUsbKey()
    {
        var usbRoot = CreateUsbDirectory("remove-password-save-failure");
        var writeFault = new ThrowOnNthCredentialWrite();
        var fixture = CreateFixture(
            Password("secret1"),
            writeFault,
            new UsbDriveInfo("L:", "Password removal USB", "volume:remove-password-save-failure", usbRoot));
        await fixture.Service.SetPasswordAsync("secret1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await fixture.Service.BindUsbAsync(null!, "volume:remove-password-save-failure", TestContext.Current.CancellationToken));
        writeFault.ThrowOnWrite = writeFault.WriteCalls + 1;

        var removed = await fixture.Service.RemovePasswordAsync("secret1", TestContext.Current.CancellationToken);

        Assert.False(removed);
        Assert.True(File.Exists(Path.Combine(usbRoot, ".SecRandom.safety.key")));
        Assert.True(fixture.Service.GetUiState().HasPassword);
        Assert.Single(await fixture.Service.GetUsbBindingsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemovePasswordAsync_WhenTheBoundKeyChanged_StillDeletesTheExternalToken()
    {
        var usbRoot = CreateUsbDirectory("remove-password-replaced-key");
        var keyPath = Path.Combine(usbRoot, ".SecRandom.safety.key");
        var fixture = CreateFixture(
            Password("secret1"),
            new UsbDriveInfo("O:", "Replaced key USB", "volume:remove-password-replaced-key", usbRoot));
        await fixture.Service.SetPasswordAsync("secret1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await fixture.Service.BindUsbAsync(null!, "volume:remove-password-replaced-key", TestContext.Current.CancellationToken));
        File.WriteAllText(keyPath, "replacement-token", System.Text.Encoding.ASCII);

        var removed = await fixture.Service.RemovePasswordAsync("secret1", TestContext.Current.CancellationToken);

        Assert.True(removed);
        Assert.False(File.Exists(keyPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot))
            Directory.Delete(_temporaryRoot, recursive: true);
    }

    private SecurityFixture CreateFixture(SecurityVerificationResponse response, params UsbDriveInfo[] devices)
    {
        return CreateFixture(response, null, devices);
    }

    private SecurityFixture CreateFixture(
        SecurityVerificationResponse response,
        ThrowOnNthCredentialWrite? writeFault,
        params UsbDriveInfo[] devices)
    {
        Directory.CreateDirectory(_temporaryRoot);
        var configService = new TestConfigService(new MainConfigModel());
        var configHandler = new MainConfigHandler(NullLogger<MainConfigHandler>.Instance, configService);
        var prompt = new ScriptedPrompt(response);
        var credentialStore = new SecurityCredentialStore(
            Path.Combine(_temporaryRoot, Guid.NewGuid().ToString("N"), "credentials.json"),
            CredentialKdfParameters.Test,
            writeFault is null ? null : writeFault.BeforeWrite);
        var usbCatalog = new TestUsbDeviceCatalog(devices);
        var service = new SecurityService(
            configHandler,
            credentialStore,
            prompt,
            usbCatalog,
            NullLogger<SecurityService>.Instance);
        return new SecurityFixture(service, configHandler, configService, prompt, usbCatalog);
    }

    private string CreateUsbDirectory(string name)
    {
        var path = Path.Combine(_temporaryRoot, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static SecurityVerificationResponse Password(string password) => new(password, string.Empty, false);

    private static string CreateTotpCode(string secret)
    {
        var method = typeof(TotpService).GetMethod("CreateCode", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<string>(method.Invoke(null, [secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30]));
    }

    private sealed record SecurityFixture(
        SecurityService Service,
        MainConfigHandler ConfigHandler,
        TestConfigService ConfigService,
        ScriptedPrompt Prompt,
        TestUsbDeviceCatalog UsbCatalog);

    private sealed class ScriptedPrompt(SecurityVerificationResponse response) : ISecurityVerificationPrompt
    {
        public List<SecurityVerificationRequest> Requests { get; } = [];

        public Task<SecurityVerificationResponse> RequestAsync(
            TopLevel xamlRoot,
            SecurityVerificationRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowOnNthCredentialWrite
    {
        public int WriteCalls { get; private set; }
        public int? ThrowOnWrite { get; set; }

        public void BeforeWrite()
        {
            WriteCalls++;
            if (WriteCalls == ThrowOnWrite)
                throw new CryptographicException("Simulated credential persistence failure.");
        }
    }

    private sealed class TestUsbDeviceCatalog(params UsbDriveInfo[] devices) : IUsbDeviceCatalog
    {
        private IReadOnlyList<UsbDriveInfo> _devices = devices;

        public IReadOnlyList<UsbDriveInfo> GetRemovableDevices() => _devices;

        public void SetDevices(params UsbDriveInfo[] devices) => _devices = devices;
    }

    private sealed class TestConfigService(MainConfigModel config) : ConfigServiceBase
    {
        public int SaveCount { get; private set; }

        public override bool IsConfigExists<T>(T fallback) => true;

        public override T LoadConfig<T>(T fallback) =>
            config is T typed ? typed : fallback;

        public override void SaveConfig<T>(T saved) => SaveCount++;

        public override void DeleteConfig<T>(T deleted)
        {
        }

        public void ResetSaveCount() => SaveCount = 0;
    }
}
