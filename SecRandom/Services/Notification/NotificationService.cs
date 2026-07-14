using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Enums;
using SecRandom.Core.Models;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Views;

namespace SecRandom.Services.Notification;

public sealed class NotificationService
{
    private readonly MainConfigHandler _configHandler;
    private readonly ILogger<NotificationService> _logger;
    private NotificationWindow? _window;

    public NotificationService(MainConfigHandler configHandler, ILogger<NotificationService> logger)
    {
        _configHandler = configHandler;
        _logger = logger;
    }

    public async Task ShowAsync(NotificationSettingsType type, string title, IReadOnlyCollection<string> items)
    {
        if (items.Count == 0)
            return;

        var config = _configHandler.Data;
        var basic = config.GetOverrideNotificationSettings(type, OverridableNotificationSettingsType.Basic);
        if (!basic.Enabled)
            return;

        var service = config.GetOverrideNotificationSettings(type, OverridableNotificationSettingsType.Service);
        if (service.UseMainWindowWhenExceedThreshold
            && items.Count > Math.Clamp(service.MainWindowDisplayThreshold, 1, 100))
        {
            Dispatcher.UIThread.Post(App.ShowMainWindow);
            return;
        }
        var useBuiltIn = service.NotificationServiceType is 0 or 2;
        var useClassIsland = service.NotificationServiceType is 1 or 2;
        var classIslandDelivered = useClassIsland && await SendToClassIslandAsync(type, title, items, service);

        if (useBuiltIn || (useClassIsland && !classIslandDelivered))
        {
            var window = config.GetOverrideNotificationSettings(
                type,
                OverridableNotificationSettingsType.NotificationWindow);
            ShowBuiltIn(title, items, basic, window);
        }
    }

    private void ShowBuiltIn(
        string title,
        IReadOnlyCollection<string> items,
        NotificationChannelSettings basic,
        NotificationChannelSettings windowSettings)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _window ??= new NotificationWindow();
            _window.Show(title, items, basic.Animation, basic.AutoCloseTime, windowSettings);
        });
    }

    private async Task<bool> SendToClassIslandAsync(
        NotificationSettingsType type,
        string title,
        IReadOnlyCollection<string> items,
        NotificationChannelSettings settings)
    {
        if (!TryGetClassIslandPort(out var port))
            return false;

        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "notification",
            subtype = type switch
            {
                NotificationSettingsType.RollCall => "roll_call",
                NotificationSettingsType.QuickDraw => "quick_draw",
                NotificationSettingsType.Lottery => "lottery",
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
            },
            class_name = title,
            selected_students = items.Select(item => new
            {
                student_id = 0,
                student_name = item,
                exists = true
            }),
            draw_count = items.Count,
            display_duration = Math.Clamp(settings.DisplayDuration, 1, 60)
        });

        try
        {
            using var client = new TcpClient();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await client.ConnectAsync(IPAddress.Loopback, port, cancellation.Token);
            await client.GetStream().WriteAsync(payload, cancellation.Token);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "ClassIsland notification delivery failed.");
            return false;
        }
    }

    private static bool TryGetClassIslandPort(out int port)
    {
        port = 0;
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config",
            "ClassIsland",
            "ipc_config.json");
        if (!File.Exists(path))
            return false;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("port", out var value)
                || !value.TryGetInt32(out port))
                return false;

            return port is > 0 and <= 65535;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }
}
