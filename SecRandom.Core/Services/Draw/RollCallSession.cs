using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Draw;
using SecRandom.Core.Services.Config;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Draw;

internal sealed class RollCallSession(
    MainConfigHandler configHandler,
    IProfileService profileService,
    IDrawTemporaryRecordService temporaryRecordService,
    DrawEngine drawEngine) : IRollCallSession
{
    public IReadOnlyList<Student> GetEligibleStudents()
    {
        var students = profileService.CurrentStudentList?.Students.Where(student => student.IsCandidate).ToList() ?? [];
        var threshold = GetRepeatThreshold(configHandler.Data.RollCallSettings.DrawMode, configHandler.Data.RollCallSettings.HalfRepeat);
        if (threshold <= 0)
            return students;

        var counts = temporaryRecordService.GetStudentCounts(GetListName(), string.Empty, string.Empty);
        return students.Where(student => counts.GetValueOrDefault(ProfileRecordIdentity.EnsureRecordId(student)) < threshold).ToArray();
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
        profileService.RecordStudentHistory(output.Result, DateTime.Now, 1, drawMethod: (int)drawType, weights: weights);
        temporaryRecordService.RecordStudents(GetListName(), string.Empty, string.Empty, output.Result);
        return output;
    }

    private string GetListName() => profileService.StudentListConfig?.Name ?? "default";

    private static int GetRepeatThreshold(DrawMode mode, int halfRepeat) => mode switch
    {
        DrawMode.Repeat => 0,
        DrawMode.NoRepeat => 1,
        DrawMode.HalfRepeat => Math.Max(1, halfRepeat),
        _ => 1
    };
}
