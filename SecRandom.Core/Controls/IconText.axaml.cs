using Avalonia;
using Avalonia.Controls;

namespace SecRandom.Core.Controls;

public partial class IconText : UserControl
{
    public static readonly StyledProperty<string?> GlyphProperty = AvaloniaProperty.Register<IconText, string?>(
        nameof(Glyph));

    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<IconText, string>(
        nameof(Text));

    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<IconText, double>(
        nameof(Spacing), 4);

    public IconText()
    {
        InitializeComponent();
    }

    public string? Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }
}