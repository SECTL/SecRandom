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
    
    public List<FontFamily> FontFamilies { get; } = [
        ..FontManager.Current.SystemFonts
            .Where(fontFamily => FontManager.Current.TryGetGlyphTypeface(new Typeface(fontFamily), out _)),
        GlobalConstants.DefaultAvaFontFamily];
    
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
}