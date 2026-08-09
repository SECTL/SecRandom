using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using SecRandom.Mobile;

namespace SecRandom.Views.Mobile;

public sealed partial class MobileHistoryPage : UserControl
{
    private readonly IMobileCapabilities _capabilities;

    public MobileHistoryPage(IMobileCapabilities capabilities)
    {
        _capabilities = capabilities;
        InitializeComponent();
        this.FindControl<TabStripItem>("LotteryTab")!.IsVisible = _capabilities.IsLotteryEnabled;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
