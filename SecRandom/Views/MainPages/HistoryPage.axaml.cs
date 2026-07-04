using Avalonia.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Icons;

namespace SecRandom.Views.MainPages;

[PageInfo("main.history", FluentIcons.HistoryRegular, location: PageLocation.Bottom, hidePageTitle: true)]
public partial class HistoryPage : UserControl
{
    public HistoryPage()
    {
        InitializeComponent();
    }
}
