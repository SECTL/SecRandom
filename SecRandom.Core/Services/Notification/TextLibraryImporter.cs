using System.Text;
using System.Text.Json;
using SecRandom.Core.Models.Notification;

namespace SecRandom.Core.Services.Notification;

/// <summary>
///     一次文案导入的结果。部分行非法时 <paramref name="Success" /> 仍可为真：
///     <paramref name="Templates" /> 是合法行，<paramref name="Errors" /> 带 1 起的行号，
///     调用方据此决定"导入"按钮是否可用，并把行级原因显示给用户。
/// </summary>
public sealed record TextLibraryImportResult(
    bool Success,
    IReadOnlyList<string> Templates,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

/// <summary>
///     文案模板的归一化与三种导入来源（JSON / 文案清单 / 表格单列）的解析校验。
/// </summary>
/// <remarks>
///     <para>
///         校验规则由产品需求钉死：空文本与超 200 字符拒绝、占位符出现 ≥2 次拒绝、
///         出现 0 次只警告并把 <c>{result}</c> 补到末尾、重复文本去重。所有上限都是
///         "先拒绝、不截断"——截断会悄悄改掉用户写的话。
///     </para>
///     <para>
///         纯静态、无 IO、无依赖，UI 层可以直接在后台线程解析大段粘贴内容，
///         与 <c>RosterImportParser</c> 的定位一致。
///     </para>
/// </remarks>
public static class TextLibraryImporter
{
    /// <summary>JSON 一次性粘贴的字符上限（需求方给的字面值，产品源码里此前不存在）。</summary>
    public const int MaxJsonImportChars = 14121;

    /// <summary>单条模板的字符上限，作用于归一化后的最终文本。</summary>
    public const int MaxTextLength = 200;

    /// <summary>单次导入的条目上限，防止一份巨型文件把库撑到不可用。</summary>
    public const int MaxEntryCount = 500;

    private static readonly JsonDocumentOptions JsonParseOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    ///     归一化单条模板：去首尾空白、把连续空白合并成一个空格；缺占位符时补到末尾。
    /// </summary>
    /// <param name="raw">原始文本，可为 <see langword="null" />。</param>
    /// <param name="appendedPlaceholder">是否因为缺少占位符而自动补上了 <c>{result}</c>。</param>
    /// <returns>
    ///     归一化后的模板；输入为 <see langword="null" /> 或全是空白时返回空串（调用方按"文案为空"处理）。
    ///     长度、占位符个数与去重都不在这里判断：它们需要行号上下文，由导入入口统一处理。
    /// </returns>
    public static string NormalizeTemplate(string? raw, out bool appendedPlaceholder)
    {
        appendedPlaceholder = false;
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var normalized = CollapseWhitespace(raw);
        if (normalized.Length == 0)
            return string.Empty;

        if (!normalized.Contains(FullscreenTextTemplate.ResultPlaceholder, StringComparison.Ordinal))
        {
            normalized = $"{normalized} {FullscreenTextTemplate.ResultPlaceholder}";
            appendedPlaceholder = true;
        }

        return normalized;
    }

    /// <summary>
    ///     解析一次性粘贴的 JSON：可以是字符串数组，也可以是 <c>{"texts": [...]}</c> 对象，
    ///     其中每个元素可以是字符串或带 <c>text</c> 字段的对象。
    /// </summary>
    public static TextLibraryImportResult ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Fail("内容为空");

        // 先按字符数挡在最前面：超长文本不进入解析器，避免一次粘贴冻结 UI
        if (json.Length > MaxJsonImportChars)
            return Fail($"内容超过 {MaxJsonImportChars} 个字符上限");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, JsonParseOptions);
        }
        catch (JsonException ex)
        {
            // JsonException 的行列从 0 起，用户看到的是 1 起
            var line = (ex.LineNumber ?? 0) + 1;
            var column = (ex.BytePositionInLine ?? 0) + 1;
            return Fail($"JSON 格式错误：第 {line} 行第 {column} 列附近无法解析");
        }

        using (document)
        {
            var root = document.RootElement;
            return root.ValueKind switch
            {
                JsonValueKind.Array => FromJsonArray(root),
                JsonValueKind.Object => FromJsonObject(root),
                _ => Fail("JSON 顶层必须是字符串数组，或含 texts 数组的对象")
            };
        }
    }

    /// <summary>
    ///     从一组模板文本导入。空白行按错误上报（这是程序给出的清单，不是用户表格里可以留空的行）。
    /// </summary>
    public static TextLibraryImportResult FromTemplates(IEnumerable<string?> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);
        return Collect(templates.Select(static template => new RawLine(template)), skipBlankLines: false);
    }

    /// <summary>
    ///     从表格的某一列导入：每个单元格一行文本，空白单元格跳过（不是错误）。
    /// </summary>
    public static TextLibraryImportResult FromCells(IEnumerable<string?> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        return Collect(cells.Select(static cell => new RawLine(cell)), skipBlankLines: true);
    }

    private static TextLibraryImportResult FromJsonArray(JsonElement array)
    {
        if (array.GetArrayLength() > MaxEntryCount)
            return Fail($"文案条数超过 {MaxEntryCount} 条上限");

        return Collect(EnumerateJsonLines(array), skipBlankLines: false);
    }

    private static TextLibraryImportResult FromJsonObject(JsonElement root)
    {
        if (!TryGetProperty(root, "texts", out var texts))
            return Fail("JSON 对象缺少 texts 数组");
        if (texts.ValueKind != JsonValueKind.Array)
            return Fail("JSON 对象的 texts 字段必须是数组");
        if (texts.GetArrayLength() > MaxEntryCount)
            return Fail($"文案条数超过 {MaxEntryCount} 条上限");

        var result = Collect(EnumerateJsonLines(texts), skipBlankLines: false);

        // format_version 只用于"新版写、旧版读"的提示；未知版本仍按当前结构尽力读取，
        // 不拒绝用户的粘贴内容（拒绝只会让人无从下手）。
        if (TryGetProperty(root, "format_version", out var version)
            && version.ValueKind == JsonValueKind.Number
            && version.TryGetInt32(out var versionNumber)
            && versionNumber > 1)
        {
            var warning = $"内容格式版本为 {versionNumber}，已按当前版本读取";
            result = result with { Warnings = [.. result.Warnings, warning] };
        }

        return result;
    }

    private static IEnumerable<RawLine> EnumerateJsonLines(JsonElement array)
    {
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                yield return new RawLine(element.GetString());
                continue;
            }

            if (element.ValueKind == JsonValueKind.Object)
            {
                if (!TryGetProperty(element, "text", out var text))
                {
                    yield return new RawLine(null, "缺少 text 字段");
                    continue;
                }

                if (text.ValueKind != JsonValueKind.String)
                {
                    yield return new RawLine(null, "text 字段必须是字符串");
                    continue;
                }

                yield return new RawLine(text.GetString());
                continue;
            }

            yield return new RawLine(null, "应为字符串或含 text 字段的对象");
        }
    }

    private static TextLibraryImportResult Collect(IEnumerable<RawLine> lines, bool skipBlankLines)
    {
        var templates = new List<string>();
        var errors = new List<string>();
        var warnings = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var numberOfLine = 0;
        foreach (var line in lines)
        {
            if (line.ShapeError is { Length: > 0 } shapeError)
            {
                errors.Add($"第 {numberOfLine + 1} 条：{shapeError}");
                numberOfLine++;
                continue;
            }

            // 表格里的空白行不算一条文案，也不占条目上限
            if (skipBlankLines && string.IsNullOrWhiteSpace(line.Text))
            {
                numberOfLine++;
                continue;
            }

            numberOfLine++;
            if (templates.Count >= MaxEntryCount)
                return Fail($"文案条数超过 {MaxEntryCount} 条上限");

            var template = NormalizeTemplate(line.Text, out var appendedPlaceholder);
            if (template.Length == 0)
            {
                errors.Add($"第 {numberOfLine} 条：文案为空");
                continue;
            }

            // 上限作用于最终文本：补占位符后超限同样拒绝，因为落盘/渲染的就是最终文本
            if (template.Length > MaxTextLength)
            {
                errors.Add($"第 {numberOfLine} 条：文案超过 {MaxTextLength} 个字符上限");
                continue;
            }

            if (CountPlaceholders(template) != 1)
            {
                errors.Add($"第 {numberOfLine} 条：占位符 {FullscreenTextTemplate.ResultPlaceholder} 只能出现一次");
                continue;
            }

            if (!seen.Add(template))
            {
                warnings.Add($"第 {numberOfLine} 条：与前面的文案重复，已跳过");
                continue;
            }

            if (appendedPlaceholder)
                warnings.Add($"第 {numberOfLine} 条：缺少占位符 {FullscreenTextTemplate.ResultPlaceholder}，已自动补到末尾");

            templates.Add(template);
        }

        if (templates.Count == 0)
        {
            if (errors.Count == 0)
                errors.Add("没有可导入的文案");
            return new TextLibraryImportResult(false, templates, errors, warnings);
        }

        return new TextLibraryImportResult(true, templates, errors, warnings);
    }

    private static int CountPlaceholders(string text)
    {
        var count = 0;
        var index = 0;
        while (index < text.Length)
        {
            var hit = text.IndexOf(FullscreenTextTemplate.ResultPlaceholder, index, StringComparison.Ordinal);
            if (hit < 0)
                break;

            count++;
            index = hit + FullscreenTextTemplate.ResultPlaceholder.Length;
        }

        return count;
    }

    /// <summary>
    ///     折叠空白：首尾去掉、中间的每一段连续空白换成一个半角空格。
    ///     用 <see cref="char.IsWhiteSpace(char)" /> 而不是正则，全角空格 U+3000 与制表符一并覆盖。
    /// </summary>
    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                // 首部的空白直接丢弃；中间的空白先挂起，等下一个可见字符再落一个空格
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        // 手写粘贴的 JSON 大小写不统一，按 OrdinalIgnoreCase 容忍 texts/Texts、text/Text
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static TextLibraryImportResult Fail(string error)
        => new(false, Array.Empty<string>(), [error], Array.Empty<string>());

    private readonly record struct RawLine(string? Text, string? ShapeError = null);
}
