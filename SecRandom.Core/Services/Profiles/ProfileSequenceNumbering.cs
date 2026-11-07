using System.Globalization;

namespace SecRandom.Core.Services.Profiles;

/// <summary>
///     新增单个成员时，显示编号（<c>Student.Id</c> / <c>Prize.Id</c>）与现有成员冲突的判定结果。
/// </summary>
/// <remarks>
///     编号只是展示用元数据，成员身份始终由 <c>RecordId</c> 承担，因此顺延编号不会影响
///     抽取历史、公平权重或已签发的证明。
/// </remarks>
public enum SequenceNumberConflictKind
{
    /// <summary>没有成员使用该编号（或新编号为空），可以直接新增。</summary>
    None,

    /// <summary>只有一名成员使用该编号且它是纯数字，可以把该编号及之后的数字编号整体 +1。</summary>
    Shiftable,

    /// <summary>编号已被占用，但新编号不是纯数字，无法自动顺延。</summary>
    NotNumeric,

    /// <summary>名单里已经有多名成员使用同一编号，自动顺延无法消除重复。</summary>
    AlreadyDuplicated
}

/// <summary>一名成员的编号顺延计划项；<see cref="Index" /> 指向原集合下标。</summary>
public sealed record SequenceNumberShiftEntry(int Index, string OldId, long OldNumber, long NewNumber);

/// <summary>新增成员时的编号冲突分析结果，供页面决定展示哪种确认弹窗。</summary>
public sealed class SequenceNumberConflict
{
    public SequenceNumberConflictKind Kind { get; init; } = SequenceNumberConflictKind.None;

    /// <summary>去掉首尾空白后的新编号。</summary>
    public string NewId { get; init; } = string.Empty;

    /// <summary>名单中已使用该编号的成员下标（<see cref="SequenceNumberConflictKind.None" /> 时为空）。</summary>
    public IReadOnlyList<int> DuplicateIndices { get; init; } = [];

    /// <summary>纯数字且数值不小于新编号的成员及顺延结果，按原集合顺序。</summary>
    public IReadOnlyList<SequenceNumberShiftEntry> ShiftEntries { get; init; } = [];

    /// <summary>新成员插入位置：被撞成员在原集合中的下标。</summary>
    public int InsertIndex { get; init; }

    /// <summary>追加到末尾时可用的编号（现有最大数字 + 1）；没有数字编号或已到上限时为 <c>null</c>。</summary>
    public long? AppendNumber { get; init; }
}

/// <summary>
///     单个成员新增时的编号规则，纯逻辑且无副作用：新增成员填写了与现有成员相同的编号时，
///     由页面询问用户“顺延并插入”还是“追加到末尾”。
/// </summary>
/// <remarks>
///     比较使用去掉首尾空白后的 <see cref="StringComparison.Ordinal" />；顺延只处理纯 ASCII 十进制数字，
///     非数字编号（如 <c>A15</c>、<c>初一(3)班-07</c>）保持原样，因此不会破坏这类编号的格式。
/// </remarks>
public static class ProfileSequenceNumbering
{
    /// <summary>把编号解析成数字；只接受去掉首尾空白后的纯 ASCII 十进制数字。</summary>
    public static long? TryParseNumber(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var text = id.Trim();
        foreach (var character in text)
        {
            if (character is < '0' or > '9')
                return null;
        }

        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>分析新编号与现有编号的冲突。</summary>
    public static SequenceNumberConflict Analyze(IReadOnlyList<string>? existingIds, string? newId)
    {
        var trimmedNewId = newId?.Trim() ?? string.Empty;
        if (existingIds is null || existingIds.Count == 0 || trimmedNewId.Length == 0)
            return new SequenceNumberConflict { Kind = SequenceNumberConflictKind.None, NewId = trimmedNewId };

        var duplicates = new List<int>();
        var numbers = new long?[existingIds.Count];
        long? maximumNumber = null;

        for (var index = 0; index < existingIds.Count; index++)
        {
            var existingId = existingIds[index]?.Trim() ?? string.Empty;
            if (existingId.Length > 0 && string.Equals(existingId, trimmedNewId, StringComparison.Ordinal))
                duplicates.Add(index);

            var number = TryParseNumber(existingId);
            numbers[index] = number;
            if (number is { } value && (maximumNumber is null || value > maximumNumber))
                maximumNumber = value;
        }

        var appendNumber = maximumNumber is { } maximum && maximum < long.MaxValue ? maximum + 1 : (long?)null;

        if (duplicates.Count == 0)
            return new SequenceNumberConflict
            {
                Kind = SequenceNumberConflictKind.None,
                NewId = trimmedNewId,
                AppendNumber = appendNumber
            };

        if (duplicates.Count > 1)
            return new SequenceNumberConflict
            {
                Kind = SequenceNumberConflictKind.AlreadyDuplicated,
                NewId = trimmedNewId,
                DuplicateIndices = duplicates,
                AppendNumber = appendNumber
            };

        if (TryParseNumber(trimmedNewId) is not { } newNumber)
            return new SequenceNumberConflict
            {
                Kind = SequenceNumberConflictKind.NotNumeric,
                NewId = trimmedNewId,
                DuplicateIndices = duplicates,
                AppendNumber = appendNumber
            };

        var shiftEntries = new List<SequenceNumberShiftEntry>();
        for (var index = 0; index < existingIds.Count; index++)
        {
            if (numbers[index] is not { } existingNumber || existingNumber < newNumber)
                continue;

            // 编号已经到达 long 上限时无法再 +1，按“不可自动顺延”处理，避免写出重复编号。
            if (existingNumber == long.MaxValue)
                return new SequenceNumberConflict
                {
                    Kind = SequenceNumberConflictKind.NotNumeric,
                    NewId = trimmedNewId,
                    DuplicateIndices = duplicates,
                    AppendNumber = appendNumber
                };

            shiftEntries.Add(new SequenceNumberShiftEntry(index, existingIds[index]?.Trim() ?? string.Empty,
                existingNumber, existingNumber + 1));
        }

        return new SequenceNumberConflict
        {
            Kind = SequenceNumberConflictKind.Shiftable,
            NewId = trimmedNewId,
            DuplicateIndices = duplicates,
            ShiftEntries = shiftEntries,
            InsertIndex = duplicates[0],
            AppendNumber = appendNumber
        };
    }
}
