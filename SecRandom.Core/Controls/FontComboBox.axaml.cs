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
    
    public List<FontFamily> FontFamilies { get; } =
        BuildFontFamilies(FontManager.Current.SystemFonts, CanValidateFontFamily);
    
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
        IEnumerable<FontFamily> fontFamilies,
        Func<FontFamily, bool> canValidateFontFamily)
    {
        var result = new List<FontFamily>();
        foreach (var fontFamily in fontFamilies)
        {
            try
            {
                if (canValidateFontFamily(fontFamily))
                    result.Add(fontFamily);
            }
            catch (FormatException)
            {
            }
        }

        result.Add(GlobalConstants.DefaultAvaFontFamily);
        return result;
    }

    private static bool CanValidateFontFamily(FontFamily fontFamily)
    {
        _ = fontFamily.Name;
        return true;
    }
}
