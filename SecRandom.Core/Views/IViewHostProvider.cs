namespace SecRandom.Core.Views;

public interface IViewHostProvider
{
    Task<ViewHostSelection> GetHostAsync(ViewShowOptions options, CancellationToken cancellationToken = default);
}
