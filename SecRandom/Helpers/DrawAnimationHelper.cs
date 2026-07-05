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
            return;

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
        var token = ReplaceToken(target);
        var transformGroup = BuildTransformGroup(style);
        var startOpacity = ResolveStartOpacity(style);
        await ApplyAnimationFrameAsync(target, transformGroup, style, 0, startOpacity).ConfigureAwait(true);

        if (delayMs > 0)
        {
            try
            {
                await Task.Delay(delayMs, token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
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
        var (offsetY, scale, angle) = style switch
        {
            DrawAnimationStyleMode.FadeFloat => (34.0, 0.96, 0.0),
            DrawAnimationStyleMode.HorizontalShake => (4.0, 1.0, 0.0),
            _ => (0.0, 1.0, 0.0)
        };

        return new TransformGroup
        {
            Children =
            [
                new TranslateTransform(ResolveTranslateX(style, 0, 0), offsetY),
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
        return startOpacity + (1 - startOpacity) * eased;
    }

    private static double ResolveStartOpacity(DrawAnimationStyleMode style)
    {
        return 0;
    }

    private static double ResolveTranslateX(DrawAnimationStyleMode style, double progress, double eased)
    {
        return style switch
        {
            DrawAnimationStyleMode.HorizontalShake => Math.Sin(progress * Math.PI * 6 - Math.PI / 2) * (1 - eased) * 34,
            _ => 0
        };
    }

    private static double ResolveTranslateY(DrawAnimationStyleMode style, double eased)
    {
        return style == DrawAnimationStyleMode.FadeFloat ? (1 - eased) * 34 : 0;
    }

    private static double ResolveScale(DrawAnimationStyleMode style, double progress, double eased)
    {
        return style switch
        {
            DrawAnimationStyleMode.FadeFloat => 0.96 + 0.04 * eased,
            DrawAnimationStyleMode.HorizontalShake => 0.98 + 0.02 * eased,
            _ => 1
        };
    }

    private static double ResolveAngle(DrawAnimationStyleMode style, double progress, double eased)
    {
        return style == DrawAnimationStyleMode.HorizontalShake
            ? Math.Sin(progress * Math.PI * 6 - Math.PI / 2) * (1 - eased) * 3
            : 0;
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
