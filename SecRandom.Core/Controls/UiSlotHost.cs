using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services.Views;

namespace SecRandom.Core.Controls;

/// <summary>
///     A named place in the shell where plugins may inject content. The host declares the slot by wrapping the
///     area with this control and setting <see cref="SlotId" /> to one of the
///     <see cref="HostUiSlots" /> constants; plugin contributions are then appended, prepended or used to
///     replace the declared content.
/// </summary>
/// <remarks>
///     Content declared inline in XAML counts as the slot's default content and stays in place unless a
///     contribution uses <see cref="UiSlotKind.Replace" /> or <see cref="UiSlotKind.Hide" />.
/// </remarks>
public class UiSlotHost : Panel
{
    public static readonly StyledProperty<string?> SlotIdProperty =
        AvaloniaProperty.Register<UiSlotHost, string?>(nameof(SlotId));

    private readonly List<Control> _injected = [];
    private bool _attached;
    private bool? _defaultVisible;
    private IUiContributionService? _service;

    /// <summary>Id of the slot this host renders. See <see cref="HostUiSlots" />.</summary>
    public string? SlotId
    {
        get => GetValue(SlotIdProperty);
        set => SetValue(SlotIdProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _attached = true;
        _defaultVisible ??= IsVisible;
        Hook();
        Rebuild();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;

        if (_service is not null)
            _service.Changed -= OnContributionsChanged;

        _service = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SlotIdProperty)
            Rebuild();
    }

    private void Hook()
    {
        _service ??= IAppHost.TryGetService<IUiContributionService>();

        if (_service is null)
            return;

        _service.Changed -= OnContributionsChanged;
        _service.Changed += OnContributionsChanged;
    }

    private void OnContributionsChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
            Rebuild();
        else
            Dispatcher.UIThread.Post(Rebuild);
    }

    private void Rebuild()
    {
        foreach (var control in _injected)
            Children.Remove(control);
        _injected.Clear();

        var slotId = SlotId;
        if (!_attached || string.IsNullOrWhiteSpace(slotId))
            return;

        var service = _service ??= IAppHost.TryGetService<IUiContributionService>();
        if (service is null)
            return;

        if (service.IsSlotHidden(slotId!))
        {
            IsVisible = false;
            return;
        }

        IsVisible = _defaultVisible ?? true;

        var context = new UiSlotContext(slotId, DataContext);

        if (service.BuildReplacement(slotId, context) is { } replacement)
        {
            Children.Add(replacement);
            _injected.Add(replacement);
            return;
        }

        var contributions = service.GetSlotContributions(slotId);

        // Prepends walk backwards so the highest priority contribution ends up first.
        var prepends = contributions.Where(contribution => contribution.Kind == UiSlotKind.Prepend).ToArray();
        for (var index = prepends.Length - 1; index >= 0; index--)
        {
            if (Build(prepends[index], context) is not { } content)
                continue;

            Children.Insert(0, content);
            _injected.Add(content);
        }

        foreach (var contribution in contributions.Where(contribution => contribution.Kind == UiSlotKind.Append))
        {
            if (Build(contribution, context) is not { } content)
                continue;

            Children.Add(content);
            _injected.Add(content);
        }
    }

    private static Control? Build(IUiContentContribution contribution, UiSlotContext context)
    {
        try
        {
            return contribution.CreateContent(context);
        }
        catch
        {
            // The service already logs failures for replacements; a broken prepend/append must only cost
            // its own visual.
            return null;
        }
    }
}
