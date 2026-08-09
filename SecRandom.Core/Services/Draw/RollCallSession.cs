using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Draw;
using SecRandom.Core.Services.Config;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Draw;

internal sealed class RollCallSession(
    MainConfigHandler configHandler,
    IProfileService profileService,
    IDrawTemporaryRecordService temporaryRecordService,
    IDrawCommitService drawCommitService,
    DrawEngine drawEngine) : IRollCallSession
{
    public IReadOnlyList<Student> GetEligibleStudents()
    {
        var students = profileService.CurrentStudentList?.Students ?? [];
        var settings = configHandler.Data.RollCallSettings;
        var threshold = DrawRepeatPolicy.ResolveThreshold(settings.DrawMode, settings.HalfRepeat);
        var counts = temporaryRecordService.GetStudentCounts(GetListName(), string.Empty, string.Empty);
        return DrawCandidateFilter.FilterEligibleStudents(students, string.Empty, string.Empty, counts, threshold);
    }

    public DrawResult<Student> DrawOnce()
    {
        var candidates = GetEligibleStudents();
        if (candidates.Count == 0)
            return new DrawResult<Student> { Status = DrawStatus.NoEligibleCandidates };

        var drawType = configHandler.Data.RollCallSettings.DrawType;
        var prepared = drawType == DrawType.Fair
            ? drawEngine.PrepareStudentsForMobileDesktopDefaults(1, candidates, DrawSettingsType.RollCall, DrawType.Fair)
            : null;
        var output = drawType == DrawType.Fair
            ? drawEngine.DrawPreparedStudents(prepared!, 1)
            : drawEngine.DrawPreparedStudentsWithMobileDesktopDefaults(1, candidates, DrawSettingsType.RollCall, DrawType.Random);
        if (!output.IsSuccess || output.Result.Count == 0)
            return output;

        var weights = drawType == DrawType.Fair
            ? prepared!.WeightedCandidates.ToDictionary(candidate => candidate.Candidate, candidate => candidate.Weight)
            : output.Result.ToDictionary(student => student, _ => 1.0);
        drawCommitService.CommitStudentDraw(new StudentDrawCommit(
            output.Result,
            DateTime.Now,
            1,
            GetListName(),
            DrawMethod: (int)drawType,
            Weights: weights));
        return output;
    }

    private string GetListName() => profileService.StudentListConfig?.Name ?? "default";
}
