using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Plugins;
using SecRandom.Core.Services.Draw;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Security;
using SecRandom.Services.Linkage;
using SecRandom.Services;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Services.Plugins;

public sealed class PluginDrawInvoker(
    string pluginId,
    ILogger logger,
    LinkageDrawCoordinator linkageDrawCoordinator,
    IDrawTemporaryRecordService temporaryRecordService,
    MainConfigHandler configHandler,
    FeatureAvailabilityService featureAvailability) : IPluginDrawInvoker
{
    public async Task<PluginDrawResult> DrawStudentsAsync(PluginStudentDrawRequest request)
    {
        PluginDrawResult? response = null;
        await linkageDrawCoordinator.AuthorizeAsync(SecurityOperation.RollCallStart, () =>
        {
            var engine = new DrawEngine();
            var profileService = IAppHost.GetService<IProfileService>();
            var studentListName = profileService.CurrentStudentList?.Name ?? string.Empty;
            var temporaryCounts = temporaryRecordService.GetStudentCounts(studentListName, string.Empty, string.Empty);
            var result = engine.DrawStudent(Math.Max(1, request.Count), student =>
                MatchesTags(student.Tags, request) && !HasReachedStudentRepeatLimit(student, temporaryCounts),
                linkageDrawCoordinator.GetCourseName());
            if (result.IsSuccess && result.Result.Count > 0)
            {
                var courseName = linkageDrawCoordinator.GetCourseName();
                profileService.RecordStudentHistory(
                    result.Result,
                    DateTime.Now,
                    Math.Max(1, request.Count),
                    drawMethod: (int)configHandler.Data.RollCallSettings.DrawType,
                    courseName: courseName);
                temporaryRecordService.RecordStudents(studentListName, string.Empty, string.Empty, result.Result);
            }
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
        if (!featureAvailability.IsLotteryEnabled)
        {
            logger.LogInformation("Plugin draw rejected because lottery is disabled: plugin={PluginId}.", pluginId);
            return new PluginDrawResult { Status = "FeatureDisabled", ResultCount = 0 };
        }

        PluginDrawResult? response = null;
        var disabledDuringAuthorization = false;
        await linkageDrawCoordinator.AuthorizeAsync(SecurityOperation.LotteryStart, () =>
        {
            if (!featureAvailability.IsLotteryEnabled)
            {
                disabledDuringAuthorization = true;
                return Task.CompletedTask;
            }

            var engine = new DrawEngine();
            var profileService = IAppHost.GetService<IProfileService>();
            var prizeListName = profileService.CurrentPrizeList?.Name ?? string.Empty;
            var requestedCount = Math.Max(1, request.Count);
            var result = engine.DrawPrizeWithTemporaryCounts(
                requestedCount,
                _ => true,
                temporaryRecordService.GetPrizeCounts(prizeListName));
            if (result.IsSuccess && result.Result.Count > 0)
            {
                profileService.RecordPrizeHistory(result.Result, DateTime.Now, requestedCount);
                temporaryRecordService.RecordPrizes(prizeListName, result.Result);
            }
            logger.LogInformation(
                "Plugin draw invoked: plugin={PluginId}, type=prize, count={Count}, status={Status}, resultCount={ResultCount}.",
                pluginId, request.Count, result.Status, result.Result.Count);
            response = new PluginDrawResult { Status = result.Status.ToString(), ResultCount = result.Result.Count };
            return Task.CompletedTask;
        });

        if (disabledDuringAuthorization)
            return new PluginDrawResult { Status = "FeatureDisabled", ResultCount = 0 };
        return response ?? new PluginDrawResult { Status = "AuthorizationRequired", ResultCount = 0 };
    }

    private static bool MatchesTags(string tags, PluginStudentDrawRequest request)
    {
        var tagSet = tags.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return request.IncludeTags.All(tagSet.Contains) && !request.ExcludeTags.Any(tagSet.Contains);
    }

    private bool HasReachedStudentRepeatLimit(Student student, IReadOnlyDictionary<string, int> temporaryCounts)
    {
        var settings = configHandler.Data.RollCallSettings;
        var mode = settings.DrawMode;
        var threshold = mode switch
        {
            DrawMode.Repeat => 0,
            DrawMode.NoRepeat => 1,
            DrawMode.HalfRepeat => Math.Max(1, settings.HalfRepeat),
            _ => 1
        };
        return threshold > 0 && temporaryCounts.GetValueOrDefault(ProfileRecordIdentity.EnsureRecordId(student)) >= threshold;
    }
}
