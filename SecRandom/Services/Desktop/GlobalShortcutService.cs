using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Services;
using SecRandom.ViewModels.MainPages;
using SecRandom.Views;

namespace SecRandom.Services.Desktop;

public sealed class GlobalShortcutService : IHostedService
{
    private const uint WmHotkey = 0x0312;
    private const uint WmReload = 0x8001;
    private const uint WmQuit = 0x0012;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private readonly MainConfigHandler _configHandler;
    private MoreSettingsConfig _settings;
    private readonly RollCallPageViewModel _rollCall;
    private readonly LotteryPageViewModel _lottery;
    private readonly IFeatureAvailabilityService _featureAvailability;
    private readonly ILogger<GlobalShortcutService> _logger;
    private readonly ManualResetEventSlim _threadReady = new();
    private readonly object _settingsGate = new();
    private readonly Dictionary<int, ShortcutAction> _registered = [];
    private ShortcutBinding[] _pendingBindings = [];
    private Thread? _thread;
    private uint _threadId;
    private bool _started;

    public GlobalShortcutService(
        MainConfigHandler configHandler,
        RollCallPageViewModel rollCall,
        LotteryPageViewModel lottery,
        IFeatureAvailabilityService featureAvailability,
        ILogger<GlobalShortcutService> logger)
    {
        _configHandler = configHandler;
        _settings = configHandler.Data.MoreSettings;
        _rollCall = rollCall;
        _lottery = lottery;
        _featureAvailability = featureAvailability;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogInformation("全局快捷键仅在 Windows 上可用。");
            return Task.CompletedTask;
        }

        lock (_settingsGate)
        {
            if (_started)
                return Task.CompletedTask;

            _started = true;
            _pendingBindings = CreateBindings();
            _settings.PropertyChanged += SettingsOnPropertyChanged;
            _featureAvailability.Changed += FeatureAvailabilityOnChanged;
            _thread = new Thread(RunMessageLoop)
            {
                IsBackground = true,
                Name = "SecRandom.GlobalShortcut"
            };
            _thread.Start();
        }

        if (!_threadReady.Wait(TimeSpan.FromSeconds(2), cancellationToken))
            _logger.LogWarning("全局快捷键注册线程启动超时。");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Thread? thread;
        uint threadId;
        lock (_settingsGate)
        {
            if (!_started)
                return Task.CompletedTask;

            _started = false;
            _settings.PropertyChanged -= SettingsOnPropertyChanged;
            _featureAvailability.Changed -= FeatureAvailabilityOnChanged;
            thread = _thread;
            threadId = _threadId;
            _thread = null;
            _threadId = 0;
        }

        if (threadId != 0)
            PostThreadMessage(threadId, WmQuit, UIntPtr.Zero, IntPtr.Zero);
        thread?.Join(TimeSpan.FromSeconds(2));
        return Task.CompletedTask;
    }

    public void Refresh()
    {
        lock (_settingsGate)
        {
            if (ReferenceEquals(_settings, _configHandler.Data.MoreSettings))
                return;

            _settings.PropertyChanged -= SettingsOnPropertyChanged;
            _settings = _configHandler.Data.MoreSettings;
            if (!_started)
                return;

            _settings.PropertyChanged += SettingsOnPropertyChanged;
            _pendingBindings = CreateBindings();
            if (_threadId != 0)
                PostThreadMessage(_threadId, WmReload, UIntPtr.Zero, IntPtr.Zero);
        }
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        lock (_settingsGate)
        {
            if (!_started)
                return;

            _pendingBindings = CreateBindings();
            if (_threadId != 0)
                PostThreadMessage(_threadId, WmReload, UIntPtr.Zero, IntPtr.Zero);
        }
    }

    private void FeatureAvailabilityOnChanged(object? sender, EventArgs e)
    {
        SettingsOnPropertyChanged(sender, new PropertyChangedEventArgs(nameof(MoreSettingsConfig.LotteryEnabled)));
    }

    private void RunMessageLoop()
    {
        _threadId = GetCurrentThreadId();
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        _threadReady.Set();
        ReloadBindings();

        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            if (message.message == WmReload)
                ReloadBindings();
            else if (message.message == WmHotkey
                     && _registered.TryGetValue((int)message.wParam.ToUInt64(), out var action))
                Dispatcher.UIThread.Post(() => Execute(action), DispatcherPriority.Normal);
        }

        UnregisterAll();
    }

    private void ReloadBindings()
    {
        UnregisterAll();

        ShortcutBinding[] bindings;
        lock (_settingsGate)
            bindings = _pendingBindings;

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in bindings)
        {
            if (string.IsNullOrWhiteSpace(binding.Shortcut))
                continue;

            if (!TryParseShortcut(binding.Shortcut, out var modifiers, out var virtualKey))
            {
                _logger.LogWarning("忽略无效全局快捷键：{ShortcutAction}。", binding.Action);
                continue;
            }

            if (!used.Add(binding.Shortcut))
            {
                _logger.LogWarning("忽略重复全局快捷键：{ShortcutAction}。", binding.Action);
                continue;
            }

            var id = 0x5000 + (int)binding.Action;
            if (!RegisterHotKey(IntPtr.Zero, id, modifiers | ModNoRepeat, virtualKey))
            {
                _logger.LogWarning("注册全局快捷键失败：{ShortcutAction}（错误码 {ErrorCode}）。", binding.Action,
                    Marshal.GetLastWin32Error());
                continue;
            }

            _registered[id] = binding.Action;
        }
    }

    private void UnregisterAll()
    {
        foreach (var id in _registered.Keys)
            UnregisterHotKey(IntPtr.Zero, id);
        _registered.Clear();
    }

    private void Execute(ShortcutAction action)
    {
        if (!_featureAvailability.IsLotteryEnabled && action is ShortcutAction.OpenLotteryPage
            or ShortcutAction.IncreaseLotteryCount or ShortcutAction.DecreaseLotteryCount or ShortcutAction.StartLottery)
            return;

        switch (action)
        {
            case ShortcutAction.OpenRollCallPage:
                App.ToggleMainWindow("main.rollCall");
                break;
            case ShortcutAction.QuickDraw:
                App.ShowQuickDrawWindow();
                break;
            case ShortcutAction.OpenLotteryPage:
                App.ToggleMainWindow("main.lottery");
                break;
            case ShortcutAction.IncreaseRollCallCount:
                _rollCall.IncreaseCountFromShortcut();
                break;
            case ShortcutAction.DecreaseRollCallCount:
                _rollCall.DecreaseCountFromShortcut();
                break;
            case ShortcutAction.IncreaseLotteryCount:
                _lottery.IncreaseCountFromShortcut();
                break;
            case ShortcutAction.DecreaseLotteryCount:
                _lottery.DecreaseCountFromShortcut();
                break;
            case ShortcutAction.StartRollCall:
                ObserveActionAsync(_rollCall.ToggleDrawFromShortcutAsync(), "点名快捷键执行失败。");
                break;
            case ShortcutAction.StartLottery:
                ObserveActionAsync(_lottery.ToggleDrawFromShortcutAsync(), "抽奖快捷键执行失败。");
                break;
        }
    }

    private async void ObserveActionAsync(Task task, string failureMessage)
    {
        try
        {
            await task.ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, failureMessage);
        }
    }

    private ShortcutBinding[] CreateBindings()
    {
        if (!_settings.EnableShortcut)
            return [];

        var bindings = new List<ShortcutBinding>
        {
            new(ShortcutAction.OpenRollCallPage, _settings.OpenRollCallPageShortcut),
            new(ShortcutAction.QuickDraw, _settings.QuickDrawShortcut),
            new(ShortcutAction.IncreaseRollCallCount, _settings.IncreaseRollCallCountShortcut),
            new(ShortcutAction.DecreaseRollCallCount, _settings.DecreaseRollCallCountShortcut),
            new(ShortcutAction.StartRollCall, _settings.StartRollCallShortcut)
        };
        if (_featureAvailability.IsLotteryEnabled)
        {
            bindings.Add(new(ShortcutAction.OpenLotteryPage, _settings.OpenLotteryPageShortcut));
            bindings.Add(new(ShortcutAction.IncreaseLotteryCount, _settings.IncreaseLotteryCountShortcut));
            bindings.Add(new(ShortcutAction.DecreaseLotteryCount, _settings.DecreaseLotteryCountShortcut));
            bindings.Add(new(ShortcutAction.StartLottery, _settings.StartLotteryShortcut));
        }
        return bindings.ToArray();
    }

    private static bool TryParseShortcut(string shortcut, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        var parts = shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        foreach (var part in parts[..^1])
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL": modifiers |= ModControl; break;
                case "ALT": modifiers |= ModAlt; break;
                case "SHIFT": modifiers |= ModShift; break;
                case "WIN": modifiers |= ModWin; break;
                default: return false;
            }
        }

        return TryMapVirtualKey(parts[^1], out virtualKey);
    }

    private static bool TryMapVirtualKey(string key, out uint virtualKey)
    {
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]))
        {
            virtualKey = char.ToUpperInvariant(key[0]);
            return true;
        }

        if (key.Length is >= 2 and <= 3 && key[0] is 'F' or 'f'
            && int.TryParse(key[1..], out var functionKey) && functionKey is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + functionKey - 1);
            return true;
        }

        virtualKey = key.ToUpperInvariant() switch
        {
            "SPACE" => 0x20,
            "TAB" => 0x09,
            "ENTER" => 0x0D,
            "DELETE" => 0x2E,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
            "LEFT" => 0x25,
            "UP" => 0x26,
            "RIGHT" => 0x27,
            "DOWN" => 0x28,
            _ => 0
        };
        return virtualKey != 0;
    }

    private readonly record struct ShortcutBinding(ShortcutAction Action, string Shortcut);

    private enum ShortcutAction
    {
        OpenRollCallPage,
        QuickDraw,
        OpenLotteryPage,
        IncreaseRollCallCount,
        DecreaseRollCallCount,
        IncreaseLotteryCount,
        DecreaseLotteryCount,
        StartRollCall,
        StartLottery
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public Point point;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int x;
        public int y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint threadId, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Message message, IntPtr hWnd, uint minFilter, uint maxFilter);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out Message message, IntPtr hWnd, uint minFilter, uint maxFilter, uint remove);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
