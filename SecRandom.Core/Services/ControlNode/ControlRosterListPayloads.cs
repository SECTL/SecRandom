using System.Text.Json.Serialization;

namespace SecRandom.Core.Services.ControlNode;

/// <summary>
///     <c>roster.list</c> 的返回：本机有哪些点名名单 / 奖池。
/// </summary>
/// <remarks>
///     <para>
///         只回**名称**，不含成员。操作员发 <c>draw.trigger</c> 时需要一个 <c>list_name</c> 下拉框，
///         而名单成员（学生姓名）仍然只能走 admin 的 <c>roster.read</c>。把两者合成一条命令，
///         就等于为了一个下拉框把整份名单摊给所有操作员。
///     </para>
///     <para>
///         上限 50 份与 <c>roster.read</c> 的 <c>MaxLists</c> 保持一致：这是"选一项"的列表，
///         不是导出通道；超过这个数量，控制台应该用别的方式管理名单。
///     </para>
/// </remarks>
public sealed record ControlRosterListResponse(
    [property: JsonPropertyName("roll_call")] IReadOnlyList<string> RollCall,
    [property: JsonPropertyName("lottery")] IReadOnlyList<string> Lottery,
    [property: JsonPropertyName("default_roll_call")] string? DefaultRollCall,
    [property: JsonPropertyName("default_lottery")] string? DefaultLottery)
{
    /// <summary>列表最多回多少份（点名名单、奖池各自计数）。</summary>
    public const int MaxNames = 50;

    /// <summary>去空白、去重并截断到 <see cref="MaxNames" />，保持原有顺序。</summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string>? names)
    {
        if (names is null) return [];

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var raw in names)
        {
            var name = (raw ?? string.Empty).Trim();
            if (name.Length == 0 || !seen.Add(name)) continue;

            result.Add(name);
            if (result.Count >= MaxNames) break;
        }

        return result;
    }
}