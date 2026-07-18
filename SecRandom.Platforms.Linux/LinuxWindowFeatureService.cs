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

    public WindowFeatures SupportedFeatures => IsX11Session ? WindowFeatures.Topmost : WindowFeatures.None;

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

        if (TrySetTopmost(window.Value, request.Enabled, out var failure))
            return WindowFeatureApplyResult.Partial(requested, unsupported, WindowFeatures.None);

        return WindowFeatureApplyResult.Partial(WindowFeatures.None, unsupported, requested, failure);
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
                Data1 = aboveAtom
            };
            if (XQueryTree(display, window, out var root, out _, out var children, out _) == 0 || root == nint.Zero)
            {
                failure = "Unable to resolve the X11 root window for the EWMH request.";
                return false;
            }

            if (children != nint.Zero)
                XFree(children);

            var sent = XSendEvent(
                display,
                root,
                propagate: 0,
                SubstructureNotifyMask | SubstructureRedirectMask,
                ref message);
            if (sent == 0)
            {
                failure = "The X11 window manager rejected the EWMH topmost request.";
                return false;
            }

            XFlush(display);
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

    private static bool IsX11Session => !string.IsNullOrWhiteSpace(
        Environment.GetEnvironmentVariable("DISPLAY")) && string.IsNullOrWhiteSpace(
        Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

    [StructLayout(LayoutKind.Explicit, Size = 192)]
    private struct XClientMessageEvent
    {
        [FieldOffset(0)] public int Type;
        [FieldOffset(8)] public nint Serial;
        [FieldOffset(16)] public int SendEvent;
        [FieldOffset(24)] public nint Display;
        [FieldOffset(32)] public nint Window;
        [FieldOffset(40)] public nint MessageType;
        [FieldOffset(48)] public int Format;
        [FieldOffset(56)] public nint Data0;
        [FieldOffset(64)] public nint Data1;
        [FieldOffset(72)] public nint Data2;
        [FieldOffset(80)] public nint Data3;
        [FieldOffset(88)] public nint Data4;
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
}
