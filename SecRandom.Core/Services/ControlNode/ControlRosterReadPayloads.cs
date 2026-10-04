using System.Text.Json;
using System.Text.Json.Serialization;
using SecRandom.Shared.Models.ControlNode;

namespace SecRandom.Core.Services.ControlNode;

/// <summary>
///     <c>roster.read</c> 的请求：读这台机器上的名单或奖池。
/// </summary>
/// <remarks>
///     读名单是集控里**最敏感的一条只读通道**（含学生姓名），因此它按 Admin 授权，
///     并且默认**不返回已停用的学生**——停用通常就是"这个人不该再被抽到"，
///     把停用名单整份摊到控制台上没有收益。
/// </remarks>
public sealed record ControlRosterReadRequest(
    [property: JsonPropertyName("roster_kind")] string RosterKind,
    [property: JsonPropertyName("list_name")] string? ListName,
    [property: JsonPropertyName("include_disabled")] bool IncludeDisabled)
{
    public const string Students = "students";
    public const string Prizes = "prizes";

    /// <summary>一次最多返回多少份名单/奖池（控制台要的是"有哪些"，不是全量导出）。</summary>
    public const int MaxLists = 50;

    /// <summary>
    ///     每份名单最多返回多少人。单帧上限 64 KiB，超出后按 <c>truncated</c> 标记截断。
    /// </summary>
    public const int MaxMembersPerList = 500;

    public static bool TryParse(JsonElement? payload, out ControlRosterReadRequest? request, out string reason)
    {
        request = null;
        reason = ControlRejectReasons.InvalidCommand;

        var kind = Students;
        string? listName = null;
        var includeDisabled = false;

        if (payload is { ValueKind: JsonValueKind.Object } root)
        {
            if (root.TryGetProperty("roster_kind", out var kindElement) && kindElement.ValueKind == JsonValueKind.String)
                kind = (kindElement.GetString() ?? string.Empty).Trim();

            if (root.TryGetProperty("list_name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
            {
                var candidate = (nameElement.GetString() ?? string.Empty).Trim();
                listName = candidate.Length == 0 ? null : candidate;
            }

            includeDisabled = root.TryGetProperty("include_disabled", out var disabledElement)
                              && disabledElement.ValueKind == JsonValueKind.True;
        }
        else if (payload is not null and not { ValueKind: JsonValueKind.Null })
        {
            // 载荷存在但不是对象 → 明确拒绝，而不是"当作没传"。
            return false;
        }

        if (!string.Equals(kind, Students, StringComparison.Ordinal)
            && !string.Equals(kind, Prizes, StringComparison.Ordinal))
        {
            reason = $"unsupported_roster_kind:{kind}";
            return false;
        }

        request = new ControlRosterReadRequest(kind, listName, includeDisabled);
        return true;
    }
}

/// <summary><c>roster.read</c> 的返回：按名单分组的成员。</summary>
public sealed record ControlRosterReadResponse(
    [property: JsonPropertyName("roster_kind")] string RosterKind,
    [property: JsonPropertyName("lists")] IReadOnlyList<ControlRosterListPayload> Lists);

/// <param name="Name">名单/奖池名。</param>
/// <param name="IsDefault">是否是本机当前默认使用的那一份。</param>
/// <param name="Count">返回的成员数。</param>
/// <param name="Total">该名单的实际成员数（截断前）。</param>
/// <param name="Truncated">是否因为单帧上限被截断。</param>
public sealed record ControlRosterListPayload(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("is_default")] bool IsDefault,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("truncated")] bool Truncated,
    [property: JsonPropertyName("members")] IReadOnlyList<ControlRosterMemberPayload> Members);

/// <param name="Id">学号 / 奖品编号（可为空：名单允许只有姓名）。</param>
/// <param name="Count">奖品的数量（学生为 <c>null</c>）。</param>
/// <param name="Weight">奖品的权重（学生为 <c>null</c>）。</param>
public sealed record ControlRosterMemberPayload(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("gender")] string? Gender,
    [property: JsonPropertyName("group")] string? Group,
    [property: JsonPropertyName("count")] int? Count,
    [property: JsonPropertyName("weight")] double? Weight,
    [property: JsonPropertyName("enabled")] bool Enabled);
