using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services.Presentation;

namespace SecRandom.Core.Services.Presentation;

/// <summary>
///     Default <see cref="IResultPresentationService" />. Presenters come straight from DI, so a plugin only
///     has to register its presenter during <c>PluginBase.Initialize</c> - no host call, no registry call.
/// </summary>
public sealed class ResultPresentationService : IResultPresentationService
{
    private readonly ILogger<ResultPresentationService>? _logger;
    private readonly IReadOnlyList<IDrawResultPresenter> _presenters;

    public ResultPresentationService(IEnumerable<IDrawResultPresenter> presenters,
        ILogger<ResultPresentationService>? logger = null)
    {
        _logger = logger;
        _presenters = presenters
            .OrderByDescending(presenter => presenter.Priority)
            .ToArray();
    }

    public IReadOnlyList<IDrawResultPresenter> Presenters => _presenters;

    public IDrawResultPresenter? Resolve(DrawPresentationRequest request)
    {
        return _presenters.FirstOrDefault(presenter => CanPresent(presenter, request));
    }

    public async Task<DrawPresentationDecision> PresentAsync(DrawPresentationRequest request,
        CancellationToken cancellationToken = default)
    {
        foreach (var presenter in _presenters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!CanPresent(presenter, request))
                continue;

            try
            {
                var decision = await presenter.PresentAsync(request, cancellationToken).ConfigureAwait(true);
                if (decision.Handled)
                {
                    _logger?.LogDebug("插件结果呈现 {Presenter} 接管了 {Channel} 的 {Phase}。",
                        presenter.Id, request.Channel, request.Phase);
                    return decision;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // A broken presenter must never break the draw result: log it and let the host present.
                _logger?.LogWarning(exception, "插件结果呈现 {Presenter} 抛出异常，已跳过。", presenter.Id);
            }
        }

        return DrawPresentationDecision.NotHandled;
    }

    private static bool CanPresent(IDrawResultPresenter presenter, DrawPresentationRequest request)
    {
        if (!IsSelected(presenter, request))
            return false;

        try
        {
            return presenter.CanPresent(request);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     用户在"动画样式"里选了内置动画时，插件不再接管；选了某个插件动画时，只有对应呈现器可以接管；
    ///     请求里没带选择（旧配置 / 别的调用方）时保持插件自动接管的旧行为。
    /// </summary>
    private static bool IsSelected(IDrawResultPresenter presenter, DrawPresentationRequest request)
    {
        var selection = request.PluginAnimationId;
        if (string.IsNullOrEmpty(selection))
            return true;

        if (string.Equals(selection, DrawAnimationSelection.Host, StringComparison.Ordinal))
            return false;

        return string.Equals(selection, presenter.Id, StringComparison.OrdinalIgnoreCase);
    }
}
