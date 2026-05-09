using FluentAvalonia.UI.Controls;

namespace SecRandom.Core.Controls;

public class FluentIcon : FontIcon
{
    public FluentIcon()
    {
        FontFamily = GlobalConstants.FluentIconsFontFamily;
    }

    public FluentIcon(string glyph) : this()
    {
        Glyph = glyph;
    }

    public FluentIcon(string glyph, double size) : this(glyph)
    {
        Width = Height = size;
    }

    public object ProvideValue()
    {
        return this;
    }
}