using Avalonia.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services.Presentation;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Helpers;

/// <summary>
///     Lets plugins take over the presentation of a draw. The host offers every finished draw to the
///     registered <see cref="IResultPresentationService" /> first; when a plugin accepts the request the
///     built-in draw animation — and, if the plugin asks for it, the built-in result visuals — are skipped.
/// </summary>
internal static class ResultPresentationBridge
{
    /// <summary>
    ///     Offers <paramref name="request" /> to the plugin presenters. Returns true when a plugin took over,
    ///     in which case the caller must not run its own animation.
    /// </summary>
    public static async Task<bool> TryPresentAsync(DrawPresentationRequest request, Control? hostResult = null,
        CancellationToken cancellationToken = default)
    {
        var service = IAppHost.TryGetService<IResultPresentationService>();
        if (service is null)
            return false;

        DrawPresentationDecision decision;
        try
        {
            decision = await service.PresentAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // A plugin presentation must never break the draw it decorates.
            return false;
        }

        if (!decision.Handled)
            return false;

        if (decision.HideHostResult && hostResult is not null)
            hostResult.IsVisible = false;

        return true;
    }

    /// <summary>Builds a request; every collection defaults to empty so callers only pass what they have.</summary>
    public static DrawPresentationRequest BuildRequest(DrawPresentationChannel channel, DrawPresentationPhase phase,
        IReadOnlyList<Student>? students = null, IReadOnlyList<Prize>? prizes = null,
        IReadOnlyList<Student>? assignedStudents = null, string listName = "", string prizeListName = "",
        string groupScope = "", string genderScope = "", int requestedCount = 0, string pluginAnimationId = "",
        IReadOnlyList<string>? displayTitles = null)
    {
        return new DrawPresentationRequest
        {
            Channel = channel,
            Phase = phase,
            Students = students ?? [],
            Prizes = prizes ?? [],
            AssignedStudents = assignedStudents ?? [],
            DisplayTitles = displayTitles ?? [],
            ListName = listName,
            PrizeListName = prizeListName,
            GroupScope = groupScope,
            GenderScope = genderScope,
            RequestedCount = requestedCount,
            PluginAnimationId = pluginAnimationId
        };
    }

    /// <summary>Restores the built-in result visuals after a plugin hid them.</summary>
    public static void RestoreHostResult(Control? hostResult)
    {
        if (hostResult is { IsVisible: false })
            hostResult.IsVisible = true;
    }
}
