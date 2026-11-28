using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using FluentAvalonia.UI.Windowing;
using SecRandom.Views.SettingsPages.General;

namespace SecRandom.Views;

public partial class FeatureConfigurationWindow : FAAppWindow
{
    private bool _allowClose;
    private bool _confirmingClose;

    public FeatureConfigurationWindow()
    {
        InitializeComponent();
        Title = FeatureConfigurationText.Get("Title");
        TitleText.Text = Title;
        TitleBar.Height = 48;
        TitleBar.ExtendsContentIntoTitleBar = true;
        ConfigurationPage = new FeatureConfigurationSettingsPage();
        ContentHost.Content = ConfigurationPage;
    }

    public FeatureConfigurationSettingsPage ConfigurationPage { get; }

    private void Window_OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
            return;

        e.Cancel = true;
        if (_confirmingClose)
            return;
        _confirmingClose = true;
        _ = ConfirmCloseAsync();
    }

    private async Task ConfirmCloseAsync()
    {
        try
        {
            if (await ConfigurationPage.ConfirmCloseAsync())
            {
                _allowClose = true;
                Close();
            }
        }
        finally
        {
            _confirmingClose = false;
        }
    }
}
