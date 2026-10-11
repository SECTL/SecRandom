using Avalonia.Controls;

namespace SecRandom.Core.Abstraction.Services.Views;

/// <summary>弹出一层界面（宿主窗口内的叠加层，或独立小窗）的参数。</summary>
public sealed record PopupOptions
{
    /// <summary>弹出层 id；为空时由宿主生成。</summary>
    public string? Id { get; init; }

    /// <summary>内容。</summary>
    public required Control Content { get; init; }

    /// <summary>是否显示遮罩。</summary>
    public bool ShowBackdrop { get; init; } = true;

    /// <summary>遮罩颜色（#AARRGGBB）。</summary>
    public string BackdropColor { get; init; } = "#66000000";

    /// <summary>点遮罩关闭。</summary>
    public bool DismissOnClickOutside { get; init; } = true;

    /// <summary>Esc 关闭。</summary>
    public bool DismissOnEscape { get; init; } = true;

    /// <summary>是否挡住下层输入。</summary>
    public bool BlockInput { get; init; } = true;

    /// <summary>即使宿主支持窗口内叠加层也坚持开独立窗口（需要离开宿主窗口时用）。</summary>
    public bool PreferSeparateWindow { get; init; }
}

/// <summary>弹出层关闭事件。</summary>
public sealed class PopupClosedEventArgs(string id, bool closedByUser) : EventArgs
{
    /// <summary>弹出层 id。</summary>
    public string Id { get; } = id;

    /// <summary>true 表示用户关的，false 表示代码调的 <c>Close</c>。</summary>
    public bool ClosedByUser { get; } = closedByUser;
}

/// <summary>弹出层句柄。</summary>
/// <param name="Id">弹出层 id。</param>
/// <param name="UsedWindow">true 表示实际用了独立窗口（宿主没有叠加层，或调用方要求）。</param>
public sealed record PopupHandle(string Id, bool UsedWindow);

/// <summary>
///     跨 shell 的弹出层：有窗口内叠加层的 shell（桌面）走叠加层，没有的（移动端/无头）自动回退到独立窗口。
///     插件只用这一个接口就能在所有平台弹出界面，不必分别处理。
/// </summary>
public interface IPopupService
{
    /// <summary>当前 shell 是否能弹出界面。</summary>
    bool IsSupported { get; }

    /// <summary>当前打开的弹出层 id。</summary>
    IReadOnlyList<string> OpenPopupIds { get; }

    /// <summary>弹出层关闭。</summary>
    event EventHandler<PopupClosedEventArgs>? PopupClosed;

    /// <summary>弹出界面，返回句柄（含实际用的是叠加层还是窗口）。</summary>
    PopupHandle Show(PopupOptions options);

    /// <summary>关闭指定弹出层。</summary>
    void Close(string id);

    /// <summary>关闭全部插件弹出层。</summary>
    void CloseAll();
}
