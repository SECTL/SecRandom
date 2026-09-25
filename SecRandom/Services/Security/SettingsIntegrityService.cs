using System;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using SecRandom.Core;
using SecRandom.Core.Services.Config;

namespace SecRandom.Services.Security;

/// <summary>
///     settings.json 整份文件的防篡改校验。
///     开启后，每次配置写盘都会把「整份文件哈希 + 记录时间 + 程序版本 + 序号」写进安全凭据
///     信封的明文部分，启动时再比对文件哈希；不一致即说明该文件在上次记录之后被程序之外的
///     东西改过。指纹依赖凭据文件作为锚点，未设置安全密码时该功能不生效。
/// </summary>
internal sealed class SettingsIntegrityService
{
    private readonly MainConfigHandler _configHandler;
    private readonly SecurityCredentialStore _credentialStore;
    private readonly ILogger<SettingsIntegrityService> _logger;
    private readonly string? _configFilePath;

    public SettingsIntegrityService(
        MainConfigHandler configHandler,
        SecurityCredentialStore credentialStore,
        ILogger<SettingsIntegrityService> logger,
        string? configFilePath = null)
    {
        _configHandler = configHandler;
        _credentialStore = credentialStore;
        _logger = logger;
        _configFilePath = configFilePath;
        configHandler.Saved += OnConfigWritten;
        configHandler.Reloaded += OnConfigWritten;
    }

    private string ConfigFilePath => _configFilePath ?? _configHandler.Data.ConfigFilePath;

    /// <summary>是否存在指纹记录，即防篡改校验是否已开启且凭据可用。</summary>
    public bool IsEnabled => _credentialStore.LoadMetadata().SettingsIntegrity is not null;

    /// <summary>
    ///     返回本次启动检测到的「设置文件与记录不符」，没有记录或校验通过时返回 null。
    /// </summary>
    public SettingsIntegrityMismatch? GetPendingMismatch()
    {
        var record = _credentialStore.LoadMetadata().SettingsIntegrity;
        if (record is null)
            return null;

        var digest = TryComputeDigest(out var lastWriteTimeUtc);
        if (digest is not null && string.Equals(digest, record.Digest, StringComparison.Ordinal))
            return null;

        _logger.LogWarning(
            "Settings integrity check failed: the settings file does not match its recorded fingerprint.");
        return new SettingsIntegrityMismatch(record.RecordedAtUtc, record.RecordedAppVersion, lastWriteTimeUtc);
    }

    /// <summary>
    ///     以当前磁盘内容为准刷新指纹；设置项已关闭或没有可写锚点时清除指纹。
    /// </summary>
    public void AcceptCurrentFile()
    {
        try
        {
            var metadata = _credentialStore.LoadMetadata();
            if (metadata.Password is null)
                return;

            if (!_configHandler.Data.SecuritySettings.SettingsIntegrityCheckEnabled)
            {
                ClearRecord(metadata);
                return;
            }

            var digest = TryComputeDigest(out _);
            if (digest is null)
            {
                _logger.LogWarning(
                    "Settings integrity fingerprint cannot be recorded because the settings file is unavailable.");
                ClearRecord(metadata);
                return;
            }

            if (metadata.SettingsIntegrity is { } current && string.Equals(current.Digest, digest, StringComparison.Ordinal))
                return;

            metadata.SettingsIntegrity = new SettingsIntegrityRecord
            {
                Digest = digest,
                RecordedAtUtc = DateTimeOffset.UtcNow,
                RecordedAppVersion = GlobalConstants.Version,
                Generation = (metadata.SettingsIntegrity?.Generation ?? 0) + 1
            };
            _credentialStore.SaveMetadata(metadata);
        }
        catch (Exception exception) when (IsPersistenceFailure(exception))
        {
            _logger.LogWarning(exception, "Settings integrity fingerprint could not be persisted.");
        }
    }

    private void ClearRecord(SecurityCredentialMetadata metadata)
    {
        if (metadata.SettingsIntegrity is null)
            return;

        metadata.SettingsIntegrity = null;
        try
        {
            _credentialStore.SaveMetadata(metadata);
        }
        catch (Exception exception) when (IsPersistenceFailure(exception))
        {
            _logger.LogWarning(exception, "Settings integrity fingerprint could not be cleared.");
        }
    }

    private void OnConfigWritten(object? sender, EventArgs e) => AcceptCurrentFile();

    private string? TryComputeDigest(out DateTimeOffset? lastWriteTimeUtc)
    {
        lastWriteTimeUtc = null;
        try
        {
            var path = ConfigFilePath;
            if (!File.Exists(path))
                return null;

            lastWriteTimeUtc = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        }
        catch (Exception exception) when (IsPersistenceFailure(exception))
        {
            _logger.LogWarning(exception, "Settings integrity fingerprint could not be computed.");
            return null;
        }
    }

    private static bool IsPersistenceFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or CryptographicException;
}

/// <summary>设置文件与指纹记录不一致时用于启动提示的上下文。</summary>
internal readonly record struct SettingsIntegrityMismatch(
    DateTimeOffset RecordedAtUtc,
    string? RecordedAppVersion,
    DateTimeOffset? FileModifiedAtUtc);
