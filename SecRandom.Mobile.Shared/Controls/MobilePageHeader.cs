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
    }

    public string Title
    {
        get => _title.Text ?? string.Empty;
        set => _title.Text = value;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
