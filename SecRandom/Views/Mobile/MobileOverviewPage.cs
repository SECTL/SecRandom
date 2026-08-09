using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SecRandom.Views.Mobile;

public sealed partial class MobileOverviewPage : UserControl
{
    public MobileOverviewPage()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
