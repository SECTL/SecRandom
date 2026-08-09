using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Mixins;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

namespace SecRandom.Core.Controls;

public class MultiComboBoxItem : ContentControl
{
    private MultiComboBox? _parent;
    private static readonly Point s_invalidPoint = new(double.NaN, double.NaN);
    private Point _pointerDownPoint = s_invalidPoint;
    private bool _updateInternal;

    public static readonly StyledProperty<bool> IsSelectedProperty =
        AvaloniaProperty.Register<MultiComboBoxItem, bool>(nameof(IsSelected));

    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    static MultiComboBoxItem()
    {
        PressedMixin.Attach<MultiComboBoxItem>();
        FocusableProperty.OverrideDefaultValue<MultiComboBoxItem>(true);
        IsSelectedProperty.Changed.AddClassHandler<MultiComboBoxItem, bool>((item, args) =>
            item.OnSelectionChanged(args));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsSelectedProperty)
            PseudoClasses.Set(":selected", IsSelected);
    }

    private void OnSelectionChanged(AvaloniaPropertyChangedEventArgs<bool> args)
    {
        if (_updateInternal)
            return;
        if (this.FindLogicalAncestorOfType<MultiComboBox>() is not { SelectedItems: { } selected } parent)
            return;
        if (args.NewValue.Value)
        {
            if (!selected.Contains(DataContext))
                selected.Add(DataContext);
        }
        else
        {
            selected.Remove(DataContext);
        }
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        _parent = this.FindLogicalAncestorOfType<MultiComboBox>();
        if (IsSelected &&
            _parent?.SelectedItems is { } selected &&
            !selected.Contains(DataContext))
        {
            selected.Add(DataContext);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _pointerDownPoint = e.GetPosition(this);
        if (e.Handled)
            return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var p = e.GetCurrentPoint(this);
            if (p.Properties.PointerUpdateKind is PointerUpdateKind.LeftButtonPressed
                or PointerUpdateKind.RightButtonPressed)
            {
                if (p.Pointer.Type == PointerType.Mouse)
                {
                    SetCurrentValue(IsSelectedProperty, !IsSelected);
                    e.Handled = true;
                }
                else
                {
                    _pointerDownPoint = p.Position;
                }
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!e.Handled && !double.IsNaN(_pointerDownPoint.X) &&
            e.InitialPressMouseButton is MouseButton.Left or MouseButton.Right)
        {
            var point = e.GetCurrentPoint(this);
            if (new Rect(Bounds.Size).ContainsExclusive(point.Position) && e.Pointer.Type == PointerType.Touch)
            {
                SetCurrentValue(IsSelectedProperty, !IsSelected);
                e.Handled = true;
            }
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateSelection();
    }

    internal void UpdateSelection()
    {
        _updateInternal = true;
        try
        {
            if (_parent?.SelectedItems is { } selected)
                SetCurrentValue(IsSelectedProperty, selected.Contains(DataContext));
        }
        finally
        {
            _updateInternal = false;
        }
    }
}
