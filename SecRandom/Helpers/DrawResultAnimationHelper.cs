using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;

namespace SecRandom.Helpers;

internal static class DrawResultAnimationHelper
{
    private static readonly Dictionary<Control, CancellationTokenSource> AnimationTokens = [];

    public static async Task RevealAsync(Control? target, bool enabled, int durationMs)
    {
        if (target is null)
            return;

        if (!enabled)
        {
            Cancel(target);
            target.Opacity = 1;
            target.RenderTransform = null;
            return;
        }

        durationMs = Math.Clamp(durationMs, 80, 3000);
        await AnimateAsync(target, 0, 18, durationMs).ConfigureAwait(true);
    }

    public static async Task PreviewSwapAsync(Control? target, int durationMs)
    {
        if (target is null)
            return;

        durationMs = Math.Clamp(durationMs, 60, 240);
        await AnimateAsync(target, 0.35, 10, durationMs).ConfigureAwait(true);
    }

    private static async Task AnimateAsync(Control target, double startOpacity, double startOffsetY, int durationMs)
    {
        var token = ReplaceToken(target);
        var transform = new TranslateTransform(0, startOffsetY);
        target.RenderTransform = transform;
        target.Opacity = startOpacity;

        var steps = Math.Max(1, durationMs / 16);
        try
        {
            for (var i = 0; i <= steps; i++)
            {
                token.ThrowIfCancellationRequested();

                var progress = (double)i / steps;
                var eased = 1 - Math.Pow(1 - progress, 3);
                target.Opacity = startOpacity + (1 - startOpacity) * eased;
                transform.Y = (1 - eased) * startOffsetY;
                await Task.Delay(Math.Max(1, durationMs / steps), token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            ClearToken(target, token);
        }

        target.Opacity = 1;
        transform.Y = 0;
    }

    private static CancellationToken ReplaceToken(Control target)
    {
        lock (AnimationTokens)
        {
            if (AnimationTokens.Remove(target, out var existing))
            {
                existing.Cancel();
                existing.Dispose();
            }

            var current = new CancellationTokenSource();
            AnimationTokens[target] = current;
            return current.Token;
        }
    }

    private static void ClearToken(Control target, CancellationToken token)
    {
        lock (AnimationTokens)
        {
            if (!AnimationTokens.TryGetValue(target, out var current) || current.Token != token)
                return;

            AnimationTokens.Remove(target);
            current.Dispose();
        }
    }

    private static void Cancel(Control target)
    {
        lock (AnimationTokens)
        {
            if (!AnimationTokens.Remove(target, out var current))
                return;

            current.Cancel();
            current.Dispose();
        }
    }
}
