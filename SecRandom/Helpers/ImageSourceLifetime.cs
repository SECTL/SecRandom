using Avalonia.Threading;

namespace SecRandom.Helpers;

internal static class ImageSourceLifetime
{
    private const int RetirementDelayMilliseconds = 100;

    public static void DisposeAfterRender(IDisposable image)
    {
        ArgumentNullException.ThrowIfNull(image);
        _ = DisposeAfterRenderAsync(image);
    }

    private static async Task DisposeAfterRenderAsync(IDisposable image)
    {
        try
        {
            // The compositor may still reference the preceding Image.Source after its binding changes.
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Render).GetTask()
                .ConfigureAwait(false);
            await Task.Delay(RetirementDelayMilliseconds).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(image.Dispose, DispatcherPriority.Background).GetTask()
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Dispatcher shutdown owns process-lifetime resource cleanup.
        }
    }
}
