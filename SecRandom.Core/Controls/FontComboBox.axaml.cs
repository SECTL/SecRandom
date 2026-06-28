using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;

namespace SecRandom.Core.Controls;

public partial class FontComboBox : UserControl
{
    public static readonly StyledProperty<string> ValueProperty = AvaloniaProperty.Register<FontComboBox, string>(
        nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    public string Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }
    
    public List<FontFamily> FontFamilies { get; } = BuildFontFamilies(
        FontManager.Current.SystemFonts,
        fontFamily => FontManager.Current.TryGetGlyphTypeface(new Typeface(fontFamily), out _));
    
    public FontComboBox()
    {
        InitializeComponent();
        
        FontSelector.SelectionChanged += OnSelectionChanged;
        this.GetObservable(ValueProperty).Subscribe(OnValueChanged);
    }
    
    private void OnValueChanged(string? value)
    {
        if (value == null) return;
        var matching = FontFamilies.FirstOrDefault(f => f.ToString() == value || f.Name == value);
        if (matching != null && !Equals(FontSelector.SelectedItem, matching))
        {
            FontSelector.SelectedItem = matching;
        }
    }
    
    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (FontSelector.SelectedItem is FontFamily ff)
        {
            var newValue = ff.ToString().Replace(@"compositefont:", "");
            if (Value != newValue)
            {
                Value = newValue;
            }
        }
    }

    private static List<FontFamily> BuildFontFamilies(
        IEnumerable<FontFamily> systemFonts,
        Func<FontFamily, bool> canUseFontFamily)
    {
        var fontFamilies = new List<FontFamily>();
        foreach (var fontFamily in systemFonts)
        {
            if (CanUseFontFamily(fontFamily, canUseFontFamily))
            {
                fontFamilies.Add(fontFamily);
            }
        }

        fontFamilies.Add(GlobalConstants.DefaultAvaFontFamily);
        return fontFamilies;
    }

    private static bool CanUseFontFamily(FontFamily fontFamily, Func<FontFamily, bool> canUseFontFamily)
    {
        try
        {
            return canUseFontFamily(fontFamily);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
