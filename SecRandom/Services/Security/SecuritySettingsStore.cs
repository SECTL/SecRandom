using System;
using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Shared;

namespace SecRandom.Services.Security;

/// <summary>
///     安全设置（防护总开关、因素选择、受保护操作、Sudo、防篡改校验）的独立加密存储。
/// </summary>
/// <remarks>
///     <para>
///         这些开关本身就是防护的一部分。它们以前明文住在 <c>data/config/settings.json</c> 里，
///         任何能编辑 JSON 的人都能把 <c>security_enabled</c> 改成 false 再重启，等于没有防护。
///         现在它们单独住在 <c>data/config/security/settings.json</c>，用 AES-256-GCM 加密：
///         认证标签让"改一个字段"直接变成"整份文件校验失败"，而不是生效。
///     </para>
///     <para>
///         密钥是设备本地的随机 256 位值（同目录的 <c>settings.key</c>），整个目录只允许当前用户
///         访问，因此启动时不必先输入安全密码——"要不要验证"本身就要先读到这些开关才能判断。
///         代价是它与 <c>credentials.json</c> 处在同一保护级别：能读到这个目录的人就能伪造设置，
///         和"能重写凭据文件就能重置锁定状态"是同一个边界。
///     </para>
///     <para>
///         读不出来时**不回落到全关**。文件解不开（被改过、密钥被换过、磁盘损坏），或者文件干脆
///         不见了而本机早就配过安全密码，都说明盘上的状态已经不可信：此时按最严的一档生效，
///         于是"改坏它"和"删掉它"都不能成为关掉防护的办法。用户仍然可以用原来的安全密码进入
///         安全设置页把开关改回来，改完就会重新写出一份有效文件。
///     </para>
///     <para>
///         首次运行做一次迁移：旧版 settings.json 里的 <c>security_settings</c> 会被取出来写进这个
///         加密文件，然后立刻重写一次 settings.json，把留在旧文件里的明文副本删掉。
///     </para>
/// </remarks>
internal sealed class SecuritySettingsStore
{
    private const int FormatVersion = 1;
    private const int KeyFormatVersion = 1;
    private const int EncryptionKeyLength = 32;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const string SettingsFileName = "settings.json";
    private const string KeyFileName = "settings.key";

    /// <summary>
    ///     认证附加数据把信封绑死在"安全设置"这一用途上：同一把密钥下换个文件类型或换一版格式
    ///     都无法通过校验。
    /// </summary>
    private static readonly byte[] AssociatedData = Encoding.ASCII.GetBytes("SecRandom/SecuritySettings/v1");

    /// <summary>信封与密钥文件都是应用内部结构，沿用凭据文件的命名风格（不跟设置文件的 snake_case）。</summary>
    private static readonly JsonSerializerOptions EnvelopeJsonOptions = new() { WriteIndented = true };

    private readonly ILogger<SecuritySettingsStore> _logger;
    private readonly MainConfigHandler? _configHandler;
    private readonly SecurityCredentialStore? _credentialStore;
    private readonly string _settingsPath;
    private readonly string _keyPath;
    private readonly Action? _beforeWrite;
    private readonly object _gate = new();
    private byte[]? _key;
    private bool _directoryProtected;

    public SecuritySettingsStore(
        MainConfigHandler configHandler,
        SecurityCredentialStore credentialStore,
        ILogger<SecuritySettingsStore> logger)
        : this(
            Utils.GetFilePath("config", "security", SettingsFileName),
            configHandler,
            credentialStore,
            logger,
            beforeWrite: null)
    {
    }

    internal SecuritySettingsStore(string settingsPath, ILogger<SecuritySettingsStore> logger, Action? beforeWrite = null)
        : this(settingsPath, configHandler: null, credentialStore: null, logger, beforeWrite)
    {
    }

    internal SecuritySettingsStore(
        string settingsPath,
        MainConfigHandler? configHandler,
        SecurityCredentialStore? credentialStore,
        ILogger<SecuritySettingsStore> logger,
        Action? beforeWrite = null)
    {
        _settingsPath = settingsPath;
        _keyPath = Path.Combine(
            Path.GetDirectoryName(settingsPath) ?? throw new InvalidOperationException("The security settings path has no directory."),
            KeyFileName);
        _configHandler = configHandler;
        _credentialStore = credentialStore;
        _logger = logger;
        _beforeWrite = beforeWrite;

        Data = Load();
        Data.PropertyChanged += DataOnPropertyChanged;
    }

    /// <summary>内存中的安全设置。所有改动都必须经由安全服务的验证边界，改动会自动落盘。</summary>
    public SecuritySettingsConfig Data { get; private set; }

    /// <summary>
    ///     磁盘上的加密设置存在却读不出来。此时 <see cref="Data" /> 是最严的一档而不是文件里的值，
    ///     调用方可以据此提示用户"设置文件已失效"。
    /// </summary>
    public bool IsIntegrityCompromised { get; private set; }

    public event EventHandler? Saved;

    public void Save()
    {
        lock (_gate)
        {
            Write(Data);
        }

        Saved?.Invoke(this, EventArgs.Empty);
    }

    private void DataOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        try
        {
            Save();
        }
        catch (Exception exception) when (IsPersistenceFailure(exception))
        {
            // 自动保存由属性写入触发，不能把一次磁盘故障变成设置页里的异常。
            _logger.LogWarning(exception, "Unable to persist security settings: {Path}", _settingsPath);
        }
    }

    private SecuritySettingsConfig Load()
    {
        if (File.Exists(_settingsPath))
            return TryReadEncrypted(out var stored) ? stored : Compromised("could not be decrypted");

        // 旧版本把这组开关明文放在 settings.json 里，只有旧版本写出来的 settings.json 才有这一段，
        // 所以这是升级路径。迁移只在这个加密文件不存在时发生，之后一份构造出来的 settings.json
        // 或备份都无法重新指定这组开关。
        if (_configHandler is { } handler && handler.Data.ConsumeLegacySecuritySettings() is { } legacy)
        {
            _logger.LogInformation("Migrating the security settings out of settings.json.");
            Write(legacy);
            ScrubLegacyPlaintext(handler);
            return legacy;
        }

        // 文件不在、也没有旧版明文副本，但本机早就配过安全密码：这不是全新安装，按最严的一档处理。
        if (_credentialStore?.LoadMetadata().Password is not null)
            return Compromised("is missing while a security password is configured");

        var defaults = new SecuritySettingsConfig();
        Write(defaults);
        return defaults;
    }

    private SecuritySettingsConfig Compromised(string reason)
    {
        IsIntegrityCompromised = true;
        _logger.LogWarning(
            "The security settings file {Reason}; protective defaults are in effect instead: {Path}",
            reason,
            _settingsPath);
        return CreateProtectiveDefaults();
    }

    /// <summary>
    ///     迁移过后旧 settings.json 里还留着明文副本，重写一次让模型序列化时把它去掉，
    ///     免得"盘上还有一份能改的明文设置"这种误导性的残留。
    /// </summary>
    private void ScrubLegacyPlaintext(MainConfigHandler handler)
    {
        try
        {
            handler.Save();
        }
        catch (Exception exception) when (IsPersistenceFailure(exception))
        {
            _logger.LogWarning(exception, "Unable to drop the legacy plaintext security settings from settings.json.");
        }
    }

    /// <summary>
    ///     最严的一档：防护总开关打开、所有受保护操作打开。因素开关不在这里预置，它们由凭据推导。
    ///     防篡改校验不在这里打开——那是用户明确选择的功能，不能替用户打开。
    /// </summary>
    private static SecuritySettingsConfig CreateProtectiveDefaults() => new()
    {
        SecurityEnabled = true,
        SudoModeEnabled = true,
        SettingsIntegrityAction = SettingsIntegrityAction.Confirm,
        SettingsIntegrityRestoreSource = SettingsIntegrityRestoreSource.Local,
        ProtectOpenSettings = true,
        ProtectToggleMainWindow = true,
        ProtectToggleFloatingWindow = true,
        ProtectRestart = true,
        ProtectExit = true,
        ProtectRollCallStart = true,
        ProtectRollCallReset = true,
        ProtectQuickDrawStart = true,
        ProtectQuickDrawReset = true,
        ProtectLotteryStart = true,
        ProtectLotteryReset = true,
        ProtectLinkage = true
    };

    private bool TryReadEncrypted(out SecuritySettingsConfig settings)
    {
        settings = new SecuritySettingsConfig();
        if (!File.Exists(_settingsPath))
            return false;

        try
        {
            var envelope = JsonSerializer.Deserialize<SecuritySettingsEnvelope>(
                File.ReadAllText(_settingsPath), EnvelopeJsonOptions);
            if (envelope is null || envelope.FormatVersion != FormatVersion ||
                !IsValidBase64(envelope.Nonce, NonceLength) ||
                !IsValidBase64(envelope.Tag, TagLength) ||
                string.IsNullOrWhiteSpace(envelope.Ciphertext))
                return false;

            var nonce = Convert.FromBase64String(envelope.Nonce);
            var tag = Convert.FromBase64String(envelope.Tag);
            var ciphertext = Convert.FromBase64String(envelope.Ciphertext);
            var payload = new byte[ciphertext.Length];
            try
            {
                using var aes = new AesGcm(GetOrCreateKey(), TagLength);
                aes.Decrypt(nonce, ciphertext, tag, payload, AssociatedData);
                settings = JsonSerializer.Deserialize<SecuritySettingsConfig>(payload, ConfigServiceBase.JsonOptions)
                           ?? new SecuritySettingsConfig();
                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            _logger.LogWarning(exception, "Unable to read the encrypted security settings file: {Path}", _settingsPath);
            return false;
        }
    }

    private void Write(SecuritySettingsConfig settings)
    {
        var key = GetOrCreateKey();
        var payload = JsonSerializer.SerializeToUtf8Bytes(settings, ConfigServiceBase.JsonOptions);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var ciphertext = new byte[payload.Length];
        var tag = new byte[TagLength];
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Encrypt(nonce, payload, ciphertext, tag, AssociatedData);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }

        var directory = Path.GetDirectoryName(_settingsPath)
                        ?? throw new InvalidOperationException("The security settings path has no directory.");
        Directory.CreateDirectory(directory);
        EnsureDirectoryProtection(directory);
        _beforeWrite?.Invoke();
        var envelope = new SecuritySettingsEnvelope
        {
            FormatVersion = FormatVersion,
            Nonce = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Ciphertext = Convert.ToBase64String(ciphertext)
        };

        // 密钥文件先落地：只有它和设置文件都写完，这份加密设置才读得回来。
        EnsureKeyFile(key);
        WriteAtomically(JsonSerializer.Serialize(envelope, EnvelopeJsonOptions));
        IsIntegrityCompromised = false;
    }

    private void WriteAtomically(string content)
    {
        var temporaryPath = $"{_settingsPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, content, Encoding.UTF8);
            SecurityPathProtection.RestrictFileToOwner(temporaryPath);
            File.Move(temporaryPath, _settingsPath, true);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private byte[] GetOrCreateKey()
    {
        lock (_gate)
        {
            if (_key is { } cached)
                return cached;

            if (TryReadKey(out var stored))
            {
                _key = stored;
                return stored;
            }

            _key = RandomNumberGenerator.GetBytes(EncryptionKeyLength);
            return _key;
        }
    }

    private bool TryReadKey(out byte[] key)
    {
        key = [];
        if (!File.Exists(_keyPath))
            return false;

        try
        {
            var file = JsonSerializer.Deserialize<SecuritySettingsKeyFile>(File.ReadAllText(_keyPath), EnvelopeJsonOptions);
            if (file is null || file.FormatVersion != KeyFormatVersion || !IsValidBase64(file.Key, EncryptionKeyLength))
                return false;

            key = Convert.FromBase64String(file.Key);
            return true;
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            _logger.LogWarning(exception, "Unable to read the security settings key: {Path}", _keyPath);
            return false;
        }
    }

    private void EnsureKeyFile(byte[] key)
    {
        var content = JsonSerializer.Serialize(
            new SecuritySettingsKeyFile { FormatVersion = KeyFormatVersion, Key = Convert.ToBase64String(key) },
            EnvelopeJsonOptions);
        if (File.Exists(_keyPath) && string.Equals(File.ReadAllText(_keyPath), content, StringComparison.Ordinal))
            return;

        var temporaryPath = $"{_keyPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, content, Encoding.UTF8);
            SecurityPathProtection.RestrictFileToOwner(temporaryPath);
            File.Move(temporaryPath, _keyPath, true);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    /// <summary>
    ///     与凭据文件共享同一个目录规则：目录只允许当前用户访问。加固失败不影响读写。
    /// </summary>
    private void EnsureDirectoryProtection(string directory)
    {
        if (_directoryProtected)
            return;

        _directoryProtected = true;
        SecurityPathProtection.RestrictDirectoryToOwner(directory);
    }

    private static bool IsValidBase64(string? value, int expectedLength)
    {
        try
        {
            return value is not null && Convert.FromBase64String(value).Length == expectedLength;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsReadFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or JsonException or FormatException
            or CryptographicException or NotSupportedException or ArgumentException;

    private static bool IsPersistenceFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or CryptographicException
            or NotSupportedException or ArgumentException;

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 临时文件清理是尽力而为的
        }
    }
}

/// <summary>加密信封。字段名沿用凭据文件的风格，与 <c>FileConfigService.IsEncryptedEnvelope</c> 无关。</summary>
internal sealed class SecuritySettingsEnvelope
{
    public int FormatVersion { get; init; }
    public required string Nonce { get; init; }
    public required string Tag { get; init; }
    public required string Ciphertext { get; init; }
}

/// <summary>
///     设备本地的设置加密密钥。它自己的格式也带版本号，将来换算法时能分辨旧文件。
/// </summary>
internal sealed class SecuritySettingsKeyFile
{
    public int FormatVersion { get; init; }
    public required string Key { get; init; }
}
