using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SecRandom.Mobile.Controls;

public sealed partial class MobilePageHeader : UserControl
{
    private readonly Border _root;
    private readonly TextBlock _title;

    public MobilePageHeader()
    {
        InitializeComponent();
        _root = this.FindControl<Border>("Root")!;
        _title = this.FindControl<TextBlock>("TitleText")!;
        RefreshTheme();
    }

    public string Title
    {
        get => _title.Text ?? string.Empty;
        set => _title.Text = value;
    }

    public void RefreshTheme()
    {
        _root.Background = Views.MobileTheme.Surface;
        _root.BorderBrush = Views.MobileTheme.Border;
        _title.Foreground = Views.MobileTheme.Text;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
