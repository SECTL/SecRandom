using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace SecRandom.Core.Behaviors;

/// <summary>
///     <see cref="StackPanel" /> 进入动画行为。
/// </summary>
public class StackPanelIntroAnimationBehavior
{
    public static readonly AttachedProperty<bool> IsIntroAnimationEnabledProperty =
        AvaloniaProperty.RegisterAttached<StackPanelIntroAnimationBehavior, Panel, bool>("IsIntroAnimationEnabled");

    public static readonly AttachedProperty<bool> IsAnimationPlayedProperty =
        AvaloniaProperty.RegisterAttached<StackPanelIntroAnimationBehavior, Control, bool>("IsAnimationPlayed");

    public static readonly AttachedProperty<bool> CanPlayAnimationProperty =
        AvaloniaProperty.RegisterAttached<StackPanelIntroAnimationBehavior, Control, bool>("CanPlayAnimation");

    public static readonly AttachedProperty<bool> IsAnimationPlayingStartedProperty =
        AvaloniaProperty.RegisterAttached<StackPanelIntroAnimationBehavior, Panel, bool>("IsAnimationPlayingStarted");

    static StackPanelIntroAnimationBehavior()
    {
        IsIntroAnimationEnabledProperty.Changed.AddClassHandler<Panel>(HandleIsIntroAnimationChanged);
    }

    public static void SetIsIntroAnimationEnabled(Panel obj, bool value)
    {
        obj.SetValue(IsIntroAnimationEnabledProperty, value);
    }

    public static bool GetIsIntroAnimationEnabled(Panel obj)
    {
        return obj.GetValue(IsIntroAnimationEnabledProperty);
    }

    public static void SetIsAnimationPlayed(Control obj, bool value)
    {
        obj.SetValue(IsAnimationPlayedProperty, value);
    }

    public static bool GetIsAnimationPlayed(Control obj)
    {
        return obj.GetValue(IsAnimationPlayedProperty);
    }

    public static void SetCanPlayAnimation(Control obj, bool value)
    {
        obj.SetValue(CanPlayAnimationProperty, value);
    }

    public static bool GetCanPlayAnimation(Control obj)
    {
        return obj.GetValue(CanPlayAnimationProperty);
    }

    public static void SetIsAnimationPlayingStarted(Panel obj, bool value)
    {
        obj.SetValue(IsAnimationPlayingStartedProperty, value);
    }

    public static bool GetIsAnimationPlayingStarted(Panel obj)
    {
        return obj.GetValue(IsAnimationPlayingStartedProperty);
    }

    private static void HandleIsIntroAnimationChanged(Panel panel, AvaloniaPropertyChangedEventArgs args)
    {
        if (!GetIsIntroAnimationEnabled(panel) || GetIsAnimationPlayingStarted(panel)) return;

        var animable = panel.Children
            .Where(x => x.RenderTransform == null && x.IsVisible)
            .ToList();
        foreach (var c in animable) SetCanPlayAnimation(c, true);

        panel.Loaded += (_, _) => { StartAnimation(panel); };
    }

    private static void StartAnimation(Panel panel)
    {
        if (!GetIsIntroAnimationEnabled(panel) || GetIsAnimationPlayingStarted(panel)) return;

        var targets = panel.Children.Where(GetCanPlayAnimation).ToList();
        var index = 0;
        var timer = new DispatcherTimer(DispatcherPriority.Send)
        {
            Interval = TimeSpan.FromSeconds(0.025)
        };
        timer.Tick += (sender, args) =>
        {
            if (index >= targets.Count)
            {
                timer.Stop();
                return;
            }

            SetIsAnimationPlayed(targets[index], true);
            index++;
        };
        SetIsAnimationPlayingStarted(panel, true);
        timer.Start();
    }
}