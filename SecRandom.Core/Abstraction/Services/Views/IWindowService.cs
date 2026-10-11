using Avalonia.Controls;

namespace SecRandom.Core.Abstraction.Services.Views;

/// <summary>打开一个插件窗口的参数。</summary>
public sealed record WindowRequest
{
    /// <summary>窗口 id；为空时由宿主生成。同一个 id 再次 <c>Show</c> 会激活已有窗口。</summary>
    public string? Id { get; init; }

    /// <summary>窗口内容。</summary>
    public required Control Content { get; init; }

    /// <summary>标题栏文字。</summary>
    public string? Title { get; init; }

    /// <summary>初始宽高（null 表示由内容决定）。</summary>
    public double? Width { get; init; }

    /// <summary>初始高度。</summary>
    public double? Height { get; init; }

    /// <summary>最小宽度。</summary>
    public double? MinWidth { get; init; }

    /// <summary>最小高度。</summary>
    public double? MinHeight { get; init; }

    /// <summary>是否允许缩放。</summary>
    public bool CanResize { get; init; } = true;

    /// <summary>是否显示标题栏（false 时是纯内容窗，需要自己提供关闭方式）。</summary>
    public bool ShowTitleBar { get; init; } = true;

    /// <summary>是否置顶。</summary>
    public bool Topmost { get; init; }

    /// <summary>是否在屏幕居中。</summary>
    public bool CenterOnScreen { get; init; } = true;

    /// <summary>Esc 是否关闭。</summary>
    public bool CloseOnEscape { get; init; } = true;

    /// <summary>是否出现在任务栏。</summary>
    public bool ShowInTaskbar { get; init; } = true;
}

/// <summary>窗口关闭事件。</summary>
public sealed class WindowClosedEventArgs(string id, bool closedByUser) : EventArgs
{
    /// <summary>窗口 id。</summary>
    public string Id { get; } = id;

    /// <summary>true 表示用户关的（点关闭/按 Esc），false 表示代码调的 <c>Close</c>。</summary>
    public bool ClosedByUser { get; } = closedByUser;
}

/// <summary>
///     插件窗口：开一个独立窗口，或开一个带返回值的模态对话框。
///     <para>
///         对话框的返回值由内容自己在结束时回传：调用 <c>Close(id, result)</c>，
///         <see cref="ShowDialogAsync" /> 的 Task 就会以该结果完成；用户直接关掉窗口则返回 null。
///     </para>
///     <para>无头/移动端 <see cref="IsSupported" /> 可能为 false，此时 <c>Show</c> 返回 id 但不会真的开窗。</para>
/// </summary>
public interface IWindowService
{
    /// <summary>当前 shell 是否支持插件开窗。</summary>
    bool IsSupported { get; }

    /// <summary>当前打开的窗口 id。</summary>
    IReadOnlyList<string> OpenWindowIds { get; }

    /// <summary>窗口关闭（用户关或代码关）。</summary>
    event EventHandler<WindowClosedEventArgs>? WindowClosed;

    /// <summary>以模态方式打开，等用户处理完再返回结果（由 <c>Close(id, result)</c> 提供）。</summary>
    Task<object?> ShowDialogAsync(WindowRequest request, CancellationToken cancellationToken = default);

    /// <summary>非模态打开，返回窗口 id。</summary>
    string Show(WindowRequest request);

    /// <summary>把窗口带到前台。</summary>
    bool Activate(string id);

    /// <summary>关闭窗口（可选带回结果）。</summary>
    void Close(string id, object? result = null);

    /// <summary>关闭全部插件窗口。</summary>
    void CloseAll();
}
