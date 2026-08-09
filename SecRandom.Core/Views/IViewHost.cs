namespace SecRandom.Core.Views;

public interface IViewHost
{
    string HostId { get; }
    IReadOnlyList<ViewBase> PageStack { get; }
    ViewBase? ActiveModalView { get; }
    event EventHandler? Destroyed;
    Task ShowPageAsync(ViewBase view, CancellationToken cancellationToken = default);
    Task ShowModalAsync(ViewBase view, CancellationToken cancellationToken = default);
    Task ActivateAsync(ViewBase view, CancellationToken cancellationToken = default);
    Task CloseAsync(ViewBase view, CancellationToken cancellationToken = default);
    Task DestroyAsync(CancellationToken cancellationToken = default);
}
