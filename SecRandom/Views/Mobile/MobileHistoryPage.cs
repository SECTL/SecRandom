using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Mobile;

namespace SecRandom.Views.Mobile;

public sealed partial class MobileHistoryPage : UserControl
{
    private readonly IFeatureAvailabilityService _featureAvailability;

    public MobileHistoryPage(IMobileCapabilities capabilities)
    {
        _featureAvailability = IAppHost.GetService<IFeatureAvailabilityService>();
        InitializeComponent();
        RefreshLotteryVisibility();
        _featureAvailability.Changed += FeatureAvailabilityOnChanged;
        DetachedFromVisualTree += (_, _) => _featureAvailability.Changed -= FeatureAvailabilityOnChanged;
    }

    private void FeatureAvailabilityOnChanged(object? sender, EventArgs e) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(RefreshLotteryVisibility);

    private void RefreshLotteryVisibility()
    {
        var tabs = this.FindControl<TabStrip>("HistoryTabs")!;
        this.FindControl<TabStripItem>("LotteryTab")!.IsVisible = _featureAvailability.IsLotteryEnabled;
        if (!_featureAvailability.IsLotteryEnabled && tabs.SelectedIndex == 1)
            tabs.SelectedIndex = 0;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
