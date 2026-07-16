using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClassIsland.Shared.IPC;
using dotnetCampus.Ipc.CompilerServices.GeneratedProxies;
using dotnetCampus.Ipc.Pipes;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Enums;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Shared.Models.Profile;
using SecRandom4Ci.Interface.Enums;
using SecRandom4Ci.Interface.Models;
using SecRandom4Ci.Interface.Services;
using ProfileStudent = SecRandom.Shared.Models.Profile.Student;

namespace SecRandom.Services.Notification;

public sealed class NotificationService : IDisposable
{
    private static readonly Version MinimumPluginVersion = new(1, 2, 0, 0);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan InvocationTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan JsonRouteReadyDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly MainConfigHandler _configHandler;
    private readonly ILogger<NotificationService> _logger;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private IpcClient? _classIslandClient;
    private ISecRandomService? _classIslandService;
    private DateTimeOffset _nextClassIslandConnectAttempt = DateTimeOffset.MinValue;
    private bool _quickDrawBuiltInPreviewActive;
    private bool _isDisposed;

    public NotificationService(MainConfigHandler configHandler, ILogger<NotificationService> logger)
    {
        _configHandler = configHandler;
        _logger = logger;
    }

    public void QueueStudents(
        NotificationSettingsType type,
        string className,
        IReadOnlyCollection<ProfileStudent> students)
    {
        Queue(
            type,
            className,
            students.Select(CreateStudentItem).ToList(),
             students.Select(student => DisplayValue(student.Name, student.Id)).ToList());
    }

    public Task BeginClassIslandAnimationAsync(
        NotificationSettingsType type,
        string className,
        IReadOnlyCollection<ProfileStudent> candidates,
        int drawCount)
    {
        if (_isDisposed)
            return Task.CompletedTask;

        var config = _configHandler.Data;
        var basicSettings = config.GetOverrideNotificationSettings(type, OverridableNotificationSettingsType.Basic);
        var serviceSettings = config.GetOverrideNotificationSettings(type, OverridableNotificationSettingsType.Service);
        if (!basicSettings.Enabled || !basicSettings.Animation || !serviceSettings.UsesExternalNotificationService)
            return Task.CompletedTask;

        return SendToClassIslandAsync(
            new NotificationData
            {
                ClassName = className,
                Items = candidates.Select(CreateStudentItem).ToList(),
                DrawCount = Math.Max(drawCount, 1),
                DisplayDuration = Math.Clamp(serviceSettings.DisplayDuration, 1, 60),
                Animation = true,
                ResultType = GetPartialResultType(type)
            },
            builtInFallback: null);
    }

    public Task BeginClassIslandLotteryAnimationAsync(
        string className,
        IReadOnlyCollection<Prize> candidates,
        int drawCount)
    {
        if (_isDisposed)
            return Task.CompletedTask;

        var config = _configHandler.Data;
        var basicSettings = config.GetOverrideNotificationSettings(
            NotificationSettingsType.Lottery,
            OverridableNotificationSettingsType.Basic);
        var serviceSettings = config.GetOverrideNotificationSettings(
            NotificationSettingsType.Lottery,
            OverridableNotificationSettingsType.Service);
        if (!basicSettings.Enabled || !basicSettings.Animation || !serviceSettings.UsesExternalNotificationService)
            return Task.CompletedTask;

        return SendToClassIslandAsync(
            new NotificationData
            {
                ClassName = className,
                Items = candidates.Select(prize => new NotificationItem
                {
                    IsLottery = true,
                    LotteryName = DisplayValue(prize.Name, prize.Id),
                    Exists = prize.Exists
                }).ToList(),
                DrawCount = Math.Max(drawCount, 1),
                DisplayDuration = Math.Clamp(serviceSettings.DisplayDuration, 1, 60),
                Animation = true,
                ResultType = ResultType.PartialLottery
            },
            builtInFallback: null);
    }

    public void QueueLottery(
        string className,
        IReadOnlyList<Prize> prizes,
        IReadOnlyList<ProfileStudent> assignedStudents)
    {
        List<NotificationItem> items = [];
        List<string> displayItems = [];
        for (var index = 0; index < prizes.Count; index++)
        {
            var prize = prizes[index];
            var student = index < assignedStudents.Count ? assignedStudents[index] : null;
            var prizeName = DisplayValue(prize.Name, prize.Id);
            items.Add(new NotificationItem
            {
                IsLottery = true,
                LotteryName = prizeName,
                StudentId = student is null ? 0 : ParseStudentId(student.Id),
                StudentName = student is null ? string.Empty : DisplayValue(student.Name, student.Id),
                Exists = prize.Exists && (student?.Exists ?? true)
            });
            displayItems.Add(prizeName);
        }

        Queue(NotificationSettingsType.Lottery, className, items, displayItems);
    }

    public async Task<bool> BeginBuiltInNotificationPresentationAsync(NotificationSettingsType type)
    {
        if (_isDisposed || type != NotificationSettingsType.QuickDraw)
            return false;

        if (!UsesBuiltInNotificationService(type))
            return false;

        var config = _configHandler.Data;
        var serviceSettings = config.GetOverrideNotificationSettings(
            type,
            OverridableNotificationSettingsType.Service);

        var windowSettings = config.GetOverrideNotificationSettings(
            type,
            OverridableNotificationSettingsType.NotificationWindow);
        _quickDrawBuiltInPreviewActive = true;
        await App.ShowQuickDrawNotificationPreviewWindowAsync(windowSettings);
        return true;
    }

    public bool UsesBuiltInNotificationService(NotificationSettingsType type)
    {
        if (_isDisposed)
            return false;

        var config = _configHandler.Data;
        var basicSettings = config.GetOverrideNotificationSettings(type, OverridableNotificationSettingsType.Basic);
        return basicSettings.Enabled
            && config.GetOverrideNotificationSettings(type, OverridableNotificationSettingsType.Service)
                .UsesBuiltInNotificationService;
    }

    public void CancelBuiltInNotificationPresentation(NotificationSettingsType type)
    {
        if (type == NotificationSettingsType.QuickDraw)
        {
            _quickDrawBuiltInPreviewActive = false;
            App.HideQuickDrawNotificationWindow();
        }
    }

    private void Queue(
        NotificationSettingsType type,
        string className,
        IReadOnlyCollection<NotificationItem> items,
        IReadOnlyCollection<string> displayItems)
    {
        if (_isDisposed || items.Count == 0)
            return;

        var config = _configHandler.Data;
        var basicSettings = config.GetOverrideNotificationSettings(
            type,
            OverridableNotificationSettingsType.Basic);
        if (!basicSettings.Enabled)
            return;

        var serviceSettings = config.GetOverrideNotificationSettings(
            type,
            OverridableNotificationSettingsType.Service);
        var windowSettings = config.GetOverrideNotificationSettings(
            type,
            OverridableNotificationSettingsType.NotificationWindow);
        var useMainWindow = serviceSettings.UseMainWindowWhenExceedThreshold
                            && items.Count > Math.Clamp(serviceSettings.MainWindowDisplayThreshold, 1, 100);
        if (useMainWindow)
        {
            if (type == NotificationSettingsType.QuickDraw)
                _quickDrawBuiltInPreviewActive = false;
            Dispatcher.UIThread.Post(App.ShowMainWindow);
            return;
        }

        if (serviceSettings.UsesBuiltInNotificationService)
        {
            var showPreview = type != NotificationSettingsType.QuickDraw || !_quickDrawBuiltInPreviewActive;
            _quickDrawBuiltInPreviewActive = false;
            ShowBuiltIn(type, displayItems, basicSettings, serviceSettings, windowSettings, preserveQuickDrawResult: true, showPreview);
        }

        if (serviceSettings.UsesExternalNotificationService)
        {
            var notification = new NotificationData
            {
                ClassName = className,
                Items = items.ToList(),
                DrawCount = items.Count,
                DisplayDuration = Math.Clamp(serviceSettings.DisplayDuration, 1, 60),
                Animation = basicSettings.Animation,
                ResultType = GetResultType(type)
            };
            Action? builtInFallback = serviceSettings.UsesExternalNotificationService
                && !serviceSettings.UsesBuiltInNotificationService
                && serviceSettings.UseBuiltInOnServiceFailure
                ? () => ShowBuiltIn(type, displayItems, basicSettings, serviceSettings, windowSettings, preserveQuickDrawResult: false, showPreview: true)
                : null;
            _ = Task.Run(() => SendToClassIslandAsync(notification, builtInFallback));
        }
    }

    private void ShowBuiltIn(
        NotificationSettingsType type,
        IReadOnlyCollection<string> items,
        NotificationChannelSettings basicSettings,
        NotificationChannelSettings serviceSettings,
        NotificationChannelSettings windowSettings,
        bool preserveQuickDrawResult,
        bool showPreview)
    {
        if (!_isDisposed)
            App.ShowQuickDrawNotificationWindow(
                items,
                Math.Clamp(serviceSettings.DisplayDuration, 1, 60),
                basicSettings.Animation,
                type == NotificationSettingsType.QuickDraw && preserveQuickDrawResult,
                windowSettings,
                showPreview);
    }

    private async Task SendToClassIslandAsync(NotificationData notification, Action? builtInFallback)
    {
        await _sendGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_isDisposed)
                return;

            var service = await GetClassIslandServiceAsync().ConfigureAwait(false);
            if (service is null)
            {
                builtInFallback?.Invoke();
                return;
            }

            if (!string.Equals(
                    await InvokeClassIslandAsync(service.IsAlive).ConfigureAwait(false),
                    "Yes",
                    StringComparison.Ordinal))
            {
                _logger.LogDebug("SecRandom4Ci 通知服务未响应。");
                InvalidateClassIslandConnection();
                ScheduleClassIslandRetry();
                builtInFallback?.Invoke();
                return;
            }

            await InvokeClassIslandAsync(() => service.ShowNotification(notification)).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "通过 SecRandom4Ci 插件发送 ClassIsland 通知失败。");
            InvalidateClassIslandConnection();
            ScheduleClassIslandRetry();
            builtInFallback?.Invoke();
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task<ISecRandomService?> GetClassIslandServiceAsync()
    {
        if (_isDisposed)
            return null;
        if (_classIslandService is not null)
            return _classIslandService;
        if (DateTimeOffset.UtcNow < _nextClassIslandConnectAttempt)
            return null;

        IpcClient? client = null;
        try
        {
            client = new IpcClient();
            await client.Connect().WaitAsync(ConnectTimeout).ConfigureAwait(false);
            // ClassIsland initializes the JSON routed peer after the named pipe connects.
            await Task.Delay(JsonRouteReadyDelay).ConfigureAwait(false);
            if (client.PeerProxy is null)
            {
                DisposeClient(client);
                ScheduleClassIslandRetry();
                return null;
            }

            var service = GeneratedIpcFactory.CreateIpcProxy<ISecRandomService>(client.Provider, client.PeerProxy);
            var pluginVersion = await InvokeClassIslandAsync(service.GetPluginVersion).ConfigureAwait(false);
            if (pluginVersion is null || pluginVersion < MinimumPluginVersion ||
                !string.Equals(
                    await InvokeClassIslandAsync(service.IsAlive).ConfigureAwait(false),
                    "Yes",
                    StringComparison.Ordinal))
            {
                _logger.LogDebug("SecRandom4Ci 插件不可用或版本低于 {MinimumPluginVersion}。", MinimumPluginVersion);
                DisposeClient(client);
                ScheduleClassIslandRetry();
                return null;
            }

            _classIslandClient = client;
            _classIslandService = service;
            _nextClassIslandConnectAttempt = DateTimeOffset.MinValue;
            _logger.LogInformation("已连接到 ClassIsland IPC，SecRandom4Ci {PluginVersion} 通知服务可用。", pluginVersion);
            return _classIslandService;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "连接 ClassIsland IPC 的 SecRandom4Ci 通知服务失败。");
            DisposeClient(client);
            InvalidateClassIslandConnection();
            ScheduleClassIslandRetry();
            return null;
        }
    }

    private static NotificationItem CreateStudentItem(ProfileStudent student)
    {
        return new NotificationItem
        {
            StudentId = ParseStudentId(student.Id),
            StudentName = DisplayValue(student.Name, student.Id),
            Exists = student.Exists
        };
    }

    private void InvalidateClassIslandConnection()
    {
        _classIslandService = null;
        DisposeClient(_classIslandClient);
        _classIslandClient = null;
    }

    private static void DisposeClient(IpcClient? client)
    {
        if (client is null)
            return;

        try
        {
            client.Provider.Dispose();
        }
        catch (Exception)
        {
        }
    }

    private static Task<T> InvokeClassIslandAsync<T>(Func<T> invoke)
    {
        return Task.Run(invoke).WaitAsync(InvocationTimeout);
    }

    private static Task InvokeClassIslandAsync(Action invoke)
    {
        return Task.Run(invoke).WaitAsync(InvocationTimeout);
    }

    private void ScheduleClassIslandRetry()
    {
        _nextClassIslandConnectAttempt = DateTimeOffset.UtcNow.Add(RetryDelay);
    }

    private static ResultType GetResultType(NotificationSettingsType type)
    {
        return type switch
        {
            NotificationSettingsType.RollCall => ResultType.FinishedRollCall,
            NotificationSettingsType.QuickDraw => ResultType.FinishedQuickDraw,
            NotificationSettingsType.Lottery => ResultType.FinishedLottery,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }

    private static ResultType GetPartialResultType(NotificationSettingsType type)
    {
        return type switch
        {
            NotificationSettingsType.RollCall => ResultType.PartialRollCall,
            NotificationSettingsType.QuickDraw => ResultType.PartialQuickDraw,
            NotificationSettingsType.Lottery => ResultType.PartialLottery,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }

    private static int ParseStudentId(string value)
    {
        return int.TryParse(value, out var studentId) ? studentId : 0;
    }

    private static string DisplayValue(string primary, string fallback)
    {
        return string.IsNullOrWhiteSpace(primary) ? fallback : primary;
    }

    public void Dispose()
    {
        _isDisposed = true;
        InvalidateClassIslandConnection();
    }
}
