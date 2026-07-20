namespace SecRandom.Core.Views;

public interface IViewEngine
{
    Task<IViewHandle> ShowAsync(string viewId, ViewShowOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>
    /// 单栈 host 导航语义：先关闭该 host 上的其他 Page 会话，再打开目标视图。
    /// </summary>
    Task<IViewHandle> ShowExclusiveAsync(string hostId, string viewId, ViewShowOptions? options = null, CancellationToken cancellationToken = default);
    Task<ViewCloseResult> ShowModalAsync(string viewId, ViewShowOptions? options = null, CancellationToken cancellationToken = default);
    Task<ViewCloseResult> CloseAsync(string viewId, ViewCloseReason reason = ViewCloseReason.Programmatic, object? result = null, CancellationToken cancellationToken = default);
    Task CloseHostAsync(IViewHost host, ViewCloseReason reason, CancellationToken cancellationToken = default);
}
