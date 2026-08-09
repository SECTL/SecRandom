using System.Collections;
using SecRandom.Core.Views;

namespace SecRandom.Services.ViewEngine;

public sealed class RemainingListViewState
{
    public string Title { get; private set; } = string.Empty;
    public string EmptyText { get; private set; } = string.Empty;
    public IReadOnlyList<object> Items { get; private set; } = [];

    internal void Set(string title, IEnumerable items, string emptyText)
    {
        Title = title;
        EmptyText = emptyText;
        Items = items.Cast<object>().ToArray();
    }
}

public sealed class RemainingListViewService(
    IViewEngine viewEngine,
    IViewHostProvider hostProvider,
    RemainingListViewState state)
{
    internal const string ViewId = "main.remainingList";
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task ShowAsync(
        string title,
        IEnumerable items,
        string emptyText,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(emptyText);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            await viewEngine.CloseAsync(ViewId, cancellationToken: cancellationToken).ConfigureAwait(true);
            state.Set(title, items, emptyText);
            var options = hostProvider is DesktopViewHostProvider
                ? new ViewShowOptions { ActivationPreference = ViewActivationPreference.NewHost }
                : null;
            await viewEngine.ShowAsync(ViewId, options, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
