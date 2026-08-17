using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Draw;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.PluginSdk;
using SecRandom.Services.Linkage;
using SecRandom.Services.Notification;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Services.Draw;

/// <summary>
/// Implements the host-neutral external student draw boundary.
/// Transport-specific integrations, such as the SecAgent plugin, call this service.
/// </summary>
public sealed class ExternalStudentDrawService(
    IProfileService profileService,
    MainConfigHandler configHandler,
    IDrawTemporaryRecordService temporaryRecordService,
    DrawEngine drawEngine,
    LinkageDrawCoordinator linkageDrawCoordinator,
    NotificationService notificationService) : IExternalStudentDrawService
{
    public async Task<ExternalStudentDrawResult> DrawAsync(
        ExternalStudentDrawRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Mode is not ("flash" or "result_only"))
            throw new ArgumentException("mode must be flash or result_only.", nameof(request));

        var requestedCount = Math.Clamp(request.Count, 1, 100);
        if (request.Mode == "flash")
            requestedCount = 1;

        var gender = request.Gender?.Trim() ?? string.Empty;
        var listName = profileService.CurrentStudentList?.Name ?? string.Empty;
        var temporaryCounts = temporaryRecordService.GetStudentCounts(listName, gender, string.Empty);
        var hasMatchingStudents = (profileService.CurrentStudentList?.Students ?? [])
            .Any(student => student.IsCandidate && Matches(student, gender, request));
        var mayResetExhaustedRound = configHandler.Data.QuickDrawSettings.DrawMode != DrawMode.Repeat
                                     && hasMatchingStudents;

        var result = await InvokeAuthorizedAsync(
            SecurityOperation.QuickDrawStart,
            async () =>
            {
                DrawResult<Student> DrawFromRemainingStudents()
                    => drawEngine.DrawStudent(
                        requestedCount,
                        student => Matches(student, gender, request)
                                      && !HasReachedTemporaryLimit(student, temporaryCounts),
                        DrawSettingsType.QuickDraw,
                        linkageDrawCoordinator.GetCourseName());

                var draw = DrawFromRemainingStudents();
                if (!draw.IsSuccess
                    && mayResetExhaustedRound
                    && draw.Status == DrawStatus.RepeatLimitExhausted)
                {
                    temporaryRecordService.ResetStudentList(listName);
                    temporaryCounts = temporaryRecordService.GetStudentCounts(listName, gender, string.Empty);
                    draw = DrawFromRemainingStudents();
                }

                if (!draw.IsSuccess || draw.Result.Count == 0)
                    return draw;

                profileService.RecordStudentHistory(
                    draw.Result,
                    DateTime.Now,
                    requestedCount,
                    drawMethod: (int)configHandler.Data.QuickDrawSettings.DrawType,
                    courseName: linkageDrawCoordinator.GetCourseName());
                temporaryRecordService.RecordStudents(listName, gender, string.Empty, draw.Result);
                if (request.Mode == "flash")
                    notificationService.QueueStudents(
                        NotificationSettingsType.QuickDraw,
                        linkageDrawCoordinator.GetCourseName(),
                        draw.Result);
                return draw;
            },
            cancellationToken).ConfigureAwait(false);

        return new ExternalStudentDrawResult(
            request.Mode,
            result.Status.ToString(),
            listName,
            result.Result);
    }

    private async Task<DrawResult<Student>> InvokeAuthorizedAsync(
        SecurityOperation operation,
        Func<Task<DrawResult<Student>>> action,
        CancellationToken cancellationToken)
    {
        DrawResult<Student>? result = null;
        var authorized = await linkageDrawCoordinator.AuthorizeAsync(
            operation,
            async () => result = await action().ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
        return authorized && result is not null
            ? result
            : new DrawResult<Student> { Status = DrawStatus.Failure };
    }

    private bool HasReachedTemporaryLimit(
        Student student,
        IReadOnlyDictionary<string, int> temporaryCounts)
    {
        var settings = configHandler.Data.QuickDrawSettings;
        var threshold = settings.DrawMode switch
        {
            DrawMode.Repeat => 0,
            DrawMode.NoRepeat => 1,
            DrawMode.HalfRepeat => Math.Max(1, settings.HalfRepeat),
            _ => 1
        };
        return threshold > 0
               && temporaryCounts.GetValueOrDefault(ProfileRecordIdentity.EnsureRecordId(student)) >= threshold;
    }

    private static bool Matches(
        Student student,
        string gender,
        ExternalStudentDrawRequest request)
    {
        var tags = student.Tags.Split(
            [',', ';', ' '],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (string.IsNullOrWhiteSpace(gender)
                || string.Equals(student.Gender, gender, StringComparison.OrdinalIgnoreCase))
               && request.IncludeTags.All(tag => tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
               && request.ExcludeTags.All(tag => !tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
               && (request.IncludeIds.Count == 0
                   || request.IncludeIds.Contains(student.Id, StringComparer.OrdinalIgnoreCase))
               && (request.IncludeNames.Count == 0
                   || request.IncludeNames.Contains(student.Name, StringComparer.OrdinalIgnoreCase));
    }
}
