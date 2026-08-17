using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.PluginSdk;

/// <summary>
/// Host-neutral student draw boundary for plugins and other external integrations.
/// The transport and authentication policy belong to the integration that consumes it.
/// </summary>
public sealed record ExternalStudentDrawRequest
{
    public string Mode { get; init; } = "result_only";
    public int Count { get; init; } = 1;
    public string Gender { get; init; } = string.Empty;
    public IReadOnlyList<string> IncludeTags { get; init; } = [];
    public IReadOnlyList<string> ExcludeTags { get; init; } = [];
    public IReadOnlyList<string> IncludeIds { get; init; } = [];
    public IReadOnlyList<string> IncludeNames { get; init; } = [];
}

public sealed record ExternalStudentDrawResult(
    string Mode,
    string Status,
    string Profile,
    IReadOnlyList<Student> Students);

public interface IExternalStudentDrawService
{
    Task<ExternalStudentDrawResult> DrawAsync(
        ExternalStudentDrawRequest request,
        CancellationToken cancellationToken = default);
}
