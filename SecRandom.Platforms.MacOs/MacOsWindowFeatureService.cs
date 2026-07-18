using System.Runtime.InteropServices;
using SecRandom.Platforms.Abstractions;

namespace SecRandom.Platforms.MacOs;

public sealed class MacOsWindowFeatureService : IWindowFeatureService
{
    private const nint NsNormalWindowLevel = 0;
    private const nint NsFloatingWindowLevel = 3;

    public WindowFeatures SupportedFeatures => WindowFeatures.Topmost | WindowFeatures.ClickThrough;

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

        return WindowFeatureApplyResult.Partial(applied, unsupported, failed, detail);
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
}
