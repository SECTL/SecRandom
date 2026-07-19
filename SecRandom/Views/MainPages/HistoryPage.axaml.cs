using Avalonia.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Icons;
using SecRandom.Core.Views;

namespace SecRandom.Views.MainPages;

[PageInfo("main.history", FluentIcons.HistoryFilled, location: PageLocation.Bottom, hidePageTitle: true)]
public partial class HistoryPage : ViewBase
{
    public HistoryPage()
    {
        InitializeComponent();
    }
}
