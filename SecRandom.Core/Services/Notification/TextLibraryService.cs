using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Helpers;
using SecRandom.Core.Models.Notification;

namespace SecRandom.Core.Services.Notification;

/// <summary>
///     全屏通知文案库：默认 30 条产品文案 + 用户自建条目，按使用次数做平均线轮换。
/// </summary>
/// <remarks>
///     <para>
///         落盘位置是 <c>data/texts/library.json</c>，刻意不在 <c>data/config/</c> 下：
///         备份归档会把 <c>config/</c> 里除 device-uuid 之外的路径折叠成 <c>config/settings.json</c>，
///         放进去等于让文案库在备份/还原里被静默吞掉。
///     </para>
///     <para>
///         计数是"尽力而为"的公平性辅助量，不是账本：文件损坏时整库重建默认 30 条并保存，
///         保存失败只记日志不抛出——文案库坏掉不应该让抽取流程失败。
///     </para>
///     <para>
///         文件的 <c>format_version</c> 高于本版本时只读：旧代码不知道新字段的语义，
///         写回会把新版本的字段整段抹掉。
///     </para>
/// </remarks>
public sealed class TextLibraryService
{
    private const int CurrentFormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(ConfigServiceBase.JsonOptions)
    {
        // 用户会手改这个文件：允许注释与尾逗号，省得为了一个逗号整库重建
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<TextLibraryService> _logger;
    private readonly object _gate = new();

    private List<TextLibraryEntry> _entries = new();
    private string? _lastPickedId;
    private bool _readOnly;

    /// <summary>
    ///     生产环境用：路径固定为 <c>data/texts/library.json</c>（目录不存在时创建）。
    /// </summary>
    public TextLibraryService(ILogger<TextLibraryService> logger)
        // GetDirectoryPath 负责建目录并返回目录本身，文件名必须再拼一次：
        // 直接传 GetDirectoryPath("texts", "library.json") 会把该文件当目录建出来。
        : this(logger, Path.Combine(SecRandom.Shared.Utils.GetDirectoryPath("texts"), "library.json"))
    {
    }

    /// <summary>
    ///     测试/迁移用：显式指定文件路径。目录不存在时在首次保存时创建。
    /// </summary>
    public TextLibraryService(ILogger<TextLibraryService> logger, string filePath)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        _logger = logger;
        FilePath = Path.GetFullPath(filePath);
        Reload();
    }

    /// <summary>实际使用的库文件路径（测试可据此断言落盘）。</summary>
    public string FilePath { get; }

    /// <summary>当前库内容的快照；每次取值都是一份新数组，调用方修改它不会影响服务。</summary>
    public IReadOnlyList<TextLibraryEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToArray();
            }
        }
    }

    /// <summary>所有条目（含停用条目）的使用次数总和。</summary>
    public int TotalUseCount
    {
        get
        {
            lock (_gate)
            {
                var total = 0L;
                foreach (var entry in _entries)
                    total += entry.UseCount;
                return total > int.MaxValue ? int.MaxValue : (int)total;
            }
        }
    }

    /// <summary>启用条目的平均使用次数；没有启用条目时为 0。</summary>
    public double AverageUseCount
    {
        get
        {
            lock (_gate)
            {
                return TextLibraryRotation.AverageUseCount(_entries);
            }
        }
    }

    /// <summary>当前处于平均线以内、本轮可被选中的条目数。</summary>
    public int EligibleCount
    {
        get
        {
            lock (_gate)
            {
                return TextLibraryRotation.GetEligible(_entries, TextLibraryRotation.AverageUseCount(_entries)).Count;
            }
        }
    }

    /// <summary>库内容发生变化（且已尝试落盘）后触发，UI 据此刷新列表。</summary>
    public event EventHandler? Changed;

    /// <summary>
    ///     从磁盘重新加载；文件缺失按"新建默认库"处理，内容损坏按"重建默认库"处理，两者都会立即写盘。
    /// </summary>
    public void Reload()
    {
        lock (_gate)
        {
            ReloadCore();
        }

        RaiseChanged();
    }

    /// <summary>
    ///     把当前内存状态写回磁盘；只读模式下跳过并告警，写入失败只记日志。
    /// </summary>
    public void Save()
    {
        lock (_gate)
        {
            PersistCore();
        }

        RaiseChanged();
    }

    /// <summary>
    ///     整库替换为给定模板：所有计数清零、<c>LastPickedId</c> 清空。
    ///     全部模板都非法时保持原库不动——否则一次坏导入会留下一个空库。
    /// </summary>
    public void ReplaceAll(IEnumerable<string> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);

        var changed = false;
        lock (_gate)
        {
            var entries = BuildEntries(templates, out var skipped);
            if (entries.Count == 0)
            {
                _logger.LogWarning("文案库替换被忽略：没有合法文案（跳过 {Skipped} 条）", skipped);
                return;
            }

            if (skipped > 0)
                _logger.LogWarning("文案库替换跳过了 {Skipped} 条非法文案", skipped);

            _entries = entries;
            _lastPickedId = null;
            PersistCore();
            changed = true;
        }

        if (changed)
            RaiseChanged();
    }

    /// <summary>恢复 30 条默认文案，并清零全部计数。</summary>
    public void RestoreDefaults()
    {
        lock (_gate)
        {
            LoadDefaultsCore();
            PersistCore();
        }

        RaiseChanged();
    }

    /// <summary>只清计数与 <c>LastPickedId</c>，条目本身（含停用状态与顺序）保留。</summary>
    public void ResetCounts()
    {
        lock (_gate)
        {
            foreach (var entry in _entries)
            {
                entry.UseCount = 0;
                entry.LastUsedUtc = null;
            }

            _lastPickedId = null;
            PersistCore();
        }

        RaiseChanged();
    }

    /// <summary>
    ///     按平均线轮换选一条文案，并立刻提交使用计数（<c>use_count + 1</c>、
    ///     <c>last_used_utc = now</c>、<c>last_picked_id = id</c>）后落盘。
    /// </summary>
    /// <returns>选中的模板文本；没有可用条目（例如全部停用）时返回 <see langword="null" />。</returns>
    public string? PickTemplate()
    {
        string? text = null;
        lock (_gate)
        {
            var average = TextLibraryRotation.AverageUseCount(_entries);
            var eligible = TextLibraryRotation.GetEligible(_entries, average);
            var picked = TextLibraryRotation.Pick(eligible, _lastPickedId, RandomNumberGenerator.GetInt32);
            if (picked is null)
            {
                _logger.LogWarning("文案库没有可用文案，本次不展示文案");
                return null;
            }

            picked.UseCount++;
            picked.LastUsedUtc = DateTimeOffset.UtcNow;
            _lastPickedId = picked.Id;
            text = picked.Text;

            // 选中即落盘：崩溃后计数仍然有效，不会让同一条被重复偏爱
            PersistCore();
        }

        RaiseChanged();
        return text;
    }

    /// <summary>启用/停用一条；停用项不参与轮换，也不计入平均线。</summary>
    public void SetEnabled(string id, bool enabled)
    {
        var changed = false;
        lock (_gate)
        {
            var entry = FindCore(id);
            if (entry is null)
            {
                _logger.LogWarning("文案库中不存在 id={Id}，忽略启用状态修改", id);
                return;
            }

            if (entry.Enabled == enabled)
                return;

            entry.Enabled = enabled;
            PersistCore();
            changed = true;
        }

        if (changed)
            RaiseChanged();
    }

    /// <summary>追加一条自定义文案；归一化后仍非法（空、超长、占位符个数不对、重复）时返回 null。</summary>
    public TextLibraryEntry? Add(string template)
    {
        TextLibraryEntry? added = null;
        lock (_gate)
        {
            var normalized = TextLibraryImporter.NormalizeTemplate(template, out _);
            if (!IsUsable(normalized))
            {
                _logger.LogWarning("新增文案被拒绝：模板不符合格式要求");
                return null;
            }

            if (ContainsTextCore(normalized, null))
            {
                _logger.LogWarning("新增文案被拒绝：库中已有相同文案");
                return null;
            }

            added = new TextLibraryEntry { Id = NewId(), Text = normalized };
            _entries.Add(added);
            PersistCore();
        }

        RaiseChanged();
        return added;
    }

    /// <summary>改写一条文案；id 与使用计数保持不变。文本非法或与其它条目重复时返回 false。</summary>
    public bool Update(string id, string template)
    {
        var changed = false;
        lock (_gate)
        {
            var entry = FindCore(id);
            if (entry is null)
            {
                _logger.LogWarning("文案库中不存在 id={Id}，忽略修改", id);
                return false;
            }

            var normalized = TextLibraryImporter.NormalizeTemplate(template, out _);
            if (!IsUsable(normalized))
            {
                _logger.LogWarning("修改文案被拒绝：模板不符合格式要求");
                return false;
            }

            if (ContainsTextCore(normalized, id))
            {
                _logger.LogWarning("修改文案被拒绝：库中已有相同文案");
                return false;
            }

            if (string.Equals(entry.Text, normalized, StringComparison.Ordinal))
                return false;

            entry.Text = normalized;
            PersistCore();
            changed = true;
        }

        if (changed)
            RaiseChanged();
        return changed;
    }

    /// <summary>删除一条；删除的是上次选中项时同时清空 <c>LastPickedId</c>，避免留下悬空引用。</summary>
    public bool Remove(string id)
    {
        var removed = false;
        lock (_gate)
        {
            var index = _entries.FindIndex(entry => string.Equals(entry.Id, id, StringComparison.Ordinal));
            if (index < 0)
            {
                _logger.LogWarning("文案库中不存在 id={Id}，忽略删除", id);
                return false;
            }

            _entries.RemoveAt(index);
            if (string.Equals(_lastPickedId, id, StringComparison.Ordinal))
                _lastPickedId = null;

            PersistCore();
            removed = true;
        }

        if (removed)
            RaiseChanged();
        return removed;
    }

    private void ReloadCore()
    {
        _readOnly = false;

        var exists = File.Exists(FilePath);
        TextLibraryDocument? document = null;
        if (exists)
        {
            try
            {
                var json = SharedFileReader.ReadAllText(FilePath);
                document = JsonSerializer.Deserialize<TextLibraryDocument>(json, JsonOptions);
                if (document is null)
                    throw new JsonException("反序列化结果为空");
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                _logger.LogWarning(ex, "文案库文件无法解析，将重建默认文案：{Path}", FilePath);
                document = null;
            }
        }

        if (document is null)
        {
            LoadDefaultsCore();
            // 首启与损坏重建都立刻落盘：下次启动可直接读取，也便于用户找到文件手工编辑
            PersistCore();
            return;
        }

        if (document.FormatVersion > CurrentFormatVersion)
        {
            // 更高版本可能有本版本不认识的字段，写回会丢数据，因此只读
            _readOnly = true;
            _logger.LogWarning(
                "文案库格式版本 {Version} 高于当前支持的 {Current}，本次运行只读：{Path}",
                document.FormatVersion, CurrentFormatVersion, FilePath);
        }

        _entries = NormalizeLoadedEntries(document.Texts);
        _lastPickedId = ResolveLastPickedId(document.LastPickedId);
    }

    /// <summary>
    ///     整理从磁盘读到的条目：补齐/去重 id（id 是稳定键，缺了会让计数挂到错误的条目上）、
    ///     丢弃空文本、把负数计数归零。
    /// </summary>
    private List<TextLibraryEntry> NormalizeLoadedEntries(IEnumerable<TextLibraryEntry>? loaded)
    {
        var entries = new List<TextLibraryEntry>();
        if (loaded is null)
            return entries;

        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in loaded)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Text))
                continue;

            var id = entry.Id;
            if (string.IsNullOrWhiteSpace(id) || !usedIds.Add(id))
            {
                id = NewId();
                usedIds.Add(id);
            }

            if (entry.UseCount < 0)
                entry.UseCount = 0;
            entry.Id = id;
            entries.Add(entry);
        }

        return entries;
    }

    private string? ResolveLastPickedId(string? lastPickedId)
    {
        if (string.IsNullOrWhiteSpace(lastPickedId))
            return null;

        return _entries.Any(entry => string.Equals(entry.Id, lastPickedId, StringComparison.Ordinal))
            ? lastPickedId
            : null;
    }

    private void LoadDefaultsCore()
    {
        _entries = BuildEntries(DefaultTextLibrary.Templates, out _);
        _lastPickedId = null;
    }

    private List<TextLibraryEntry> BuildEntries(IEnumerable<string?> templates, out int skipped)
    {
        skipped = 0;
        var entries = new List<TextLibraryEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var template in templates)
        {
            var normalized = TextLibraryImporter.NormalizeTemplate(template, out _);
            if (!IsUsable(normalized) || !seen.Add(normalized))
            {
                skipped++;
                continue;
            }

            entries.Add(new TextLibraryEntry { Id = NewId(), Text = normalized });
        }

        return entries;
    }

    private static bool IsUsable(string normalized)
        => normalized.Length is > 0 and <= TextLibraryImporter.MaxTextLength
           && FullscreenTextTemplate.HasSinglePlaceholder(normalized);

    private bool ContainsTextCore(string text, string? exceptId)
    {
        foreach (var entry in _entries)
        {
            if (string.Equals(entry.Id, exceptId, StringComparison.Ordinal))
                continue;

            if (string.Equals(entry.Text, text, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private TextLibraryEntry? FindCore(string id)
        => _entries.Find(entry => string.Equals(entry.Id, id, StringComparison.Ordinal));

    private void PersistCore()
    {
        if (_readOnly)
        {
            _logger.LogWarning("文案库处于只读模式（格式版本较高），跳过写入：{Path}", FilePath);
            return;
        }

        var temporaryPath = $"{FilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var document = new TextLibraryDocument
            {
                FormatVersion = CurrentFormatVersion,
                Texts = _entries,
                LastPickedId = _lastPickedId
            };

            // 原子替换：先写临时文件再 Move，避免中途崩溃留下半截 JSON
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions));
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogWarning(ex, "文案库保存失败（本次改动仅在内存中生效）：{Path}", FilePath);
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "清理临时文件失败：{Path}", path);
        }
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
