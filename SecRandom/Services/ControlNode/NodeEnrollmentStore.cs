using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Services.Security;
using SecRandom.Shared;

namespace SecRandom.Services.ControlNode;

/// <summary>
///     接入令牌的独立加密存储：<c>data/config/security/control-node.json</c>。
/// </summary>
/// <remarks>
///     <para>
///         <b>为什么单独一个文件，而不是塞进 <c>credentials.json</c>：</b>
///         <c>credentials.json</c> 的载荷要用用户的安全密码才解得开，而接入令牌必须在**冷启动、
///         用户还没输任何密码之前**就能读到——不然装了自建集控的教室机每次开机都要先输一遍安全密码
///         才连得上，这个功能等于没有。这与 <c>SecuritySettingsStore</c> 把安全开关单独加密存放
///         是同一条理由、同一种做法。
///     </para>
///     <para>
///         <b>保护限度（必须说清楚）：这是机器本地保护，不是用户口令保护。</b>
///         密钥是设备本地的随机 256 位值（同目录的 <c>control-node.key</c>），整个
///         <c>data/config/security</c> 目录只允许当前用户访问（复用 <see cref="SecurityPathProtection" />，
///         与凭据文件、安全设置同一个边界）。能读到这个目录的人就能读出令牌——与"能重写凭据文件
///         就能重置锁定状态"是同一个边界，不比它弱、也不比它强。真正的安全边界在服务端：
///         令牌可撤销、只绑一台设备一个组。
///     </para>
///     <para>
///         这个目录**不参与任何导出**：设置导入导出、云备份、诊断包、Android DocumentsProvider
///         都拿不到它（见 <c>SecurityCredentialStore</c> 的同类说明）。
///     </para>
///     <para>
///         <b>读不出来时绝不崩、也绝不静默无脑重连：</b>解密失败/文件被改坏/换了机器 ⇒ 就地清除记录，
///         状态回到"未接入（需要重新接入）"，界面给出重新接入的提示。这与 <c>SecuritySettingsStore</c>
///         的 fail-closed 方向一致：损坏不能变成"继续用一个不知道还是不是自己的凭据"。
///     </para>
/// </remarks>
public sealed class FileNodeEnrollmentStore : INodeEnrollmentStore
{
    private const int FormatVersion = 1;
    private const int KeyFormatVersion = 1;
    private const int EncryptionKeyLength = 32;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const string FileName = "control-node.json";
    private const string KeyFileName = "control-node.key";

    /// <summary>令牌前缀（服务端契约）：也是"填进来的是令牌还是接入码"的判据。</summary>
    internal const string TokenPrefix = "srn_";

    /// <summary>
    ///     认证附加数据把信封绑死在"接入令牌"这一用途上：同一把密钥换个用途或换一版格式都通不过校验。
    /// </summary>
    private static readonly byte[] AssociatedData = Encoding.ASCII.GetBytes("SecRandom/NodeEnrollment/v1");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ILogger<FileNodeEnrollmentStore> _logger;
    private readonly string _path;
    private readonly string _keyPath;
    private readonly object _gate = new();

    private byte[]? _key;
    private bool _directoryProtected;
    private NodeEnrollmentRecord? _record;
    private NodeEnrollmentStatus _status = NodeEnrollmentStatus.NotEnrolled;

    public FileNodeEnrollmentStore(ILogger<FileNodeEnrollmentStore> logger)
        : this(Utils.GetFilePath("config", "security", FileName), logger)
    {
    }

    /// <summary>测试用：指定文件路径。</summary>
    internal FileNodeEnrollmentStore(string path, ILogger<FileNodeEnrollmentStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _keyPath = Path.Combine(
            Path.GetDirectoryName(path) ?? throw new InvalidOperationException("The enrollment path has no directory."),
            KeyFileName);
        _logger = logger;

        _status = Load();
    }

    public NodeEnrollmentStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    public int Generation { get; private set; }

    public event EventHandler<NodeEnrollmentStatus>? Changed;

    public bool TryGetAccessToken(out string? accessToken)
    {
        accessToken = null;

        NodeEnrollmentRecord? record;
        lock (_gate)
        {
            // 只读内存里的那一份：令牌在启动时已经解密过一次，这里再走一遍磁盘解密既浪费，
            // 又可能在看盘失败时顺手把记录清掉（读操作**绝不能有副作用**）。
            record = _record;
        }

        if (record is null)
            return false;

        accessToken = record.NodeToken;
        return true;
    }

    public bool Save(NodeEnrollmentRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        bool written;
        try
        {
            var key = GetOrCreateKey();
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                EnsureDirectoryProtection(directory);
            }

            var payload = JsonSerializer.SerializeToUtf8Bytes(
                new EnrollmentPayload
                {
                    NodeId = record.NodeId,
                    GroupId = record.GroupId,
                    GroupName = record.GroupName,
                    NodeToken = record.NodeToken,
                    ExpiresAt = record.ExpiresAt,
                    EnrolledAt = record.EnrolledAt
                },
                JsonOptions);

            var nonce = RandomNumberGenerator.GetBytes(NonceLength);
            var ciphertext = new byte[payload.Length];
            var tag = new byte[TagLength];
            using (var aes = new AesGcm(key, TagLength))
            {
                aes.Encrypt(nonce, payload, ciphertext, tag, AssociatedData);
            }

            // 先把密钥写稳，再写令牌：反过来会出现"令牌写好了但解不开"，那在用户眼里就是"刚接入就掉线"。
            EnsureKeyFile(key);

            var envelope = new EnrollmentEnvelope
            {
                FormatVersion = FormatVersion,
                Nonce = Convert.ToBase64String(nonce),
                Tag = Convert.ToBase64String(tag),
                Ciphertext = Convert.ToBase64String(ciphertext)
            };

            WriteAtomically(JsonSerializer.Serialize(envelope, JsonOptions));
            written = true;
        }
        catch (Exception exception) when (IsPersistenceFailure(exception))
        {
            // 不抛给调用方：接入失败会由"状态没变"表现，界面照样停在未接入，比弹一个异常好排查。
            _logger.LogWarning(exception, "接入令牌保存失败：{Path}", _path);
            written = false;
        }

        if (!written)
            return false;

        var status = ToStatus(record);
        lock (_gate)
        {
            _record = record;
            _status = status;
            Generation++;
        }

        Changed?.Invoke(this, status);
        // 只记身份与到期时间，**永远不记令牌**。
        _logger.LogInformation(
            "接入信息已保存：节点={NodeId}，组={GroupId}，到期={ExpiresAt}",
            record.NodeId, record.GroupId, record.ExpiresAt?.ToString("O") ?? "未提供");
        return true;
    }

    public bool Clear()
    {
        var had = false;
        lock (_gate)
        {
            had = _record is not null || _status.HasToken || _status.IsUnreadable;
            _record = null;
            _status = NodeEnrollmentStatus.NotEnrolled;
            Generation++;
        }

        var deleted = TryDelete(_path) | TryDelete(_keyPath);
        if (had || deleted)
            Changed?.Invoke(this, NodeEnrollmentStatus.NotEnrolled);

        return had || deleted;
    }

    /// <summary>读出并解密记录；任何异常都当成"没有记录"，并顺手把坏掉的文件清掉。</summary>
    private NodeEnrollmentRecord? LoadRecord()
    {
        if (!File.Exists(_path))
            return null;

        try
        {
            var envelope = JsonSerializer.Deserialize<EnrollmentEnvelope>(File.ReadAllText(_path), JsonOptions);
            if (envelope is null
                || envelope.FormatVersion != FormatVersion
                || !TryDecode(envelope.Nonce, NonceLength, out var nonce)
                || !TryDecode(envelope.Tag, TagLength, out var tag)
                || !TryDecode(envelope.Ciphertext, null, out var ciphertext))
            {
                return DiscardCorrupt("接入记录格式不正确");
            }

            if (!TryReadKey(out var key))
                return DiscardCorrupt("接入记录的本地密钥缺失或不可读");

            byte[] payload;
            try
            {
                payload = new byte[ciphertext.Length];
                using var aes = new AesGcm(key, TagLength);
                aes.Decrypt(nonce, ciphertext, tag, payload, AssociatedData);
            }
            catch (CryptographicException exception)
            {
                _logger.LogWarning(exception, "接入记录解密失败（可能被改过或换了机器）：{Path}", _path);
                return DiscardCorrupt(null);
            }

            var stored = JsonSerializer.Deserialize<EnrollmentPayload>(payload, JsonOptions);
            if (stored is null
                || string.IsNullOrWhiteSpace(stored.NodeId)
                || string.IsNullOrWhiteSpace(stored.NodeToken))
            {
                return DiscardCorrupt("接入记录内容不完整");
            }

            return new NodeEnrollmentRecord
            {
                NodeId = stored.NodeId!,
                GroupId = stored.GroupId ?? string.Empty,
                GroupName = stored.GroupName,
                NodeToken = stored.NodeToken!,
                ExpiresAt = stored.ExpiresAt,
                EnrolledAt = stored.EnrolledAt
            };
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            return DiscardCorrupt($"接入记录无法读取：{exception.GetType().Name}");
        }
    }

    /// <summary>
    ///     丢弃读不出来的记录并回到"未接入"。
    /// </summary>
    /// <remarks>
    ///     必须清盘：留着它会让每次启动都失败一次，而用户看到的只是"连不上"，无从下手。
    ///     清掉之后状态是明确的「未接入（需要重新接入）」，用户在设置页重新填一次接入码就能恢复。
    /// </remarks>
    private NodeEnrollmentRecord? DiscardCorrupt(string? reason)
    {
        if (reason is not null)
            _logger.LogWarning("接入记录已丢弃：{Reason}（{Path}）", reason, _path);

        TryDelete(_path);
        TryDelete(_keyPath);
        lock (_gate)
        {
            _record = null;
            _status = new NodeEnrollmentStatus { IsUnreadable = true };
            Generation++;
        }

        return null;
    }

    private NodeEnrollmentStatus ToStatus(NodeEnrollmentRecord record) => new()
    {
        HasToken = true,
        IsExpired = record.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow,
        NodeId = record.NodeId,
        GroupId = record.GroupId,
        GroupName = record.GroupName,
        ExpiresAt = record.ExpiresAt
    };

    /// <summary>启动时读一次：成功就是已接入，失败就是"需要重新接入"。</summary>
    private NodeEnrollmentStatus Load()
    {
        NodeEnrollmentRecord? record;
        lock (_gate)
        {
            record = LoadRecord();
            _record = record;
            if (record is null)
                return _status;

            _status = ToStatus(record);
            return _status;
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
            var file = JsonSerializer.Deserialize<EnrollmentKeyFile>(File.ReadAllText(_keyPath), JsonOptions);
            if (file is null || file.FormatVersion != KeyFormatVersion || !IsValidBase64(file.Key, EncryptionKeyLength))
                return false;

            key = Convert.FromBase64String(file.Key!);
            return true;
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            _logger.LogWarning(exception, "接入记录的本地密钥无法读取：{Path}", _keyPath);
            return false;
        }
    }

    private void EnsureKeyFile(byte[] key)
    {
        var content = JsonSerializer.Serialize(
            new EnrollmentKeyFile { FormatVersion = KeyFormatVersion, Key = Convert.ToBase64String(key) },
            JsonOptions);

        if (File.Exists(_keyPath))
        {
            try
            {
                if (string.Equals(File.ReadAllText(_keyPath), content, StringComparison.Ordinal))
                    return;
            }
            catch (Exception exception) when (IsReadFailure(exception))
            {
                // 读不出来就走下面的重写路径。
            }
        }

        WriteAtomically(_keyPath, content);
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

    private void WriteAtomically(string content) => WriteAtomically(_path, content);

    /// <summary>临时文件 + 原子替换；临时文件也先按当前用户收紧权限（它同样含有令牌/密钥）。</summary>
    private void WriteAtomically(string path, string content)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, content, Encoding.UTF8);
            SecurityPathProtection.RestrictFileToOwner(temporaryPath);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static bool TryDecode(string? value, int? expectedLength, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value))
            return false;

        try
        {
            var decoded = Convert.FromBase64String(value);
            if (expectedLength is { } length && decoded.Length != length)
                return false;

            bytes = decoded;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsValidBase64(string? value, int expectedLength) =>
        TryDecode(value, expectedLength, out _);

    private static bool TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;

            File.Delete(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 删除是尽力而为：清不掉时内存里已经不再持有这条记录。
            return false;
        }
    }

    private static bool IsReadFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or JsonException or FormatException
            or CryptographicException or NotSupportedException or ArgumentException;

    private static bool IsPersistenceFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or CryptographicException
            or NotSupportedException or ArgumentException;

    /// <summary>加密信封：应用内部结构，命名风格沿用凭据文件（不跟设置文件的 snake_case）。</summary>
    private sealed class EnrollmentEnvelope
    {
        public int FormatVersion { get; set; }
        public string? Nonce { get; set; }
        public string? Tag { get; set; }
        public string? Ciphertext { get; set; }
    }

    /// <summary>
    ///     设备本地的加密密钥。它自己的格式也带版本号：将来换算法时能分辨旧文件。
    /// </summary>
    private sealed class EnrollmentKeyFile
    {
        public int FormatVersion { get; set; }
        public string? Key { get; set; }
    }

    /// <summary>明文载荷（只以密文形式落盘）。</summary>
    private sealed class EnrollmentPayload
    {
        public string? NodeId { get; set; }
        public string? GroupId { get; set; }
        public string? GroupName { get; set; }
        public string? NodeToken { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public DateTimeOffset EnrolledAt { get; set; }
    }
}
