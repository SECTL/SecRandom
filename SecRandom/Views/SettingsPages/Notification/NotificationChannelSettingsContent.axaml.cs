using Avalonia.Controls;

using Avalonia.Interactivity;

namespace SecRandom.Views.SettingsPages.Notification;

public partial class NotificationChannelSettingsContent : UserControl
{
    public NotificationChannelSettingsContent()
    {
        InitializeComponent();
    }

    private void TestNotification_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NotificationChannelSettingsPageBase page)
            page.HandleTestNotificationAsync(sender, e);
    }
}
