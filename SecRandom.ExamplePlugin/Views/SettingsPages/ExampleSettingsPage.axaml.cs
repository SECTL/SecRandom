using Avalonia.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;

namespace SecRandom.ExamplePlugin.Views.SettingsPages;

[PageInfo("example.settings.example", FluentIcons.PlugConnectedFilled)]
public partial class ExampleSettingsPage : UserControl
{
    public ExampleSettingsPage()
    {
        InitializeComponent();
    }
}