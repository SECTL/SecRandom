using SecRandom.Core.Models.Draw;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Abstraction.Services;

/// <summary>
/// Host-internal point-call use case without UI, authorization, or notification effects.
/// </summary>
public interface IRollCallSession
{
    IReadOnlyList<Student> GetEligibleStudents();
    DrawResult<Student> DrawOnce();
}
