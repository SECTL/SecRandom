using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Helpers;

internal static class DrawAnimationHelper
{
    private const string ResultAnimationClass = "result-animation-item";
    private static readonly Dictionary<Control, CancellationTokenSource> AnimationTokens = [];

    public static async Task PreviewAsync(Control? target, DrawAnimationStyleMode style, int durationMs)
    {
        if (target is null)
            return;

        await EnsureLayoutAsync(target).ConfigureAwait(true);
        var resultItems = GetResultAnimationItems(target).ToList();
        if (style == DrawAnimationStyleMode.DirectRotate || durationMs <= 0)
        {
            ResetTarget(target);
            foreach (var item in resultItems)
                ResetTarget(item);
            return;
        }

        if (resultItems.Count == 0)
        {
            await AnimateAsync(target, style, durationMs).ConfigureAwait(true);
            return;
        }

        var tasks = resultItems.Select(item => AnimateAsync(item, style, durationMs));
        await Task.WhenAll(tasks).ConfigureAwait(true);
    }

    public static async Task RevealAsync(Control? target, bool enabled, DrawAnimationStyleMode style, int durationMs)
    {
        if (target is null)
            return;

        ResetTarget(target);
        foreach (var item in GetResultAnimationItems(target))
            ResetTarget(item);

        if (!enabled || style == DrawAnimationStyleMode.DirectRotate || durationMs <= 0)
        {
            return;
        }

        await EnsureLayoutAsync(target).ConfigureAwait(true);
        var resultItems = GetResultAnimationItems(target).ToList();
        if (resultItems.Count == 0)
        {
            await AnimateAsync(target, style, durationMs).ConfigureAwait(true);
            return;
        }

        var tasks = resultItems.Select((item, index) => AnimateAsync(
            item,
            style,
            durationMs,
            Math.Min(index * 45, 220)));
        await Task.WhenAll(tasks).ConfigureAwait(true);
    }

    private static async Task EnsureLayoutAsync(Control target)
    {
        await Dispatcher.UIThread.InvokeAsync(target.UpdateLayout, DispatcherPriority.Render).GetTask()
            .ConfigureAwait(true);
        await Task.Delay(16).ConfigureAwait(true);
        await Dispatcher.UIThread.InvokeAsync(target.UpdateLayout, DispatcherPriority.Render).GetTask()
            .ConfigureAwait(true);
    }

    private static async Task AnimateAsync(Control target, DrawAnimationStyleMode style, int durationMs, int delayMs = 0)
    {
        // Apply the initial transform/opacity state immediately so the item is hidden
        // in its start position before the stagger delay begins. This prevents the
        // item from remaining fully visible while waiting for its turn to animate.
        var token = ReplaceToken(target);
        var transformGroup = BuildTransformGroup(style);
        var startOpacity = ResolveStartOpacity(style);
        await ApplyAnimationFrameAsync(target, transformGroup, style, 0, startOpacity).ConfigureAwait(true);

        if (delayMs > 0)
        {
            try { await Task.Delay(delayMs, token).ConfigureAwait(true); }
            catch (OperationCanceledException) { return; }
        }

        var steps = Math.Max(1, durationMs / 16);
        try
        {
            for (var i = 0; i <= steps; i++)
            {
                token.ThrowIfCancellationRequested();

                var progress = (double)i / steps;
                await ApplyAnimationFrameAsync(target, transformGroup, style, progress, startOpacity)
                    .ConfigureAwait(true);

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

        ResetTarget(target);
    }

    private static Task ApplyAnimationFrameAsync(
        Control target,
        TransformGroup transformGroup,
        DrawAnimationStyleMode style,
        double progress,
        double startOpacity)
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            var eased = EaseOut(progress);
            target.RenderTransformOrigin = RelativePoint.Center;
            target.RenderTransform = transformGroup;
            target.Opacity = ResolveOpacity(style, progress, eased, startOpacity);

            if (transformGroup.Children[0] is TranslateTransform translate)
            {
                translate.X = ResolveTranslateX(style, progress, eased);
                translate.Y = ResolveTranslateY(style, eased);
            }

            if (transformGroup.Children[1] is ScaleTransform scale)
            {
                var scaleValue = ResolveScale(style, progress, eased);
                scale.ScaleX = scaleValue;
                scale.ScaleY = scaleValue;
            }

            if (transformGroup.Children[2] is RotateTransform rotate)
                rotate.Angle = ResolveAngle(style, progress, eased);
        }, DispatcherPriority.Render).GetTask();
    }

    private static IEnumerable<Control> GetResultAnimationItems(Control target)
    {
        return target.GetVisualDescendants()
            .OfType<Control>()
            .Where(control => control.Classes.Contains(ResultAnimationClass));
    }

    private static double EaseOut(double progress)
    {
        return 1 - Math.Pow(1 - progress, 3);
    }

    private static TransformGroup BuildTransformGroup(DrawAnimationStyleMode style)
    {
        // Initial values must match what each Resolve* function returns at progress=0,
        // because during stagger delay the control is visible in this initial state.
        // Any mismatch causes a visible jump when the stagger delay ends and frame 0 runs.
        //
        // FadeFloat:      Y=36, scale=0.86, angle=0,   X=0   — matches Resolve* at p=0
        // FlipRoll:       Y=18, scale=0.52, angle=+72, X=0   — cos(0)*72=+72
        // SpotlightSweep: Y=0,  scale=1.16, angle=0,   X=-84 — -84+84*0=-84
        // Fireworks:      Y=44, scale=1.0,  angle=0,   X=0   — 1+sin(0)*0.42=1.0
        // ShuffleDeck:    Y=12, scale=0.68, angle=0,   X=0   — sin(0)*34=0
        // WheelFreeze:    Y=28, scale=0.62, angle=-120,X=64  — already matched
        var (offsetY, scale, angle) = style switch
        {
            DrawAnimationStyleMode.FlipRoll      => (18.0, 0.52, 72.0),
            DrawAnimationStyleMode.SpotlightSweep => (0.0, 1.16, 0.0),
            DrawAnimationStyleMode.Fireworks     => (44.0, 1.0,  0.0),
            DrawAnimationStyleMode.ShuffleDeck   => (12.0, 0.68, 0.0),
            DrawAnimationStyleMode.WheelFreeze   => (28.0, 0.62, -120.0),
            _ => (36.0, 0.86, 0.0)
        };

        var initialX = style switch
        {
            DrawAnimationStyleMode.SpotlightSweep => -84.0,
            DrawAnimationStyleMode.ShuffleDeck    => 0.0,
            DrawAnimationStyleMode.WheelFreeze    => 64.0,
            _ => 0.0
        };

        return new TransformGroup
        {
            Children =
            [
                new TranslateTransform(initialX, offsetY),
                new ScaleTransform(scale, scale),
                new RotateTransform(angle)
            ]
        };
    }

    private static double ResolveOpacity(DrawAnimationStyleMode style, double progress, double eased)
    {
        return ResolveOpacity(style, progress, eased, ResolveStartOpacity(style));
    }

    private static double ResolveOpacity(DrawAnimationStyleMode style, double progress, double eased, double startOpacity)
    {
        return style switch
        {
            DrawAnimationStyleMode.Fireworks => Math.Min(1, startOpacity + (1 - startOpacity) * eased + Math.Sin(progress * Math.PI * 8) * 0.08),
            DrawAnimationStyleMode.SpotlightSweep => Math.Min(1, startOpacity + (1 - startOpacity) * eased),
            _ => startOpacity + (1 - startOpacity) * eased
        };
    }

    private static double ResolveStartOpacity(DrawAnimationStyleMode style)
    {
        // Start fully transparent so the fade-in is unmistakable regardless of style.
        return 0;
    }

    private static double ResolveTranslateX(DrawAnimationStyleMode style, double progress, double eased)
    {
        return style switch
        {
            DrawAnimationStyleMode.SpotlightSweep => -84 + 84 * eased,
            DrawAnimationStyleMode.ShuffleDeck => Math.Sin(progress * Math.PI * 5) * (1 - eased) * 130,
            DrawAnimationStyleMode.WheelFreeze => Math.Cos(progress * Math.PI * 4) * (1 - eased) * 64,
            _ => 0
        };
    }

    private static double ResolveTranslateY(DrawAnimationStyleMode style, double eased)
    {
        var offset = style switch
        {
            DrawAnimationStyleMode.FadeFloat => 36,
            DrawAnimationStyleMode.Fireworks => 44,
            DrawAnimationStyleMode.WheelFreeze => 28,
            DrawAnimationStyleMode.FlipRoll => 18,
            DrawAnimationStyleMode.ShuffleDeck => 12,
            _ => 0
        };
        return (1 - eased) * offset;
    }

    private static double ResolveScale(DrawAnimationStyleMode style, double progress, double eased)
    {
        return style switch
        {
            DrawAnimationStyleMode.Fireworks => 1 + Math.Sin(progress * Math.PI * 5) * (1 - eased) * 0.42,
            DrawAnimationStyleMode.FlipRoll => 0.52 + 0.48 * eased,
            DrawAnimationStyleMode.WheelFreeze => 0.62 + 0.38 * eased,
            DrawAnimationStyleMode.ShuffleDeck => 0.68 + 0.32 * eased,
            DrawAnimationStyleMode.SpotlightSweep => 1.16 - 0.16 * eased,
            _ => 0.86 + 0.14 * eased
        };
    }

    private static double ResolveAngle(DrawAnimationStyleMode style, double progress, double eased)
    {
        return style switch
        {
            DrawAnimationStyleMode.FlipRoll => Math.Cos(progress * Math.PI * 4) * (1 - eased) * 72,
            DrawAnimationStyleMode.Fireworks => Math.Sin(progress * Math.PI * 6) * (1 - eased) * 26,
            DrawAnimationStyleMode.ShuffleDeck => Math.Sin(progress * Math.PI * 5) * (1 - eased) * 34,
            DrawAnimationStyleMode.WheelFreeze => (1 - eased) * -120,
            _ => 0
        };
    }

    private static void ResetTarget(Control target)
    {
        Cancel(target);
        target.Opacity = 1;
        target.RenderTransformOrigin = RelativePoint.Center;
        target.RenderTransform = null;
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
