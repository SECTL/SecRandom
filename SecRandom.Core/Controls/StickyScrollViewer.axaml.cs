using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data.Converters;

namespace SecRandom.Core.Controls;

/// <summary>
///     粘滞滚动控件
/// </summary>
[TemplatePart(MainScrollViewerName, typeof(ScrollViewer))]
[TemplatePart(HeaderName, typeof(ContentPresenter))]
[PseudoClasses(":header-collapsed")]
public class StickyScrollViewer : ContentControl
{
    private const string MainScrollViewerName = "PART_MainScrollViewer";
    private const string HeaderName = "PART_Header";

    public static readonly AttachedProperty<double> HeaderFadingOpacityProperty =
        AvaloniaProperty.RegisterAttached<StickyScrollViewer, Control, double>("HeaderFadingOpacity", 1.0,
            true);

    public static readonly StyledProperty<double> CollapsedHeightProperty =
        AvaloniaProperty.Register<StickyScrollViewer, double>(
            nameof(CollapsedHeight), 150);

    public static readonly StyledProperty<object?> HeaderContentProperty =
        AvaloniaProperty.Register<StickyScrollViewer, object?>(
            nameof(HeaderContent));

    public static readonly StyledProperty<IDataTemplate?> HeaderTemplateProperty =
        AvaloniaProperty.Register<StickyScrollViewer, IDataTemplate?>(
            nameof(HeaderTemplate));

    public static readonly StyledProperty<double> RealHeaderHeightProperty =
        AvaloniaProperty.Register<StickyScrollViewer, double>(
            nameof(RealHeaderHeight));

    public static readonly StyledProperty<double> SpaceAreaHeightProperty =
        AvaloniaProperty.Register<StickyScrollViewer, double>(
            nameof(SpaceAreaHeight));

    private ContentPresenter? _header;

    private ScrollViewer? _mainScrollViewer;

    public static FuncValueConverter<Vector, Thickness> ScrollViewerOffsetToHeaderMarginConverter { get; } =
        new(x => new Thickness(0, -x.Y, 0, 0));

    public static FuncValueConverter<double, Thickness> DoubleToHeaderMarginConverter { get; } =
        new(x => new Thickness(0, -x, 0, 0));

    public static FuncValueConverter<double, GridLength> DoubleToGridLengthConverter { get; } =
        new(x => new GridLength(x, GridUnitType.Pixel));

    public double CollapsedHeight
    {
        get => GetValue(CollapsedHeightProperty);
        set => SetValue(CollapsedHeightProperty, value);
    }

    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    public IDataTemplate? HeaderTemplate
    {
        get => GetValue(HeaderTemplateProperty);
        set => SetValue(HeaderTemplateProperty, value);
    }

    public double RealHeaderHeight
    {
        get => GetValue(RealHeaderHeightProperty);
        set => SetValue(RealHeaderHeightProperty, value);
    }

    public double SpaceAreaHeight
    {
        get => GetValue(SpaceAreaHeightProperty);
        set => SetValue(SpaceAreaHeightProperty, value);
    }

    public static void SetHeaderFadingOpacity(Control obj, double value)
    {
        obj.SetValue(HeaderFadingOpacityProperty, value);
    }

    public static double GetHeaderFadingOpacity(Control obj)
    {
        return obj.GetValue(HeaderFadingOpacityProperty);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (this.GetTemplateChildren().OfType<ScrollViewer>().FirstOrDefault(x => x.Name == MainScrollViewerName) is
            { } scrollViewer)
        {
            _mainScrollViewer = scrollViewer;
            _mainScrollViewer.ScrollChanged += MainScrollViewerOnScrollChanged;
        }

        if (this.GetTemplateChildren().OfType<ContentPresenter>().FirstOrDefault(x => x.Name == HeaderName) is
            { } header)
        {
            _header = header;
            _header.SizeChanged += HeaderOnSizeChanged;
        }

        base.OnApplyTemplate(e);
    }

    private void HeaderOnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateContent();
    }

    private void MainScrollViewerOnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        UpdateContent();
    }

    private void UpdateContent()
    {
        if (_mainScrollViewer == null || _header == null) return;

        var collapsed = _header.Bounds.Height - _mainScrollViewer.Offset.Y < CollapsedHeight;
        RealHeaderHeight = collapsed
            ? _header.Bounds.Height - CollapsedHeight
            : _mainScrollViewer.Offset.Y;
        SpaceAreaHeight = _header.Bounds.Height - CollapsedHeight;
        SetHeaderFadingOpacity(this,
            collapsed ? 0 : 1 - Math.Pow(_mainScrollViewer.Offset.Y / (_header.Bounds.Height - CollapsedHeight), 3));
        PseudoClasses.Set(":header-collapsed", collapsed);
    }
}