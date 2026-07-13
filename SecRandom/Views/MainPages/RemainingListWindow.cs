using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Primitives;

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
        Content = new ScrollViewer
        {
            Margin = new Thickness(16),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new ItemsControl
            {
                ItemsSource = items,
                ItemTemplate = itemTemplate,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                })
            }
        };
    }
}
