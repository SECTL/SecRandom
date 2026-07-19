using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace SecRandom.Mobile.Controls;

public sealed class MobilePageHeader : UserControl
{
    private readonly Border _root;
    private readonly TextBlock _title;

    public MobilePageHeader()
    {
        _title = new TextBlock
        {
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        var headerContent = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Children =
            {
                new Image
                {
                    Source = new Bitmap(AssetLoader.Open(new Uri("avares://SecRandom.Mobile/Assets/AppLogo.png"))),
                    Width = 32,
                    Height = 32,
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                },
                _title
            }
        };
        Grid.SetColumn(_title, 1);

        _root = new Border
        {
            Padding = new Thickness(20, 14),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = headerContent
        };
        Content = _root;
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
}
