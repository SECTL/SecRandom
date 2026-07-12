using System;
using System.Linq;
using System.Threading.Tasks;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Plugins;
using SecRandom.Core.Services.Draw;
using SecRandom.Core.Enums.Configs;
using SecRandom.Services.Security;

namespace SecRandom.Services.Plugins;

public sealed class PluginDrawInvoker(string pluginId, ILogger logger, ISecurityService securityService) : IPluginDrawInvoker
{
    public async Task<PluginDrawResult> DrawStudentsAsync(PluginStudentDrawRequest request)
    {
        PluginDrawResult? response = null;
        await securityService.AuthorizeAsync(SecurityOperation.RollCallStart, () =>
        {
            var engine = new DrawEngine();
            var result = engine.DrawStudent(Math.Max(1, request.Count), student => MatchesTags(student.Tags, request));
            logger.LogInformation(
                "Plugin draw invoked: plugin={PluginId}, type=student, count={Count}, includeTags={IncludeTags}, excludeTags={ExcludeTags}, status={Status}, resultCount={ResultCount}.",
                pluginId, request.Count, string.Join(",", request.IncludeTags), string.Join(",", request.ExcludeTags), result.Status, result.Result.Count);
            response = new PluginDrawResult { Status = result.Status.ToString(), ResultCount = result.Result.Count };
            return Task.CompletedTask;
        });

        return response ?? new PluginDrawResult { Status = "AuthorizationRequired", ResultCount = 0 };
    }

    public async Task<PluginDrawResult> DrawPrizesAsync(PluginPrizeDrawRequest request)
    {
        PluginDrawResult? response = null;
        await securityService.AuthorizeAsync(SecurityOperation.LotteryStart, () =>
        {
            var engine = new DrawEngine();
            var requestedCount = Math.Max(1, request.Count);
            var result = engine.DrawPrize(requestedCount, _ => true);
            if (result.IsSuccess && result.Result.Count > 0)
                IAppHost.GetService<IProfileService>().RecordPrizeHistory(result.Result, DateTime.Now, requestedCount);
            logger.LogInformation(
                "Plugin draw invoked: plugin={PluginId}, type=prize, count={Count}, status={Status}, resultCount={ResultCount}.",
                pluginId, request.Count, result.Status, result.Result.Count);
            response = new PluginDrawResult { Status = result.Status.ToString(), ResultCount = result.Result.Count };
            return Task.CompletedTask;
        });

        return response ?? new PluginDrawResult { Status = "AuthorizationRequired", ResultCount = 0 };
    }

    private static bool MatchesTags(string tags, PluginStudentDrawRequest request)
    {
        var tagSet = tags.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return request.IncludeTags.All(tagSet.Contains) && !request.ExcludeTags.Any(tagSet.Contains);
    }
}
