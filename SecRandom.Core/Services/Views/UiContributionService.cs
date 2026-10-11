using Avalonia.Controls;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services.Views;

namespace SecRandom.Core.Services.Views;

/// <summary>
///     Default <see cref="IUiContributionService" />. Contributions come from DI (registered by plugins during
///     <c>PluginBase.Initialize</c>) and may additionally be added or removed at runtime, for example when the
///     user flips a switch in the plugin's settings page.
/// </summary>
public sealed class UiContributionService : IUiContributionService
{
    private readonly List<IUiContentContribution> _contributions = [];
    private readonly object _gate = new();
    private readonly ILogger<UiContributionService>? _logger;

    public UiContributionService(IEnumerable<IUiContentContribution>? contributions = null,
        ILogger<UiContributionService>? logger = null)
    {
        _logger = logger;

        if (contributions is null)
            return;

        foreach (var contribution in contributions)
        {
            _contributions.RemoveAll(existing => existing.Id == contribution.Id);
            _contributions.Add(contribution);
        }
    }

    public IReadOnlyList<IUiContentContribution> Contributions
    {
        get
        {
            lock (_gate)
            {
                return _contributions.ToArray();
            }
        }
    }

    public IReadOnlyList<string> SlotIds => HostUiSlots.All;

    public event EventHandler? Changed;

    public void Add(IUiContentContribution contribution)
    {
        lock (_gate)
        {
            _contributions.RemoveAll(existing => existing.Id == contribution.Id);
            _contributions.Add(contribution);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Remove(string id)
    {
        var removed = false;
        lock (_gate)
        {
            removed = _contributions.RemoveAll(contribution => contribution.Id == id) > 0;
        }

        if (removed)
            Changed?.Invoke(this, EventArgs.Empty);

        return removed;
    }

    public IReadOnlyList<IUiContentContribution> GetSlotContributions(string slotId)
    {
        if (string.IsNullOrWhiteSpace(slotId))
            return [];

        IUiContentContribution[] snapshot;
        lock (_gate)
        {
            snapshot = _contributions
                .Where(contribution => string.Equals(contribution.SlotId, slotId, StringComparison.Ordinal))
                .OrderByDescending(contribution => contribution.Priority)
                .ToArray();
        }

        return snapshot.Where(IsEnabled).ToArray();
    }

    public bool IsSlotHidden(string slotId)
    {
        return GetSlotContributions(slotId)
            .Any(contribution => contribution.Kind == UiSlotKind.Hide);
    }

    public Control? BuildReplacement(string slotId, UiSlotContext context)
    {
        foreach (var contribution in GetSlotContributions(slotId)
                     .Where(contribution => contribution.Kind == UiSlotKind.Replace))
        {
            var content = Build(contribution, context);
            if (content is not null)
                return content;
        }

        return null;
    }

    private Control? Build(IUiContentContribution contribution, UiSlotContext context)
    {
        try
        {
            return contribution.CreateContent(context);
        }
        catch (Exception exception)
        {
            // A broken contribution must only cost its own slot.
            _logger?.LogWarning(exception, "插件界面贡献 {Contribution} 构建失败。", contribution.Id);
            return null;
        }
    }

    private bool IsEnabled(IUiContentContribution contribution)
    {
        try
        {
            return contribution.IsEnabled;
        }
        catch
        {
            return false;
        }
    }
}
