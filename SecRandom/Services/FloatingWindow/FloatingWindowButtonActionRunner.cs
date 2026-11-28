using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services;
using SecRandom.Core.Services.FeatureConfiguration;
using SecRandom.Services.Desktop;
using SecRandom.Services.ViewEngine;

namespace SecRandom.Services.FloatingWindow;

/// <summary>自定义按钮动作执行失败的原因。</summary>
public enum FloatingWindowButtonExecutionError
{
    None = 0,
    InvalidTarget,
    OpenFailed
}

/// <summary>
///     执行自定义按钮的动作。所有入口都复用应用既有的授权/导航实现，
///     这里只负责把配置翻译成对应的既有调用。
/// </summary>
public static class FloatingWindowButtonActionRunner
{
    public static bool IsFeatureAvailable(FloatingWindowButtonConfig button)
    {
        var features = IAppHost.TryGetService<FeatureRuntimeSnapshot>();
        return button.ActionKind switch
        {
            FloatingWindowButtonActionKind.QuickDraw => features?.IsEnabled(StartupFeatureCatalog.QuickDraw) != false,
            FloatingWindowButtonActionKind.Timer => features?.IsEnabled(StartupFeatureCatalog.Timer) != false,
            FloatingWindowButtonActionKind.MainPage when button.ActionTarget == "main.lottery" =>
                features?.IsEnabled(StartupFeatureCatalog.Lottery) != false,
            _ => true
        };
    }

    public static bool TryExecute(
        FloatingWindowButtonConfig button,
        out FloatingWindowButtonExecutionError error)
    {
        error = FloatingWindowButtonExecutionError.None;

        if (!IsFeatureAvailable(button))
        {
            error = FloatingWindowButtonExecutionError.OpenFailed;
            return false;
        }

        switch (button.ActionKind)
        {
            case FloatingWindowButtonActionKind.MainPage:
                if (!PageExists(PagesRegistryService.MainItems, button.ActionTarget))
                {
                    error = FloatingWindowButtonExecutionError.InvalidTarget;
                    return false;
                }

                App.ToggleMainWindow(button.ActionTarget);
                return true;

            case FloatingWindowButtonActionKind.QuickDraw:
                App.ShowQuickDrawWindow();
                return true;

            case FloatingWindowButtonActionKind.Timer:
                if (IAppHost.TryGetService<TimerViewService>() is not { } timer)
                {
                    error = FloatingWindowButtonExecutionError.OpenFailed;
                    return false;
                }

                _ = timer.ShowAsync();
                return true;

            case FloatingWindowButtonActionKind.SettingsPage:
                if (!PageExists(PagesRegistryService.SettingsItems, button.ActionTarget))
                {
                    error = FloatingWindowButtonExecutionError.InvalidTarget;
                    return false;
                }

                App.ShowSettingsWindow(button.ActionTarget);
                return true;

            case FloatingWindowButtonActionKind.Uri:
                if (!IsSafeUri(button.ActionTarget))
                {
                    error = FloatingWindowButtonExecutionError.InvalidTarget;
                    return false;
                }

                return OpenUri(button.ActionTarget);

            case FloatingWindowButtonActionKind.Path:
                return OpenPath(button.ActionTarget);

            default:
                error = FloatingWindowButtonExecutionError.InvalidTarget;
                return false;
        }
    }

    private static bool PageExists(IEnumerable<PageInfo> pages, string? id)
    {
        return !string.IsNullOrWhiteSpace(id) &&
               pages.Any(page => string.Equals(page.Id, id, StringComparison.Ordinal));
    }

    private static bool IsSafeUri(string? target)
    {
        return Uri.TryCreate(target, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static bool OpenUri(string target)
    {
        return IAppHost.TryGetService<IExternalLauncher>() is { } launcher && launcher.TryOpenUri(target);
    }

    private static bool OpenPath(string target)
    {
        if (string.IsNullOrWhiteSpace(target) || !Path.IsPathFullyQualified(target))
            return false;
        if (!File.Exists(target) && !Directory.Exists(target))
            return false;

        return IAppHost.TryGetService<IExternalLauncher>() is { } launcher && launcher.TryOpenPath(target);
    }
}
