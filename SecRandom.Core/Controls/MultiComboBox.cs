using System;
using System.Collections;
using System.Collections.Specialized;
using System.Windows.Input;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SecRandom.Core.Controls;

public class MultiComboBox : ItemsControl
{
    public const string PART_BackgroundBorder = "PART_BackgroundBorder";
    public const string PC_DropDownOpen = ":dropdownopen";
    public const string PC_Empty = ":selection-empty";

    private static readonly ITemplate<Panel?> _defaultPanel =
        new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());

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
        SelectedItemsProperty.Changed.AddClassHandler<MultiComboBox, IList?>((box, args) =>
            box.OnSelectedItemsChanged(args));
    }

    public MultiComboBox()
    {
        SetCurrentValue(SelectedItemsProperty, new AvaloniaList<object>());
        if (SelectedItems is INotifyCollectionChanged c)
            c.CollectionChanged += OnSelectedItemsCollectionChanged;
        RemoveCommand = new MultiComboBoxRemoveCommand(this);
        AddHandler(PointerPressedEvent, OnBackgroundPointerPressed);
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
        SelectedItems?.Remove(o);
        if (o is null || Presenter?.Panel is not { } panel)
            return;
        foreach (var child in panel.Children)
        {
            if (child is MultiComboBoxItem item && ReferenceEquals(item.DataContext, o))
            {
                item.IsSelected = false;
                break;
            }
        }
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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsDropDownOpenProperty)
            PseudoClasses.Set(PC_DropDownOpen, IsDropDownOpen);
    }

    private void OnBackgroundPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        SetCurrentValue(IsDropDownOpenProperty, !IsDropDownOpen);
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
        if (args.OldValue.Value is INotifyCollectionChanged old)
            old.CollectionChanged -= OnSelectedItemsCollectionChanged;
        if (args.NewValue.Value is INotifyCollectionChanged @new)
            @new.CollectionChanged += OnSelectedItemsCollectionChanged;
        PseudoClasses.Set(PC_Empty, SelectedItems?.Count == 0);
    }

    private void OnSelectedItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        PseudoClasses.Set(PC_Empty, SelectedItems?.Count == 0);
        if (Presenter?.Panel is not { } panel)
            return;
        foreach (var container in panel.Children)
        {
            if (container is MultiComboBoxItem item)
                item.UpdateSelection();
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
