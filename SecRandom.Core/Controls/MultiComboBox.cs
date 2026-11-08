using System;
using System.Collections;
using System.Collections.Specialized;
using System.Windows.Input;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Utils;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace SecRandom.Core.Controls;

public class MultiComboBox : ItemsControl
{
    public const string PART_BackgroundBorder = "PART_BackgroundBorder";
    public const string PART_SelectionBox = "PART_SelectionBox";
    public const string PC_DropDownOpen = ":dropdownopen";
    public const string PC_Empty = ":selection-empty";
    public const string PC_Pressed = ":pressed";

    private static readonly ITemplate<Panel?> _defaultPanel =
        new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());

    /// <summary>
    ///     The selection box's own copy of <see cref="SelectedItems" />. The template binds its
    ///     tag ItemsControl to this collection rather than to the bound <see cref="SelectedItems" />
    ///     collection.
    /// </summary>
    /// <remarks>
    ///     Pages subscribe to <see cref="SelectedItems" /> before the control's template exists
    ///     (they wire their handler in the page constructor) and rebuild that same collection from
    ///     the change handler. Because such a listener runs before the tag panel's container
    ///     generator, it can mutate the collection while a change is still being dispatched, and
    ///     Avalonia's non-virtualizing <c>PanelContainerGenerator</c> indexes the panel's containers
    ///     with the notification index — a re-entrant mutation then threw
    ///     <see cref="ArgumentOutOfRangeException" />. Notifications raised by this control-owned
    ///     collection are always complete and never raised from inside another collection change,
    ///     which is what keeps that container list in step.
    /// </remarks>
    private readonly AvaloniaList<object?> _selectionView = new();

    private INotifyCollectionChanged? _subscribedSelection;
    private bool _selectionViewDirty;
    private bool _syncingSelectionView;
    private Popup? _popup;

    public static readonly StyledProperty<bool> IsDropDownOpenProperty =
        ComboBox.IsDropDownOpenProperty.AddOwner<MultiComboBox>();

    public static readonly StyledProperty<double> MaxDropDownHeightProperty =
        AvaloniaProperty.Register<MultiComboBox, double>(nameof(MaxDropDownHeight));

    public static readonly StyledProperty<double> MaxSelectionBoxHeightProperty =
        AvaloniaProperty.Register<MultiComboBox, double>(nameof(MaxSelectionBoxHeight));

    public static readonly StyledProperty<IList?> SelectedItemsProperty =
        AvaloniaProperty.Register<MultiComboBox, IList?>(nameof(SelectedItems));

    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        TextBox.PlaceholderTextProperty.AddOwner<MultiComboBox>();

    public static readonly StyledProperty<object?> InnerLeftContentProperty =
        AvaloniaProperty.Register<MultiComboBox, object?>(nameof(InnerLeftContent));

    public static readonly StyledProperty<object?> InnerRightContentProperty =
        AvaloniaProperty.Register<MultiComboBox, object?>(nameof(InnerRightContent));

    static MultiComboBox()
    {
        FocusableProperty.OverrideDefaultValue<MultiComboBox>(true);
        ItemsPanelProperty.OverrideDefaultValue<MultiComboBox>(_defaultPanel);
        PlatformFeedback.FeedbackTypeProperty.OverrideDefaultValue<MultiComboBox>(FeedbackType.Auto);
        SelectedItemsProperty.Changed.AddClassHandler<MultiComboBox, IList?>((box, args) =>
            box.OnSelectedItemsChanged(args));
    }

    public MultiComboBox()
    {
        SetCurrentValue(SelectedItemsProperty, new AvaloniaList<object>());
        RemoveCommand = new MultiComboBoxRemoveCommand(this);
    }

    public bool IsDropDownOpen
    {
        get => GetValue(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    public double MaxDropDownHeight
    {
        get => GetValue(MaxDropDownHeightProperty);
        set => SetValue(MaxDropDownHeightProperty, value);
    }

    public double MaxSelectionBoxHeight
    {
        get => GetValue(MaxSelectionBoxHeightProperty);
        set => SetValue(MaxSelectionBoxHeightProperty, value);
    }

    public IList? SelectedItems
    {
        get => GetValue(SelectedItemsProperty);
        set => SetValue(SelectedItemsProperty, value);
    }

    public string? PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public object? InnerLeftContent
    {
        get => GetValue(InnerLeftContentProperty);
        set => SetValue(InnerLeftContentProperty, value);
    }

    public object? InnerRightContent
    {
        get => GetValue(InnerRightContentProperty);
        set => SetValue(InnerRightContentProperty, value);
    }

    public ICommand RemoveCommand { get; }

    public void Remove(object? o)
    {
        if (o is StyledElement s)
            o = s.DataContext;

        // Deselect the option container without letting it write back into SelectedItems: the single
        // removal below is the source of truth. Writing the collection from inside that removal raised a
        // second change notification while the first one was still being handled, which desynchronized
        // the tag panel's container tracking.
        if (o is not null && Presenter?.Panel is { } panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is MultiComboBoxItem item && ReferenceEquals(item.DataContext, o))
                {
                    item.ClearSelection();
                    break;
                }
            }
        }

        SelectedItems?.Remove(o);
    }

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        recycleKey = item;
        return item is not MultiComboBoxItem;
    }

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
    {
        return new MultiComboBoxItem();
    }

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        if (item is MultiComboBoxItem containerItem)
        {
            container.DataContext = containerItem.Content;
            return;
        }

        container.DataContext = item;
        base.PrepareContainerForItemOverride(container, item, index);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _popup = e.NameScope.Find<Popup>("PART_Popup");

        var selectionBox = e.NameScope.Find<ItemsControl>(PART_SelectionBox);
        if (selectionBox is null)
            return;

        selectionBox.ItemsSource = _selectionView;
        SyncSelectionView();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsDropDownOpenProperty)
            PseudoClasses.Set(PC_DropDownOpen, IsDropDownOpen);
    }

    /// <summary>
    ///     与 Avalonia <see cref="ComboBox" /> 相同的指针节奏：按下只进入按压态（:pressed），
    ///     抬起才切换下拉。按下瞬间展开下拉时，同一次触摸的抬起仍会落在刚出现的下拉层上，
    ///     轻触关闭把这次手势当作下拉框之外的按压，下拉框随即收起，用户点不到里面的复选框；
    ///     改为抬起展开后，打开手势已经结束，这条收起路径不存在。
    /// </summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.Handled && e.Source is Visual source)
        {
            if (_popup?.IsInsidePopup(source) == true)
            {
                e.Handled = true;
                return;
            }
        }

        if (IsDropDownOpen)
        {
            // 下拉展开时按下控件视为关闭，而不是再切回打开
            SetCurrentValue(IsDropDownOpenProperty, false);
            e.Handled = true;
        }
        else
        {
            PseudoClasses.Set(PC_Pressed, true);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!e.Handled && e.Source is Visual source
            && _popup?.IsInsidePopup(source) != true
            && PseudoClasses.Contains(PC_Pressed))
        {
            SetCurrentValue(IsDropDownOpenProperty, !IsDropDownOpen);
            this.PerformFeedback(FeedbackAction.Click);
            e.Handled = true;
        }

        PseudoClasses.Set(PC_Pressed, false);
        base.OnPointerReleased(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
            return;

        if ((e.Key == Key.F4 && !e.KeyModifiers.HasFlag(KeyModifiers.Alt)) ||
            ((e.Key == Key.Down || e.Key == Key.Up) && e.KeyModifiers.HasFlag(KeyModifiers.Alt)))
        {
            SetCurrentValue(IsDropDownOpenProperty, !IsDropDownOpen);
            e.Handled = true;
        }
        else if (IsDropDownOpen && e.Key == Key.Escape)
        {
            SetCurrentValue(IsDropDownOpenProperty, false);
            e.Handled = true;
        }
        else if (!IsDropDownOpen && (e.Key == Key.Return || e.Key == Key.Space))
        {
            SetCurrentValue(IsDropDownOpenProperty, true);
            e.Handled = true;
        }
        else if (IsDropDownOpen && e.Key == Key.Tab)
        {
            SetCurrentValue(IsDropDownOpenProperty, false);
            e.Handled = true;
        }
    }

    private void OnSelectedItemsChanged(AvaloniaPropertyChangedEventArgs<IList?> args)
    {
        // A styled-property class handler can observe the same collection from more than one change
        // (the constructor installs the default list, a page binding replaces it), so track the
        // subscribed collection instead of blindly unsubscribing/subscribing per change.
        var next = args.NewValue.Value as INotifyCollectionChanged;
        if (!ReferenceEquals(_subscribedSelection, next))
        {
            if (_subscribedSelection is not null)
                _subscribedSelection.CollectionChanged -= OnSelectedItemsCollectionChanged;
            _subscribedSelection = next;
            if (next is not null)
                next.CollectionChanged += OnSelectedItemsCollectionChanged;
        }

        PseudoClasses.Set(PC_Empty, SelectedItems?.Count == 0);
        SyncSelectionView();
    }

    private void OnSelectedItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        PseudoClasses.Set(PC_Empty, SelectedItems?.Count == 0);
        SyncSelectionView();
        if (Presenter?.Panel is not { } panel)
            return;
        foreach (var container in panel.Children)
        {
            if (container is MultiComboBoxItem item)
                item.UpdateSelection();
        }
    }

    /// <summary>
    ///     Brings the selection box's copy of the selection in line with <see cref="SelectedItems" />.
    /// </summary>
    private void SyncSelectionView()
    {
        if (_syncingSelectionView)
        {
            _selectionViewDirty = true;
            return;
        }

        _syncingSelectionView = true;
        try
        {
            do
            {
                _selectionViewDirty = false;
                ApplySelectionView();
            }
            while (_selectionViewDirty);
        }
        finally
        {
            _syncingSelectionView = false;
        }
    }

    private void ApplySelectionView()
    {
        var selected = SelectedItems;
        var count = selected?.Count ?? 0;

        if (IsSameSelection())
            return;

        _selectionView.Clear();
        for (var i = 0; i < count; i++)
            _selectionView.Add(selected![i]);

        bool IsSameSelection()
        {
            if (_selectionView.Count != count)
                return false;

            for (var i = 0; i < count; i++)
            {
                if (!ReferenceEquals(_selectionView[i], selected![i]))
                    return false;
            }

            return true;
        }
    }

    private sealed class MultiComboBoxRemoveCommand(MultiComboBox owner) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => owner.Remove(parameter);
    }
}
