using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Controls;
using SecRandom.Core.Icons;
using SR = SecRandom.Langs.SettingsPages.Security.Resources;

namespace SecRandom.Services.Security;

internal static class SecurityVerificationDialog
{
    public static async Task<SecurityVerificationResponse> ShowAsync(TopLevel xamlRoot, SecurityVerificationRequest request)
    {
        var password = new TextBox
        {
            PasswordChar = '●',
            PlaceholderText = SR.C_PasswordPlaceholder
        };
        var totp = new TextBox { PlaceholderText = SR.C_TotpPlaceholder, MaxLength = 6 };
        var usb = new TextBlock
        {
            Text = SR.M_UsbMissing,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var usbRequired = request.RequiredFactors.Contains(SecurityFactor.Usb);
        var usbPresent = false;
        var usbProbeRunning = false;
        var usbDialogClosed = false;
        var verifyAvailability = new VerificationAvailabilityCommand(() =>
            SecurityVerificationEligibility.CanSubmit(request, password.Text, totp.Text, usbPresent));

        password.TextChanged += (_, _) => verifyAvailability.RaiseCanExecuteChanged();
        totp.TextChanged += (_, _) => verifyAvailability.RaiseCanExecuteChanged();

        async Task RefreshUsbStatusAsync()
        {
            if (usbProbeRunning)
                return;

            usbProbeRunning = true;
            try
            {
                var bindings = await Task.Run(() =>
                    IAppHost.GetService<ISecurityService>()
                        .GetUsbBindingsAsync()
                        .GetAwaiter()
                        .GetResult());
                if (!usbDialogClosed)
                {
                    usbPresent = bindings.Any(binding => binding.IsPresent);
                    usb.Text = usbPresent
                        ? SR.M_UsbPresent
                        : SR.M_UsbMissing;
                    verifyAvailability.RaiseCanExecuteChanged();
                }
            }
            catch (Exception)
            {
                if (!usbDialogClosed)
                {
                    usbPresent = false;
                    usb.Text = SR.M_UsbMissing;
                    verifyAvailability.RaiseCanExecuteChanged();
                }
            }
            finally
            {
                usbProbeRunning = false;
            }
        }

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = request.LockoutRemaining is { } remaining
                ? string.Format(SR.M_VerificationLockedFormat, Math.Ceiling(remaining.TotalSeconds))
                : request.RequireAllSelectedFactors ? SR.M_VerificationAllFactors : SR.M_VerificationAnyFactor,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        if (request.RequiredFactors.Contains(SecurityFactor.Password))
        {
            panel.Children.Add(new TextBlock { Text = SR.S_Password });
            panel.Children.Add(password);
        }

        if (request.RequiredFactors.Contains(SecurityFactor.Totp))
        {
            panel.Children.Add(new TextBlock { Text = SR.S_Totp });
            panel.Children.Add(totp);
        }

        if (usbRequired)
        {
            var usbStatusPanel = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 8
            };
            var refreshUsbButton = new Button
            {
                Content = new FluentIcon(FluentIcons.ArrowClockwiseFilled),
                Padding = new Thickness(4),
                MinWidth = 32,
                MinHeight = 32
            };
            ToolTip.SetTip(refreshUsbButton, SR.C_Refresh);
            refreshUsbButton.Click += async (_, _) => await RefreshUsbStatusAsync();
            Grid.SetColumn(refreshUsbButton, 1);
            usbStatusPanel.Children.Add(usb);
            usbStatusPanel.Children.Add(refreshUsbButton);
            panel.Children.Add(usbStatusPanel);
        }

        var dialog = new FATaskDialog
        {
            XamlRoot = xamlRoot,
            Title = SR.M_VerificationDialogTitle,
            Header = SR.M_VerificationDialogTitle,
            Content = panel
        };
        dialog.Buttons.Add(new FATaskDialogButton(SR.C_Cancel, "cancel"));
        if (request.AllowPreview)
            dialog.Buttons.Add(new FATaskDialogButton(SR.C_Preview, "preview"));
        dialog.Buttons.Add(new FATaskDialogButton(SR.C_Verify, "verify")
        {
            IsDefault = true,
            Command = verifyAvailability
        });

        if (usbRequired)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += async (_, _) => await RefreshUsbStatusAsync();
            dialog.Closing += (_, _) =>
            {
                usbDialogClosed = true;
                timer.Stop();
            };
            _ = RefreshUsbStatusAsync();
            timer.Start();
        }

        return await dialog.ShowAsync() switch
        {
            "preview" => new SecurityVerificationResponse(string.Empty, string.Empty, false, PreviewRequested: true),
            "verify" => new SecurityVerificationResponse(password.Text ?? string.Empty, totp.Text ?? string.Empty,
                UsbPresent: usbPresent),
            _ => new SecurityVerificationResponse(string.Empty, string.Empty, false, Cancelled: true)
        };
    }

    private sealed class VerificationAvailabilityCommand(Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute();

        public void Execute(object? parameter)
        {
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
