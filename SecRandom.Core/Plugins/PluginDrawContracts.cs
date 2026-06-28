namespace SecRandom.Core.Plugins;

public sealed class PluginStudentDrawRequest
{
    public int Count { get; init; } = 1;
    public IReadOnlyList<string> IncludeTags { get; init; } = [];
    public IReadOnlyList<string> ExcludeTags { get; init; } = [];
}

public sealed class PluginPrizeDrawRequest
{
    public int Count { get; init; } = 1;
}

public sealed class PluginDrawResult
{
    public required string Status { get; init; }
    public int ResultCount { get; init; }
}

public interface IPluginDrawInvoker
{
    Task<PluginDrawResult> DrawStudentsAsync(PluginStudentDrawRequest request);
    Task<PluginDrawResult> DrawPrizesAsync(PluginPrizeDrawRequest request);
}
