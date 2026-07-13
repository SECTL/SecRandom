using System.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace SecRandom.Views.MainPages;

internal sealed class RemainingListWindow : Window
{
    public RemainingListWindow(string title, IEnumerable items, IDataTemplate? itemTemplate)
    {
        Title = title;
        Width = 1000;
        Height = 600;
        MinWidth = 600;
        MinHeight = 400;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new ListBox
        {
            ItemsSource = items,
            ItemTemplate = itemTemplate,
            Margin = new Avalonia.Thickness(16)
        };
    }
}
