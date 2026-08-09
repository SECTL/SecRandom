using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SecRandom.Views;

internal sealed class SecRandomTmpRootWindow : Window
{
    public SecRandomTmpRootWindow()
    {
        Width = 1;
        Height = 1;
        MinWidth = 1;
        MinHeight = 1;
        MaxWidth = 1;
        MaxHeight = 1;
        SizeToContent = SizeToContent.Manual;
        CanResize = false;
        ShowInTaskbar = false;
        WindowDecorations = WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
    }
}
