using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.ViewModels.MainPages;
using SecRandom.Views;

namespace SecRandom.Services;

public sealed class ShortcutService(
    MainConfigHandler configHandler,
    RollCallPageViewModel rollCallViewModel,
    LotteryPageViewModel lotteryViewModel,
    IFeatureAvailabilityService featureAvailability,
    ILogger<ShortcutService> logger) : IHostedService, IDisposable
{
    private const uint WmHotKey = 0x0312;
    private const uint WmReload = 0x8001;
    private const uint WmStop = 0x8002;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private MoreSettingsConfig _settings = configHandler.Data.MoreSettings;
    private readonly Dictionary<int, ShortcutAction> _registeredActions = [];
    private Thread? _hotkeyThread;
    private uint _hotkeyThreadId;
    private bool _disposed;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _settings.PropertyChanged += SettingsOnPropertyChanged;
        featureAvailability.Changed += FeatureAvailabilityOnChanged;
        if (!OperatingSystem.IsWindows())
            return Task.CompletedTask;

        _hotkeyThread = new Thread(HotkeyThreadMain)
        {
            IsBackground = true,
            Name = "SecRandom shortcut listener"
        };
        _hotkeyThread.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _settings.PropertyChanged -= SettingsOnPropertyChanged;
        featureAvailability.Changed -= FeatureAvailabilityOnChanged;
        if (_hotkeyThreadId != 0)
            PostThreadMessage(_hotkeyThreadId, WmStop, UIntPtr.Zero, IntPtr.Zero);
        return Task.CompletedTask;
    }

    public void Refresh()
    {
        if (ReferenceEquals(_settings, configHandler.Data.MoreSettings))
            return;

        _settings.PropertyChanged -= SettingsOnPropertyChanged;
        _settings = configHandler.Data.MoreSettings;
        _settings.PropertyChanged += SettingsOnPropertyChanged;
        SettingsOnPropertyChanged(this, new PropertyChangedEventArgs(nameof(MoreSettingsConfig.EnableShortcut)));
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_hotkeyThreadId != 0)
            PostThreadMessage(_hotkeyThreadId, WmReload, UIntPtr.Zero, IntPtr.Zero);
    }

    private void FeatureAvailabilityOnChanged(object? sender, EventArgs e)
    {
        SettingsOnPropertyChanged(sender, new PropertyChangedEventArgs(nameof(MoreSettingsConfig.LotteryEnabled)));
    }

    private void HotkeyThreadMain()
    {
        _hotkeyThreadId = GetCurrentThreadId();
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        ReloadHotkeys();

        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            if (message.message == WmStop)
                break;
            if (message.message == WmReload)
            {
                ReloadHotkeys();
                continue;
            }

            if (message.message == WmHotKey && _registeredActions.TryGetValue((int)message.wParam, out var action))
                Dispatcher.UIThread.Post(() => Execute(action), DispatcherPriority.Normal);
        }

        ClearHotkeys();
        _hotkeyThreadId = 0;
    }

    private void ReloadHotkeys()
    {
        ClearHotkeys();
        if (!_settings.EnableShortcut)
            return;

        var bindings = new List<(ShortcutAction Action, string Gesture)>
        {
            (ShortcutAction.OpenRollCallPage, _settings.OpenRollCallPageShortcut),
            (ShortcutAction.QuickDraw, _settings.QuickDrawShortcut),
            (ShortcutAction.IncreaseRollCallCount, _settings.IncreaseRollCallCountShortcut),
            (ShortcutAction.DecreaseRollCallCount, _settings.DecreaseRollCallCountShortcut),
            (ShortcutAction.StartRollCall, _settings.StartRollCallShortcut)
        };
        if (featureAvailability.IsLotteryEnabled)
        {
            bindings.Add((ShortcutAction.OpenLotteryPage, _settings.OpenLotteryPageShortcut));
            bindings.Add((ShortcutAction.IncreaseLotteryCount, _settings.IncreaseLotteryCountShortcut));
            bindings.Add((ShortcutAction.DecreaseLotteryCount, _settings.DecreaseLotteryCountShortcut));
            bindings.Add((ShortcutAction.StartLottery, _settings.StartLotteryShortcut));
        }

        var id = 1;
        foreach (var binding in bindings)
        {
            if (!TryParse(binding.Gesture, out var modifiers, out var key))
                continue;

            if (RegisterHotKey(IntPtr.Zero, id, modifiers | ModNoRepeat, key))
            {
                _registeredActions[id] = binding.Action;
                id++;
            }
            else
            {
                logger.LogWarning("快捷键注册失败：{Shortcut}。", binding.Gesture);
            }
        }
    }

    private void ClearHotkeys()
    {
        foreach (var id in _registeredActions.Keys)
            UnregisterHotKey(IntPtr.Zero, id);
        _registeredActions.Clear();
    }

    private void Execute(ShortcutAction action)
    {
        if (!featureAvailability.IsLotteryEnabled && action is ShortcutAction.OpenLotteryPage
            or ShortcutAction.IncreaseLotteryCount or ShortcutAction.DecreaseLotteryCount or ShortcutAction.StartLottery)
            return;

        switch (action)
        {
            case ShortcutAction.OpenRollCallPage:
                App.ShowMainWindow();
                MainView.Current?.SelectNavigationItemById("main.rollCall");
                break;
            case ShortcutAction.QuickDraw:
                App.ShowQuickDrawWindow();
                break;
            case ShortcutAction.OpenLotteryPage:
                App.ShowMainWindow();
                MainView.Current?.SelectNavigationItemById("main.lottery");
                break;
            case ShortcutAction.IncreaseRollCallCount:
                if (rollCallViewModel.IncreaseCountCommand.CanExecute(null))
                    rollCallViewModel.IncreaseCountCommand.Execute(null);
                break;
            case ShortcutAction.DecreaseRollCallCount:
                if (rollCallViewModel.DecreaseCountCommand.CanExecute(null))
                    rollCallViewModel.DecreaseCountCommand.Execute(null);
                break;
            case ShortcutAction.IncreaseLotteryCount:
                if (lotteryViewModel.IncreaseCountCommand.CanExecute(null))
                    lotteryViewModel.IncreaseCountCommand.Execute(null);
                break;
            case ShortcutAction.DecreaseLotteryCount:
                if (lotteryViewModel.DecreaseCountCommand.CanExecute(null))
                    lotteryViewModel.DecreaseCountCommand.Execute(null);
                break;
            case ShortcutAction.StartRollCall:
                if (rollCallViewModel.StartDrawCommand.CanExecute(null))
                    rollCallViewModel.StartDrawCommand.Execute(null);
                break;
            case ShortcutAction.StartLottery:
                if (lotteryViewModel.StartDrawCommand.CanExecute(null))
                    lotteryViewModel.StartDrawCommand.Execute(null);
                break;
        }
    }

    private static bool TryParse(string value, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        foreach (var part in parts[..^1])
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) modifiers |= ModControl;
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) modifiers |= ModAlt;
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) modifiers |= ModShift;
            else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase)) modifiers |= ModWin;
            else return false;
        }

        return TryGetVirtualKey(parts[^1], out key);
    }

    private static bool TryGetVirtualKey(string value, out uint key)
    {
        key = 0;
        if (value.Length == 1 && char.IsLetterOrDigit(value[0]))
        {
            key = char.ToUpperInvariant(value[0]);
            return true;
        }

        if (value.Length is 2 or 3 && value[0] == 'F' && int.TryParse(value[1..], out var functionKey)
            && functionKey is >= 1 and <= 24)
        {
            key = (uint)(0x70 + functionKey - 1);
            return true;
        }

        key = value.ToUpperInvariant() switch
        {
            "SPACE" => 0x20,
            "TAB" => 0x09,
            "ENTER" => 0x0D,
            "ESCAPE" => 0x1B,
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
        return key != 0;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _settings.PropertyChanged -= SettingsOnPropertyChanged;
    }

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
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern sbyte GetMessage(out Message message, IntPtr hWnd, uint minFilter, uint maxFilter);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out Message message, IntPtr hWnd, uint minFilter, uint maxFilter, uint removeMessage);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint threadId, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
