using System.Runtime.InteropServices;
using SecRandom.Platforms.Abstractions;

namespace SecRandom.Platforms.MacOs;

public sealed class MacOsWindowFeatureService : IWindowFeatureService
{
    private const nint NsNormalWindowLevel = 0;
    private const nint NsFloatingWindowLevel = 3;
    private const nint NsUtilityWindowStyleMask = 1 << 4;
    private const nint NsWindowCollectionBehaviorCanJoinAllSpaces = 1 << 0;

    public WindowFeatures SupportedFeatures => WindowFeatures.Topmost |
                                                WindowFeatures.ToolWindow |
                                                WindowFeatures.ClickThrough;

    public WindowFeatureApplyResult Apply(PlatformWindowHandle window, WindowFeatureRequest request)
    {
        if (!window.IsValid)
            return WindowFeatureApplyResult.Failed(request.Features, "The native window handle is not available.");

        if (!string.IsNullOrWhiteSpace(window.Descriptor)
            && !string.Equals(window.Descriptor, "NSWindow", StringComparison.OrdinalIgnoreCase))
        {
            return WindowFeatureApplyResult.Unsupported(request.Features,
                $"The '{window.Descriptor}' native handle is not an NSWindow.");
        }

        var requested = request.Features & SupportedFeatures;
        var unsupported = request.Features & ~SupportedFeatures;
        var applied = WindowFeatures.None;
        var failed = WindowFeatures.None;
        string? detail = null;

        if ((requested & WindowFeatures.Topmost) != 0)
        {
            if (TrySendBooleanOrInteger(window.Value, "setLevel:", request.Enabled ? NsFloatingWindowLevel : NsNormalWindowLevel,
                    out var failure))
            {
                applied |= WindowFeatures.Topmost;
            }
            else
            {
                failed |= WindowFeatures.Topmost;
                detail ??= failure;
            }
        }

        if ((requested & WindowFeatures.ClickThrough) != 0)
        {
            if (TrySendBooleanOrInteger(window.Value, "setIgnoresMouseEvents:", request.Enabled ? 1 : 0, out var failure))
            {
                applied |= WindowFeatures.ClickThrough;
            }
            else
            {
                failed |= WindowFeatures.ClickThrough;
                detail ??= failure;
            }
        }

        if ((requested & WindowFeatures.ToolWindow) != 0)
        {
            if (TrySetToolWindow(window.Value, request.Enabled, out var failure))
            {
                applied |= WindowFeatures.ToolWindow;
            }
            else
            {
                failed |= WindowFeatures.ToolWindow;
                detail ??= failure;
            }
        }

        return WindowFeatureApplyResult.Partial(applied, unsupported, failed, detail);
    }

    private static bool TrySetToolWindow(nint window, bool enabled, out string? failure)
    {
        try
        {
            var styleMaskSelector = SelRegisterName("styleMask");
            var collectionBehaviorSelector = SelRegisterName("collectionBehavior");
            if (styleMaskSelector == nint.Zero || collectionBehaviorSelector == nint.Zero)
            {
                failure = "Unable to resolve the Objective-C NSWindow style selectors.";
                return false;
            }

            var styleMask = ObjcMsgSendNint(window, styleMaskSelector);
            if (!TrySendBooleanOrInteger(
                    window,
                    "setStyleMask:",
                    enabled ? styleMask | NsUtilityWindowStyleMask : styleMask & ~NsUtilityWindowStyleMask,
                    out failure))
            {
                return false;
            }

            var collectionBehavior = ObjcMsgSendNint(window, collectionBehaviorSelector);
            return TrySendBooleanOrInteger(
                window,
                "setCollectionBehavior:",
                enabled
                    ? collectionBehavior | NsWindowCollectionBehaviorCanJoinAllSpaces
                    : collectionBehavior & ~NsWindowCollectionBehaviorCanJoinAllSpaces,
                out failure);
        }
        catch (Exception exception)
        {
            failure = exception.Message;
            return false;
        }
    }

    private static bool TrySendBooleanOrInteger(nint window, string selectorName, nint value, out string? failure)
    {
        try
        {
            var selector = SelRegisterName(selectorName);
            if (selector == nint.Zero)
            {
                failure = $"Unable to resolve Objective-C selector '{selectorName}'.";
                return false;
            }

            ObjcMsgSend(window, selector, value);
            failure = null;
            return true;
        }
        catch (Exception exception)
        {
            failure = exception.Message;
            return false;
        }
    }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint SelRegisterName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend", CallingConvention = CallingConvention.Cdecl)]
    private static extern void ObjcMsgSend(nint receiver, nint selector, nint argument);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint ObjcMsgSendNint(nint receiver, nint selector);
}
