using Avalonia;
using Avalonia.Controls.Primitives;
using FluentAvalonia.UI.Controls;

namespace SecRandom.Core.Controls;

/// <summary>
///     空白内容占位符
/// </summary>
public class Empty : TemplatedControl
{
    public static readonly StyledProperty<FAIconSource> IconProperty = AvaloniaProperty.Register<Empty, FAIconSource>(
        nameof(Icon), new FluentIconSource("\ue262")
        {
            FontSize = 64
        });

    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<Empty, string>(
        nameof(Text), Langs.Common.Resources.M_Nothing);

    public static readonly StyledProperty<double> IconHeightProperty = AvaloniaProperty.Register<Empty, double>(
        nameof(IconHeight), 64.0);

    public static readonly StyledProperty<double> IconWidthProperty = AvaloniaProperty.Register<Empty, double>(
        nameof(IconWidth), 64.0);

    public FAIconSource Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double IconHeight
    {
        get => GetValue(IconHeightProperty);
        set => SetValue(IconHeightProperty, value);
    }

    public double IconWidth
    {
        get => GetValue(IconWidthProperty);
        set => SetValue(IconWidthProperty, value);
    }
}