namespace SecRandom.Core.Abstraction.Services.Notifications;

/// <summary>通知的严重级别，决定图标与配色。</summary>
public enum NotificationSeverity
{
    /// <summary>普通信息。</summary>
    Info = 0,

    /// <summary>成功。</summary>
    Success = 1,

    /// <summary>警告。</summary>
    Warning = 2,

    /// <summary>错误。</summary>
    Error = 3
}

/// <summary>一条通知的完整参数。</summary>
public sealed record NotificationOptions(
    string Message,
    NotificationSeverity Severity = NotificationSeverity.Info,
    string? Title = null,
    TimeSpan? Duration = null,
    string? PluginId = null);

/// <summary>
///     宿主通知扩展点：把消息显示在宿主自己的信息条 / Toast 上，需要用户确认时用系统样式的对话框。
///     <para>
///         <see cref="IsSupported" /> 为 false（手机、无壳）时 <c>Show</c> 静默忽略，
///         <c>ConfirmAsync</c> 直接返回 false、<c>PromptAsync</c> 返回 null——插件不需要自己判平台。
///     </para>
/// </summary>
public interface INotificationService
{
    /// <summary>当前壳是否支持显示通知/对话框。</summary>
    bool IsSupported { get; }

    /// <summary>显示一条通知。</summary>
    void Show(string message, NotificationSeverity severity = NotificationSeverity.Info, string? title = null);

    /// <summary>显示一条带完整参数的通知。</summary>
    void Show(NotificationOptions options);

    /// <summary>弹一个确认框；用户点确认为 true，取消或宿主不支持为 false。</summary>
    Task<bool> ConfirmAsync(
        string message,
        string? title = null,
        string? confirmText = null,
        string? cancelText = null,
        CancellationToken cancellationToken = default);

    /// <summary>弹一个输入框；用户取消或宿主不支持返回 null。</summary>
    Task<string?> PromptAsync(
        string message,
        string? title = null,
        string? initialValue = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     忙碌指示扩展点：插件长任务期间在宿主界面上盖一层忙碌遮罩。
///     <para>
///         <c>using</c> 结束即关闭（可嵌套）；<see cref="Report" /> 用来自定义文案与进度（0-1）。
///     </para>
/// </summary>
public interface IBusyIndicator
{
    /// <summary>当前是否正在显示忙碌指示。</summary>
    bool IsBusy { get; }

    /// <summary>开始忙碌，返回的对象 Dispose 后结束。</summary>
    IDisposable Begin(string? message = null, double? progress = null);

    /// <summary>更新文案/进度（null 表示不改这一项）。</summary>
    void Report(string? message, double? progress = null);
}
