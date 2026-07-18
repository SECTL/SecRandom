using System.Runtime.InteropServices;
using SecRandom.Platforms.Abstractions;

namespace SecRandom.Platforms.Linux;

public sealed class LinuxWindowFeatureService : IWindowFeatureService
{
    private const int ClientMessage = 33;
    private const nint SubstructureNotifyMask = 1 << 19;
    private const nint SubstructureRedirectMask = 1 << 20;
    private const nint NetWmStateRemove = 0;
    private const nint NetWmStateAdd = 1;
    private const nint NetWmStateSourceApplication = 1;
    private const int PropModeReplace = 0;

    public WindowFeatures SupportedFeatures => IsX11Session
        ? WindowFeatures.Topmost | WindowFeatures.ToolWindow | WindowFeatures.SkipTaskSwitcher
        : WindowFeatures.None;

    public WindowFeatureApplyResult Apply(PlatformWindowHandle window, WindowFeatureRequest request)
    {
        if (!window.IsValid)
            return WindowFeatureApplyResult.Failed(request.Features, "The native window handle is not available.");

        if (!string.IsNullOrWhiteSpace(window.Descriptor)
            && !string.Equals(window.Descriptor, "XID", StringComparison.OrdinalIgnoreCase))
        {
            return WindowFeatureApplyResult.Unsupported(request.Features,
                $"The '{window.Descriptor}' native handle is not an X11 XID.");
        }

        var requested = request.Features & SupportedFeatures;
        var unsupported = request.Features & ~SupportedFeatures;
        if (requested == WindowFeatures.None)
            return WindowFeatureApplyResult.Partial(WindowFeatures.None, unsupported, WindowFeatures.None);

        var applied = WindowFeatures.None;
        var failed = WindowFeatures.None;
        string? detail = null;

        if ((requested & WindowFeatures.Topmost) != 0)
        {
            if (TrySetTopmost(window.Value, request.Enabled, out var failure))
                applied |= WindowFeatures.Topmost;
            else
            {
                failed |= WindowFeatures.Topmost;
                detail ??= failure;
            }
        }

        if ((requested & WindowFeatures.ToolWindow) != 0)
        {
            if (TrySetToolWindow(window.Value, request.Enabled, out var failure))
                applied |= WindowFeatures.ToolWindow;
            else
            {
                failed |= WindowFeatures.ToolWindow;
                detail ??= failure;
            }
        }

        if ((requested & WindowFeatures.SkipTaskSwitcher) != 0)
        {
            if (TrySetSkipTaskSwitcher(window.Value, request.Enabled, out var failure))
                applied |= WindowFeatures.SkipTaskSwitcher;
            else
            {
                failed |= WindowFeatures.SkipTaskSwitcher;
                detail ??= failure;
            }
        }

        return WindowFeatureApplyResult.Partial(applied, unsupported, failed, detail);
    }

    private static bool TrySetTopmost(nint window, bool enabled, out string? failure)
    {
        nint display;
        try
        {
            display = XOpenDisplay(nint.Zero);
        }
        catch (Exception exception)
        {
            failure = exception.Message;
            return false;
        }

        if (display == nint.Zero)
        {
            failure = "Unable to open the X11 display. The active compositor may not expose X11 window management.";
            return false;
        }

        try
        {
            var stateAtom = XInternAtom(display, "_NET_WM_STATE", onlyIfExists: false);
            var aboveAtom = XInternAtom(display, "_NET_WM_STATE_ABOVE", onlyIfExists: false);
            if (stateAtom == nint.Zero || aboveAtom == nint.Zero)
            {
                failure = "The X11 window manager does not expose the EWMH topmost atoms.";
                return false;
            }

            var message = new XClientMessageEvent
            {
                Type = ClientMessage,
                Display = display,
                Window = window,
                MessageType = stateAtom,
                Format = 32,
                Data0 = enabled ? NetWmStateAdd : NetWmStateRemove,
                Data1 = aboveAtom,
                Data3 = NetWmStateSourceApplication
            };
            return TrySendStateMessage(display, window, ref message, "topmost", out failure);
        }
        catch (Exception exception)
        {
            failure = exception.Message;
            return false;
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    private static bool TrySetToolWindow(nint window, bool enabled, out string? failure)
    {
        nint display;
        try
        {
            display = XOpenDisplay(nint.Zero);
        }
        catch (Exception exception)
        {
            failure = exception.Message;
            return false;
        }

        if (display == nint.Zero)
        {
            failure = "Unable to open the X11 display. The active compositor may not expose X11 window management.";
            return false;
        }

        try
        {
            var windowTypeAtom = XInternAtom(display, "_NET_WM_WINDOW_TYPE", onlyIfExists: false);
            var atomAtom = XInternAtom(display, "ATOM", onlyIfExists: false);
            var typeAtom = XInternAtom(display,
                enabled ? "_NET_WM_WINDOW_TYPE_UTILITY" : "_NET_WM_WINDOW_TYPE_NORMAL", onlyIfExists: false);
            if (windowTypeAtom == nint.Zero || atomAtom == nint.Zero || typeAtom == nint.Zero)
            {
                failure = "The X11 window manager does not expose the EWMH window-type atoms.";
                return false;
            }

            XChangeProperty(display, window, windowTypeAtom, atomAtom, format: 32, PropModeReplace, [typeAtom], 1);
            XSync(display, discard: false);
            failure = null;
            return true;
        }
        catch (Exception exception)
        {
            failure = exception.Message;
            return false;
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    private static bool TrySetSkipTaskSwitcher(nint window, bool enabled, out string? failure)
    {
        nint display;
        try
        {
            display = XOpenDisplay(nint.Zero);
        }
        catch (Exception exception)
        {
            failure = exception.Message;
            return false;
        }

        if (display == nint.Zero)
        {
            failure = "Unable to open the X11 display. The active compositor may not expose X11 window management.";
            return false;
        }

        try
        {
            var stateAtom = XInternAtom(display, "_NET_WM_STATE", onlyIfExists: false);
            var skipTaskbarAtom = XInternAtom(display, "_NET_WM_STATE_SKIP_TASKBAR", onlyIfExists: false);
            var skipPagerAtom = XInternAtom(display, "_NET_WM_STATE_SKIP_PAGER", onlyIfExists: false);
            if (stateAtom == nint.Zero || skipTaskbarAtom == nint.Zero || skipPagerAtom == nint.Zero)
            {
                failure = "The X11 window manager does not expose the EWMH task-switcher atoms.";
                return false;
            }

            var message = new XClientMessageEvent
            {
                Type = ClientMessage,
                Display = display,
                Window = window,
                MessageType = stateAtom,
                Format = 32,
                Data0 = enabled ? NetWmStateAdd : NetWmStateRemove,
                Data1 = skipTaskbarAtom,
                Data2 = skipPagerAtom,
                Data3 = NetWmStateSourceApplication
            };
            return TrySendStateMessage(display, window, ref message, "task-switcher exclusion", out failure);
        }
        catch (Exception exception)
        {
            failure = exception.Message;
            return false;
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    private static bool TrySendStateMessage(
        nint display,
        nint window,
        ref XClientMessageEvent message,
        string feature,
        out string? failure)
    {
        nint children = nint.Zero;
        try
        {
            if (XQueryTree(display, window, out var root, out _, out children, out _) == 0 || root == nint.Zero)
            {
                failure = "Unable to resolve the X11 root window for the EWMH request.";
                return false;
            }

            if (XSendEvent(display, root, propagate: 0, SubstructureNotifyMask | SubstructureRedirectMask, ref message) == 0)
            {
                failure = $"The X11 window manager rejected the EWMH {feature} request.";
                return false;
            }

            XFlush(display);
            failure = null;
            return true;
        }
        finally
        {
            if (children != nint.Zero)
                XFree(children);
        }
    }

    private static bool IsX11Session => !string.IsNullOrWhiteSpace(
        Environment.GetEnvironmentVariable("DISPLAY"));

    // XSendEvent accepts an XEvent union: 24 native longs on both ILP32 and LP64.
    [StructLayout(LayoutKind.Sequential)]
    private struct XClientMessageEvent
    {
        public int Type;
        public nint Serial;
        public int SendEvent;
        public nint Display;
        public nint Window;
        public nint MessageType;
        public int Format;
        public nint Data0;
        public nint Data1;
        public nint Data2;
        public nint Data3;
        public nint Data4;
        public nint Pad0;
        public nint Pad1;
        public nint Pad2;
        public nint Pad3;
        public nint Pad4;
        public nint Pad5;
        public nint Pad6;
        public nint Pad7;
        public nint Pad8;
        public nint Pad9;
        public nint Pad10;
        public nint Pad11;
    }

    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint XOpenDisplay(nint displayName);

    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
    private static extern int XCloseDisplay(nint display);

    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
    private static extern int XQueryTree(
        nint display,
        nint window,
        out nint rootReturn,
        out nint parentReturn,
        out nint childrenReturn,
        out uint childrenCountReturn);

    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
    private static extern int XFree(nint data);

    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern nint XInternAtom(nint display, string atomName, [MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);

    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
    private static extern int XSendEvent(nint display, nint window, int propagate, nint eventMask,
        ref XClientMessageEvent eventSend);

    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
    private static extern int XFlush(nint display);

    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
    private static extern int XSync(nint display, [MarshalAs(UnmanagedType.Bool)] bool discard);

    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
    private static extern int XChangeProperty(
        nint display,
        nint window,
        nint property,
        nint type,
        int format,
        int mode,
        nint[] data,
        int elementCount);
}
