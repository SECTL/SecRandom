using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;

namespace SecRandom.Core.Controls;

/// <summary>
/// A repeat button whose cadence accelerates while the primary pointer remains pressed.
/// RepeatButton supplies the platform-neutral pointer/touch repeat behavior; this control only
/// changes its interval as the hold duration grows.
/// </summary>
public sealed class AcceleratingRepeatButton : RepeatButton
{
    internal const int InitialDelayMilliseconds = 360;
    internal const int InitialIntervalMilliseconds = 140;
    internal const int MinimumIntervalMilliseconds = 24;

    private readonly Stopwatch _holdDuration = new();
    private IPointer? _activePointer;

    protected override Type StyleKeyOverride => typeof(Button);

    public AcceleratingRepeatButton()
    {
        // Trigger the first command on press so releasing after a repeat does not add a
        // duplicate increment.
        ClickMode = ClickMode.Press;
        Delay = InitialDelayMilliseconds;
        Interval = InitialIntervalMilliseconds;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += OnPointerCaptureLost;
        Click += OnClick;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_activePointer is null || !ReferenceEquals(e.Pointer, _activePointer))
            return;

        ResetRepeatState();
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        ResetRepeatState();
    }

    private void OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_holdDuration.IsRunning)
            return;

        Interval = CalculateInterval(_holdDuration.Elapsed);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        var isPrimaryPointer = e.Pointer.Type == PointerType.Touch || point.Properties.IsLeftButtonPressed;
        if (!isPrimaryPointer)
        {
            base.OnPointerPressed(e);
            return;
        }

        ResetRepeatState();
        _activePointer = e.Pointer;
        _holdDuration.Restart();
        base.OnPointerPressed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Space && IsEffectivelyEnabled && !_holdDuration.IsRunning)
        {
            ResetRepeatState();
            _holdDuration.Restart();
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (e.Key == Key.Space)
            ResetRepeatState();
    }

    internal static int CalculateInterval(TimeSpan heldFor)
    {
        // Exponential decay gives a smooth acceleration while the lower bound keeps the UI
        // responsive and avoids flooding commands with an unbounded timer rate.
        var seconds = Math.Max(0, heldFor.TotalSeconds);
        var interval = InitialIntervalMilliseconds * Math.Pow(0.62, seconds / 0.8);
        return Math.Max(MinimumIntervalMilliseconds, (int)Math.Round(interval));
    }

    private void ResetRepeatState()
    {
        _activePointer = null;
        _holdDuration.Reset();
        Interval = InitialIntervalMilliseconds;
    }
}
