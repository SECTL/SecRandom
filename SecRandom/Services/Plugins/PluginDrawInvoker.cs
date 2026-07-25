using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SecRandom.Core.Abstraction.Services;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Plugins;
using SecRandom.Core.Services.Draw;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Security;
using SecRandom.Services.Linkage;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Services.Plugins;

public sealed class PluginDrawInvoker(
    string pluginId,
    ILogger logger,
    LinkageDrawCoordinator linkageDrawCoordinator,
    IDrawTemporaryRecordService temporaryRecordService,
    MainConfigHandler configHandler,
    DrawEngine drawEngine,
    IProfileService profileService,
    IDrawCommitService drawCommitService,
    IFeatureAvailabilityService featureAvailability) : IPluginDrawInvoker
{
    public async Task<PluginDrawResult> DrawStudentsAsync(PluginStudentDrawRequest request)
    {
        if (IsFormalNotarizationEnabled())
            return RejectFormalNotarizationDraw("student");

        PluginDrawResult? response = null;
        await linkageDrawCoordinator.AuthorizeAsync(SecurityOperation.RollCallStart, () =>
        {
            var studentListName = profileService.CurrentStudentList?.Name ?? string.Empty;
            var courseName = linkageDrawCoordinator.GetCourseName();
            var settings = configHandler.Data.RollCallSettings;
            var threshold = DrawRepeatPolicy.ResolveThreshold(settings.DrawMode, settings.HalfRepeat);
            var temporaryCounts = temporaryRecordService.GetStudentCounts(studentListName, string.Empty, string.Empty);
            // 重复阈值只按临时记录口径过滤一次（与页面一致），不再叠加 DrawEngine 的历史制阈值。
            var eligible = DrawCandidateFilter.FilterEligibleStudents(
                profileService.CurrentStudentList?.Students ?? [],
                string.Empty,
                string.Empty,
                temporaryCounts,
                threshold,
                student => MatchesTags(student.Tags, request));
            var requestedCount = Math.Max(1, request.Count);
            var result = drawEngine.DrawPreparedStudents(requestedCount, eligible, DrawSettingsType.RollCall, courseName);
            if (result.IsSuccess && result.Result.Count > 0)
            {
                drawCommitService.CommitStudentDraw(new StudentDrawCommit(
                    result.Result,
                    DateTime.Now,
                    requestedCount,
                    studentListName,
                    DrawMethod: (int)settings.DrawType,
                    CourseName: courseName));
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
        if (IsFormalNotarizationEnabled())
            return RejectFormalNotarizationDraw("prize");

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

            var prizeListName = profileService.CurrentPrizeList?.Name ?? string.Empty;
            var requestedCount = Math.Max(1, request.Count);
            var result = drawEngine.DrawPrizeWithTemporaryCounts(
                requestedCount,
                _ => true,
                temporaryRecordService.GetPrizeCounts(prizeListName));
            if (result.IsSuccess && result.Result.Count > 0)
            {
                drawCommitService.CommitLotteryDraw(new LotteryDrawCommit(
                    result.Result,
                    DateTime.Now,
                    requestedCount,
                    prizeListName,
                    PrizeDrawMethod: (int)configHandler.Data.LotterySettings.DrawType));
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

    private bool IsFormalNotarizationEnabled() =>
        configHandler.Data.General.Verification.Mode == VerificationMode.FormalNotarized;

    private PluginDrawResult RejectFormalNotarizationDraw(string drawType)
    {
        logger.LogInformation(
            "Plugin draw rejected because formal notarization does not support plugin draws: plugin={PluginId}, type={DrawType}.",
            pluginId,
            drawType);
        return new PluginDrawResult { Status = "FormalNotarizationUnsupported", ResultCount = 0 };
    }
}
