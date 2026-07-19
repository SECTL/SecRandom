using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Controls;
using SecRandom.Core.Icons;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;
using AvaloniaButton = Avalonia.Controls.Button;

namespace SecRandom.Mobile.Controls;

public sealed class MobileSettingsPageFrame : UserControl
{
    public MobileSettingsPageFrame(string title, string description, Action goBack, IEnumerable<Control> items)
    {
        var stack = new StackPanel
        {
            Spacing = 8,
            MaxWidth = 720,
            Children =
            {
                CreateBackButton(goBack),
                new FAInfoBar
                {
                    Title = title,
                    Message = description,
                    Severity = FAInfoBarSeverity.Informational,
                    IsOpen = true,
                    IsClosable = false,
                    IconSource = new FluentIconSource(FluentIcons.InfoFilled)
                }
            }
        };

        foreach (var item in items)
            stack.Children.Add(item);

        Content = new ScrollViewer
        {
            Content = stack,
            Padding = new Thickness(20, 20, 20, 28),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    private static AvaloniaButton CreateBackButton(Action goBack)
    {
        var button = new AvaloniaButton
        {
            Content = new IconText { Glyph = FluentIcons.ArrowLeftFilled, Text = LR.C_Back, Spacing = 6 },
            HorizontalAlignment = HorizontalAlignment.Left,
            MinHeight = 44
        };
        button.Click += (_, _) => goBack();
        return button;
    }
}
