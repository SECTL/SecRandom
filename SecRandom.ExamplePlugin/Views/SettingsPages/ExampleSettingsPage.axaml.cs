using Avalonia.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;

namespace SecRandom.ExamplePlugin.Views.SettingsPages;

[PageInfo("plugin.secrandom.example.settings", FluentIcons.PlugConnectedFilled)]
public partial class ExampleSettingsPage : UserControl
{
    public ExampleSettingsPage()
    {
        InitializeComponent();
    }
}