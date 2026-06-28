using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Plugins;
using SecRandom.Core.Services.Draw;

namespace SecRandom.Services.Plugins;

public sealed class PluginDrawInvoker(string pluginId, ILogger logger) : IPluginDrawInvoker
{
    public Task<PluginDrawResult> DrawStudentsAsync(PluginStudentDrawRequest request)
    {
        var engine = new DrawEngine();
        var result = engine.DrawStudent(Math.Max(1, request.Count), student => MatchesTags(student.Tags, request));
        logger.LogInformation(
            "Plugin draw invoked: plugin={PluginId}, type=student, count={Count}, includeTags={IncludeTags}, excludeTags={ExcludeTags}, status={Status}, resultCount={ResultCount}.",
            pluginId,
            request.Count,
            string.Join(",", request.IncludeTags),
            string.Join(",", request.ExcludeTags),
            result.Status,
            result.Result.Count);

        return Task.FromResult(new PluginDrawResult
        {
            Status = result.Status.ToString(),
            ResultCount = result.Result.Count
        });
    }

    public Task<PluginDrawResult> DrawPrizesAsync(PluginPrizeDrawRequest request)
    {
        var engine = new DrawEngine();
        var result = engine.DrawPrize(Math.Max(1, request.Count), _ => true);
        logger.LogInformation(
            "Plugin draw invoked: plugin={PluginId}, type=prize, count={Count}, status={Status}, resultCount={ResultCount}.",
            pluginId,
            request.Count,
            result.Status,
            result.Result.Count);

        return Task.FromResult(new PluginDrawResult
        {
            Status = result.Status.ToString(),
            ResultCount = result.Result.Count
        });
    }

    private static bool MatchesTags(string tags, PluginStudentDrawRequest request)
    {
        var tagSet = tags.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return request.IncludeTags.All(tagSet.Contains) && !request.ExcludeTags.Any(tagSet.Contains);
    }
}
