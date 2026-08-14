using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace SecRandom.Platforms.Windows;

/// <summary>
/// Opens the Windows touch keyboard for touch-focused text inputs. Avalonia's InputPane
/// continues to own keyboard occlusion notifications and content positioning.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsTouchKeyboardIntegration
{
    private const double InputPaneClearance = 12;
    private static readonly Dictionary<IInputPane, TopLevel> TopLevelsByInputPane = [];
    private static readonly HashSet<Window> ShutdownGuardedWindows = [];
    private static bool _initialized;
    private static bool _shutdownHooked;

    public static void Initialize()
    {
        if (_initialized || !OperatingSystem.IsWindows())
            return;

        _initialized = true;
        Control.LoadedEvent.AddClassHandler<TopLevel>(OnTopLevelLoaded, handledEventsToo: true);
        Control.UnloadedEvent.AddClassHandler<TopLevel>(OnTopLevelUnloaded, handledEventsToo: true);
        InputElement.PointerPressedEvent.AddClassHandler<TextBox>(OnTextBoxPointerPressed, handledEventsToo: true);
    }

    private static void OnTopLevelLoaded(TopLevel topLevel, RoutedEventArgs e)
    {
        HookDesktopShutdown();
        if (topLevel is Window window)
            ShutdownGuardedWindows.Add(window);

        if (topLevel.InputPane is not { } inputPane || !TopLevelsByInputPane.TryAdd(inputPane, topLevel))
            return;

        inputPane.StateChanged += InputPane_OnStateChanged;
        if (topLevel is Window loadedWindow)
            loadedWindow.Closing += WindowOnClosing;
    }

    private static void OnTopLevelUnloaded(TopLevel topLevel, RoutedEventArgs e)
    {
        if (topLevel.InputPane is not { } inputPane || !TopLevelsByInputPane.Remove(inputPane))
            return;

        inputPane.StateChanged -= InputPane_OnStateChanged;
        topLevel.RenderTransform = null;
    }

    private static void WindowOnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (sender is Window window)
            DetachInputPaneBeforeAvaloniaDisposal(window);
    }

    private static void HookDesktopShutdown()
    {
        if (_shutdownHooked || Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        desktop.ShutdownRequested += DesktopLifetimeOnShutdownRequested;
        _shutdownHooked = true;
    }

    private static void DesktopLifetimeOnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        // Avalonia's Win32InputPane can crash in its native Unadvise call during
        // process shutdown on some Windows builds. The process is already exiting,
        // so skip that dispose path and let Windows reclaim the COM object.
        foreach (var topLevel in TopLevelsByInputPane.Values.Concat(ShutdownGuardedWindows).Distinct())
            DetachInputPaneBeforeAvaloniaDisposal(topLevel);

        TopLevelsByInputPane.Clear();
    }

    private static void DetachInputPaneBeforeAvaloniaDisposal(TopLevel topLevel)
    {
        var platformImpl = topLevel.PlatformImpl;
        if (platformImpl is null)
            return;

        var inputPaneField = platformImpl.GetType().GetField(
            "_inputPane",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (inputPaneField is not null && inputPaneField.GetValue(platformImpl) is not null)
            inputPaneField.SetValue(platformImpl, null);
    }

    private static void OnTextBoxPointerPressed(TextBox textBox, PointerPressedEventArgs e)
    {
        if (e.Pointer.Type != PointerType.Touch)
            return;

        var handle = TopLevel.GetTopLevel(textBox)?.TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (handle != nint.Zero)
            Toggle(handle);
    }

    private static void InputPane_OnStateChanged(object? sender, InputPaneStateEventArgs e)
    {
        if (sender is not IInputPane inputPane ||
            !TopLevelsByInputPane.TryGetValue(inputPane, out var topLevel))
            return;

        if (e.NewState != InputPaneState.Open || topLevel.FocusManager?.GetFocusedElement() is not TextBox textBox)
        {
            topLevel.RenderTransform = null;
            return;
        }

        var textBoxBottom = topLevel.PointToScreen(textBox.Bounds.BottomLeft).ToPoint(topLevel.RenderScaling);
        var topLevelOrigin = topLevel.PointToScreen(default).ToPoint(topLevel.RenderScaling);
        var occludedRect = e.EndRect.Translate(topLevelOrigin);
        if (!occludedRect.Contains(textBoxBottom))
            return;

        topLevel.RenderTransform = new TranslateTransform(0, occludedRect.Top - InputPaneClearance - textBoxBottom.Y);
    }

    [SupportedOSPlatform("windows")]
    private static void Toggle(nint windowHandle)
    {
        try
        {
            var invocation = (ITipInvocation)new UIHostNoLaunch();
            invocation.Toggle(windowHandle);
            Marshal.ReleaseComObject(invocation);
        }
        catch (COMException exception) when ((uint)exception.HResult == 0x80040154)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("tabtip.exe")
            {
                UseShellExecute = true
            });
        }
    }

    [ComImport, Guid("4ce576fa-83dc-4F88-951c-9d0782b4e376")]
    private class UIHostNoLaunch
    {
    }

    [ComImport, Guid("37c994e7-432b-4834-a2f7-dce1f13b834b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITipInvocation
    {
        void Toggle(nint windowHandle);
    }
}
