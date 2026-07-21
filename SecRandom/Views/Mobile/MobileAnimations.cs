using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SecRandom.Mobile.Views;

/// <summary>
/// 移动端轻量动画原语（FluentAvalonia 风格的过渡与结果动效，非桌面重型滚动动画）。
/// 所有原语都带打断语义：对同一控件启动新动画会先取消旧动画，控件从可视树分离时也会自动取消。
/// 除 <see cref="StartNameRoll"/> 外均要求调用方处于 UI 线程。
/// </summary>
internal static class MobileAnimations
{
    private sealed class AnimationSlot
    {
        internal readonly CancellationTokenSource Source = new();
    }

    private static readonly ConditionalWeakTable<Control, AnimationSlot> s_active = new();

    /// <summary>取消控件上正在运行的动画并把视觉状态复位（不透明、无变换）。</summary>
    internal static void Cancel(Control control)
    {
        if (s_active.TryGetValue(control, out AnimationSlot? slot))
        {
            slot.Source.Cancel();
            s_active.Remove(control);
        }
    }

    /// <summary>
    /// 页面内容进入过渡：淡入。用于各页面首次渲染与设置页进入。
    /// 控件尚未附加到可视树时会推迟到首次附加后再播放。
    /// </summary>
    internal static void PlayPageEnter(Control control, int durationMs = 320)
    {
        RunWhenAttached(control, () =>
        {
            CancellationToken token = Begin(control);
            TimeSpan duration = TimeSpan.FromMilliseconds(durationMs);
            var easing = new CircularEaseOut();
            _ = RunAll(control, token, Fade(control, 0, 1, duration, easing, token));
        });
    }

    /// <summary>
    /// 抽取结果揭示动效：结果文本/卡片淡入（默认 300ms，CircleEaseOut）。
    /// </summary>
    internal static void PlayResultReveal(Control control, int durationMs = 300)
    {
        RunWhenAttached(control, () =>
        {
            CancellationToken token = Begin(control);
            TimeSpan duration = TimeSpan.FromMilliseconds(durationMs);
            var easing = new CircularEaseOut();
            _ = RunAll(control, token, Fade(control, 0, 1, duration, easing, token));
        });
    }

    /// <summary>
    /// 状态切换 CrossFade：先淡出旧内容，执行替换回调，再淡入。
    /// 适用于按钮启用/禁用外观切换、内容区域整体替换。
    /// </summary>
    internal static async Task CrossFadeAsync(Control target, Action swap, int fadeOutMs = 120, int fadeInMs = 180)
    {
        CancellationToken token = Begin(target);
        try
        {
            await Fade(target, 1, 0, TimeSpan.FromMilliseconds(fadeOutMs), new CubicEaseIn(), token)
                .ConfigureAwait(true);
            if (token.IsCancellationRequested)
                return;
            swap();
            await Fade(target, 0, 1, TimeSpan.FromMilliseconds(fadeInMs), new CubicEaseOut(), token)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // 被新动画打断，由新动画负责最终状态。
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
        }
        finally
        {
            FinishOnUi(target, token);
        }
    }

    /// <summary>CrossFade 的即发即弃版本，供事件处理器使用。</summary>
    internal static void CrossFade(Control target, Action swap, int fadeOutMs = 120, int fadeInMs = 180)
        => _ = CrossFadeAsync(target, swap, fadeOutMs, fadeInMs);

    /// <summary>
    /// 抽取进行中的候选人名快速滚动微动效：按间隔随机切换候选名并做轻微透明度脉冲。
    /// 返回的 CancellationTokenSource 与 <see cref="Cancel"/> 都可终止滚动；
    /// 停止后调用方负责写入最终结果（建议配合 <see cref="PlayResultReveal"/>）。
    /// </summary>
    internal static CancellationTokenSource StartNameRoll(TextBlock target, IReadOnlyList<string> names, int intervalMs = 70)
    {
        Cancel(target);
        var slot = new AnimationSlot();
        s_active.Add(target, slot);
        CancellationTokenSource source = slot.Source;
        target.DetachedFromVisualTree += OnDetached;

        _ = Task.Run(async () =>
        {
            try
            {
                while (!source.IsCancellationRequested && names.Count > 0)
                {
                    string name = names[Random.Shared.Next(names.Count)];
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        target.Text = name;
                        target.Opacity = 0.55;
                    });
                    await Task.Delay(Math.Max(16, intervalMs / 2), source.Token);
                    if (source.IsCancellationRequested)
                        break;
                    await Dispatcher.UIThread.InvokeAsync(() => target.Opacity = 1d);
                    await Task.Delay(Math.Max(16, intervalMs / 2), source.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常打断。
            }
            catch (Exception)
            {
                // 视图销毁/UI 线程退出期间滚动微动效静默终止，不影响抽取结果。
            }
            finally
            {
                Dispatcher.UIThread.Post(() =>
                {
                    target.Opacity = 1d;
                    if (s_active.TryGetValue(target, out AnimationSlot? current) && ReferenceEquals(current, slot))
                        s_active.Remove(target);
                    target.DetachedFromVisualTree -= OnDetached;
                });
            }
        });
        return source;
    }

    private static CancellationToken Begin(Control control)
    {
        Cancel(control);
        var slot = new AnimationSlot();
        s_active.Add(control, slot);
        control.DetachedFromVisualTree += OnDetached;
        return slot.Source.Token;
    }

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control)
        {
            control.DetachedFromVisualTree -= OnDetached;
            Cancel(control);
        }
    }

    private static void FinishOnUi(Control control, CancellationToken token)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (token.IsCancellationRequested)
                return;
            s_active.Remove(control);
            control.DetachedFromVisualTree -= OnDetached;
            control.Opacity = 1;
            control.RenderTransform = null;
        });
    }

    private static async Task RunAll(Control control, CancellationToken token, params Task[] runs)
    {
        try
        {
            await Task.WhenAll(runs).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // 被新动画打断，由新动画负责最终状态。
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
        }
        finally
        {
            FinishOnUi(control, token);
        }
    }

    private static void RunWhenAttached(Control control, Action play)
    {
        if (control.IsAttachedToVisualTree())
        {
            play();
            return;
        }

        // 未挂树时动画不可见，推迟到首次附加后播放。
        void Handler(object? sender, VisualTreeAttachmentEventArgs e)
        {
            control.AttachedToVisualTree -= Handler;
            play();
        }
        control.AttachedToVisualTree += Handler;
    }

    private static Task Fade(Animatable target, double from, double to, TimeSpan duration, Easing easing, CancellationToken token)
        => Run(new Animation
        {
            Duration = duration,
            Easing = easing,
            Children = { Frame(0, Visual.OpacityProperty, from), Frame(1, Visual.OpacityProperty, to) }
        }, target, token);

    private static async Task Run(Animation animation, Animatable target, CancellationToken token)
    {
        try
        {
            await animation.RunAsync(target, token).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            // A decorative animation must never take down the application.
            System.Diagnostics.Debug.WriteLine(exception);
        }
    }

    private static KeyFrame Frame(double cue, AvaloniaProperty property, object value)
    {
        var frame = new KeyFrame { Cue = new Cue(cue) };
        frame.Setters.Add(new Setter(property, value));
        return frame;
    }
}
