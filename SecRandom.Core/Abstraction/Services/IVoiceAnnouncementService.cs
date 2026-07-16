using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Abstraction.Services;

public interface IVoiceAnnouncementService
{
    Task<IReadOnlyList<VoiceOption>> GetVoicesAsync(int engine, CancellationToken cancellationToken = default);

    Task SpeakAsync(string text, bool waitForCompletion = false, CancellationToken cancellationToken = default);

    Task PreviewAsync(string text, CancellationToken cancellationToken = default);

    Task SpeakStudentsAsync(
        IEnumerable<Student> students,
        bool waitForCompletion = false,
        CancellationToken cancellationToken = default);

    Task SpeakPrizesAsync(
        IEnumerable<Prize> prizes,
        bool waitForCompletion = false,
        CancellationToken cancellationToken = default);
}
