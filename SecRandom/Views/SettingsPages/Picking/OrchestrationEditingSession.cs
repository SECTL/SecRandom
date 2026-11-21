using SecRandom.Core.Models.Orchestration;

namespace SecRandom.Views.SettingsPages.Picking;

/// <summary>
///     Which arranged profile and which round the five list settings pages open on.
/// </summary>
/// <remarks>
///     The overview page owns the profile picker; a list page is a hidden page reached only from that overview, so
///     it has no picker of its own and must not guess. Rather than a global "current profile" that could survive a
///     page reload and point at a deleted list, the overview states the selection right before navigating and each
///     list page re-reads the profile from <c>IOrchestrationStore</c> by that id — a stale object never edits.
/// </remarks>
internal static class OrchestrationEditingSession
{
    public const string ExcludePage = "settings.picking.orchestration.exclude";
    public const string WeightPage = "settings.picking.orchestration.weight";
    public const string DeferPage = "settings.picking.orchestration.defer";
    public const string OrderPage = "settings.picking.orchestration.order";
    public const string GuaranteePage = "settings.picking.orchestration.guarantee";

    public static string? ProfileId { get; private set; }

    public static int Round { get; private set; } = 1;

    public static void Open(string profileId, int round)
    {
        ProfileId = profileId;
        Round = Math.Max(1, round);
    }

    public static string PageIdOf(OrchestrationRuleKind kind) => kind switch
    {
        OrchestrationRuleKind.Exclude => ExcludePage,
        OrchestrationRuleKind.Weight => WeightPage,
        OrchestrationRuleKind.Defer => DeferPage,
        OrchestrationRuleKind.Order => OrderPage,
        OrchestrationRuleKind.Guarantee => GuaranteePage,
        _ => ExcludePage
    };
}