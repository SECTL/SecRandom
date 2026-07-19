using SecRandom.Core.Models.Draw;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Abstraction.Services;

/// <summary>
/// Host-internal lottery use case without UI, authorization, or notification effects.
/// </summary>
public interface ILotterySession
{
    IReadOnlyList<Prize> GetEligiblePrizes();
    DrawResult<Prize> DrawOnce();
}
