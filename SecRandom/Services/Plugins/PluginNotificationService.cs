using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services.Notifications;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Models.UI;
using SecRandom.Views;

namespace SecRandom.Services.Plugins;

/// <summary>
///     <see cref="INotificationService" /> 的宿主实现：通知落到 <see cref="MainView" /> 的 Toast，
///     确认/输入请求落到 FluentAvalonia 的 <see cref="FAContentDialog" />。
///     <para>
///         所有 UI 交互都会封送到 UI 线程；<see cref="IsSupported" /> 为 false（无壳/移动端）时
///         <see cref="Show(NotificationOptions)" /> 静默忽略、
///         <see cref="ConfirmAsync" /> 返回 false、<see cref="PromptAsync" /> 返回 null，插件不需要自己判平台。
///     </para>
///     <para>
///         本类命名刻意避开宿主既有的 <c>NotificationService</c>（<c>SecRandom\App.axaml.cs</c> 注册的是别的东西），
///         以免 DI 注册冲突。
///     </para>
/// </summary>
public sealed class PluginNotificationService : INotificationService
{
    /// <summary>未指定时长时 Toast 的默认停留时间（与宿主 <see cref="ToastMessage" /> 默认值一致）。</summary>
    private static readonly TimeSpan DefaultToastDuration = TimeSpan.FromSeconds(5);

    /// <summary>默认确认按钮文案：复用宿主已有三语词条（确认 / Confirm / 確認）。</summary>
    private static string DefaultConfirmText => SecRandom.Langs.SettingsPages.Linkage.Resources.C_Confirm;

    /// <summary>默认取消按钮文案：复用宿主已有三语词条（取消 / Cancel / キャンセル）。</summary>
    private static string DefaultCancelText => SecRandom.Langs.SettingsPages.Linkage.Resources.C_Cancel;

    private readonly ILogger<PluginNotificationService>? _logger;

    /// <summary>
    ///     初始化 <see cref="PluginNotificationService" />。
    /// </summary>
    /// <param name="logger">日志服务；宿主已 <c>AddLogging</c>，因此 DI 可直接解析，省略时为 null。</param>
    public PluginNotificationService(ILogger<PluginNotificationService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>宿主主壳存在时才支持 UI 交互。</summary>
    public bool IsSupported => MainView.Current is not null;

    /// <inheritdoc />
    public void Show(string message, NotificationSeverity severity = NotificationSeverity.Info, string? title = null)
    {
        Show(new NotificationOptions(message, severity, title));
    }

    /// <inheritdoc />
    public void Show(NotificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!IsSupported)
        {
            _logger?.LogDebug("宿主没有活动主壳，插件通知被忽略。");
            return;
        }

        RunOnUiThread(() => ShowCore(options));
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmAsync(
        string message,
        string? title = null,
        string? confirmText = null,
        string? cancelText = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
        {
            _logger?.LogDebug("宿主没有活动主壳，确认对话框被忽略。");
            return false;
        }

        if (cancellationToken.IsCancellationRequested)
            return false;

        return await RunOnUiThreadAsync(
            () => ConfirmCoreAsync(message, title, confirmText, cancelText, cancellationToken)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string?> PromptAsync(
        string message,
        string? title = null,
        string? initialValue = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
        {
            _logger?.LogDebug("宿主没有活动主壳，输入对话框被忽略。");
            return null;
        }

        if (cancellationToken.IsCancellationRequested)
            return null;

        return await RunOnUiThreadAsync(
            () => PromptCoreAsync(message, title, initialValue, cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>把通知落到宿主 Toast（必须在 UI 线程调用）。</summary>
    private static void ShowCore(NotificationOptions options)
    {
        if (MainView.Current is not { } view)
            return;

        var severity = MapSeverity(options.Severity);

        // 没有自定义标题/时长时走宿主现成的 Toast 入口，保持与宿主提示风格一致。
        if (string.IsNullOrEmpty(options.Title) && options.Duration is null)
        {
            switch (options.Severity)
            {
                case NotificationSeverity.Success:
                    MainView.ShowSuccessToast(options.Message);
                    return;
                case NotificationSeverity.Warning:
                    ToastsHelper.ShowWarningToast(view, options.Message);
                    return;
                case NotificationSeverity.Error:
                    ToastsHelper.ShowErrorToast(view, options.Message);
                    return;
                default:
                    MainView.ShowToast(options.Message, severity);
                    return;
            }
        }

        // 自定义标题/时长只能自己组装 ToastMessage（属性全是 init，只能在初始化器里赋值）。
        var toast = new ToastMessage(options.Title ?? string.Empty, options.Message)
        {
            Severity = severity,
            Duration = options.Duration ?? DefaultToastDuration
        };

        ToastsHelper.ShowToast(view, toast);
    }

    /// <summary>显示确认对话框（必须在 UI 线程调用）。</summary>
    private async Task<bool> ConfirmCoreAsync(
        string message,
        string? title,
        string? confirmText,
        string? cancelText,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;

        if (TryGetTopLevel() is not { } topLevel)
        {
            _logger?.LogWarning("无法获取宿主 TopLevel，确认对话框已降级为 false。");
            return false;
        }

        var dialog = new FAContentDialog
        {
            Title = string.IsNullOrWhiteSpace(title) ? null : title,
            Content = message,
            PrimaryButtonText = string.IsNullOrWhiteSpace(confirmText) ? DefaultConfirmText : confirmText,
            CloseButtonText = string.IsNullOrWhiteSpace(cancelText) ? DefaultCancelText : cancelText,
            DefaultButton = FAContentDialogButton.Close
        };

        using var registration = RegisterCancellation(dialog, cancellationToken);

        try
        {
            var result = await dialog.ShowAsync(topLevel).ConfigureAwait(true);
            if (cancellationToken.IsCancellationRequested)
                return false;

            return result == FAContentDialogResult.Primary;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "插件确认对话框执行失败。");
            return false;
        }
    }

    /// <summary>显示输入对话框（必须在 UI 线程调用）。</summary>
    private async Task<string?> PromptCoreAsync(
        string message,
        string? title,
        string? initialValue,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return null;

        if (TryGetTopLevel() is not { } topLevel)
        {
            _logger?.LogWarning("无法获取宿主 TopLevel，输入对话框已降级为 null。");
            return null;
        }

        var textBox = new TextBox
        {
            MinWidth = 320,
            Text = initialValue ?? string.Empty
        };

        var content = new StackPanel { Spacing = 8 };
        if (!string.IsNullOrEmpty(message))
            content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(textBox);

        var dialog = new FAContentDialog
        {
            Title = string.IsNullOrWhiteSpace(title) ? null : title,
            Content = content,
            PrimaryButtonText = DefaultConfirmText,
            CloseButtonText = DefaultCancelText,
            DefaultButton = FAContentDialogButton.Primary
        };

        using var registration = RegisterCancellation(dialog, cancellationToken);

        try
        {
            var result = await dialog.ShowAsync(topLevel).ConfigureAwait(true);
            if (cancellationToken.IsCancellationRequested)
                return null;

            return result == FAContentDialogResult.Primary ? textBox.Text?.Trim() : null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "插件输入对话框执行失败。");
            return null;
        }
    }

    /// <summary>
    ///     取宿主 TopLevel：先在活动主壳上取，取不到再退回 <see cref="App.GetRootWindow" />。
    ///     <see cref="App.GetRootWindow" /> 在没有活动窗口时会抛 <see cref="InvalidOperationException" />，这里降级为 null。
    ///     必须在 UI 线程调用。
    /// </summary>
    private static TopLevel? TryGetTopLevel()
    {
        if (MainView.Current is { } view && TopLevel.GetTopLevel(view) is { } fromView)
            return fromView;

        try
        {
            if (Application.Current is App app)
                return app.GetRootWindow();
        }
        catch (InvalidOperationException)
        {
            // 没有活动窗口：按“不支持交互”降级。
        }

        return null;
    }

    /// <summary>
    ///     把取消令牌接到对话框上：取消时在 UI 线程调用 <see cref="FAContentDialog.Hide" />，
    ///     使 <c>ShowAsync</c> 立刻以 None 结束，调用方再按“已取消”返回 false/null。
    /// </summary>
    private static CancellationTokenRegistration RegisterCancellation(FAContentDialog dialog, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
            return default;

        return cancellationToken.Register(
            static state =>
            {
                var target = (FAContentDialog)state!;
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        target.Hide();
                    }
                    catch (Exception)
                    {
                        // 对话框可能已经关闭：取消注册只做尽力而为。
                    }
                });
            },
            dialog);
    }

    /// <summary>把宿主通知级别映射到 FluentAvalonia 的 InfoBar 级别。</summary>
    private static FAInfoBarSeverity MapSeverity(NotificationSeverity severity)
    {
        return severity switch
        {
            NotificationSeverity.Success => FAInfoBarSeverity.Success,
            NotificationSeverity.Warning => FAInfoBarSeverity.Warning,
            NotificationSeverity.Error => FAInfoBarSeverity.Error,
            _ => FAInfoBarSeverity.Informational
        };
    }

    /// <summary>封送一个即发即弃的 UI 操作（对齐宿主 <c>MainViewAdapter</c> 的写法）。</summary>
    private static void RunOnUiThread(Action action)
    {
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    /// <summary>封送一个返回 Task 的 UI 操作并等待结果（对齐宿主 <c>MainViewAdapter</c> 的写法）。</summary>
    private static async Task<T> RunOnUiThreadAsync<T>(Func<Task<T>> action)
    {
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
            return await action().ConfigureAwait(false);

        // Avalonia 的 Dispatcher.InvokeAsync(Func<Task<T>>) 直接返回 Task<T>，await 一次即可拿到结果。
        return await Dispatcher.UIThread.InvokeAsync(action).ConfigureAwait(false);
    }
}
