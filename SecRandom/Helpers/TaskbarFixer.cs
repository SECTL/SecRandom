/*
 * UIAccess 任务栏图标修复器
 * 代码来自 [LegacyIsland / UIAccessX](https://github.com/segf4ultk1nger/dubiisland)
 * Licensed under the GNU General Public License v3.0
 * Copyright (c) Dubi906w
 */

using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace SecRandom.Helpers;

/// <summary>
/// 修复 UIAccess 下「本该不在任务栏」的窗口（顶层效果窗口、托盘菜单、进过编辑模式的主窗口等）出现在任务栏的问题。
/// <para>
/// 只靠 owner / <c>WS_EX_TOOLWINDOW</c> 改完，shell 往往不会重算任务栏按钮，必须再 <c>ITaskbarList::DeleteTab</c> 推一把；
/// 只靠 DeleteTab 又得先猜哪些窗口该摘（旧实现按 <c>WS_EX_TOOLWINDOW</c> 猜，会漏掉带 <c>WS_EX_APPWINDOW</c> 的效果窗口）。
/// 所以这里融合两者：以 Avalonia 的 <c>ShowInTaskbar == false</c> 精确挑窗口，再补上 Avalonia 本应做的隐藏 owner，
/// 最后用 DeleteTab 强制 shell 刷新。
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static class TaskbarFixer
{
    private const int GWLP_HWNDPARENT = -8;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_APPWINDOW = 0x00040000;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private static bool _started;
    private static IntPtr _offscreenOwner;
    private static ITaskbarList? _taskbar;
    private static DispatcherTimer? _timer;

    public static void Start()
    {
        if (_started || !OperatingSystem.IsWindows())
        {
            return;
        }

        _started = true;

        Control.LoadedEvent.AddClassHandler<Window>((window, _) =>
        {
            Fix(window);
            // 托盘菜单等窗口会被复用，每次重新 Show 后再修一遍。
            window.Opened += (_, _) => Fix(window);
        });
        Window.ShowInTaskbarProperty.Changed.AddClassHandler<Window>((window, _) => Fix(window));

        // 托盘菜单是短命窗口，Loaded 即修；主窗口进过编辑模式后可能被 shell 重新加回来，定期兜底。
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => FixAll();
        _timer.Start();

        FixAll();
    }

    private static void FixAll()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        foreach (var window in desktop.Windows)
        {
            Fix(window);
        }
    }

    private static void Fix(Window window)
    {
        try
        {
            var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            if (window.ShowInTaskbar)
            {
                return;
            }

            var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            var changed = false;

            if ((exStyle & WS_EX_TOOLWINDOW) == 0)
            {
                exStyle |= WS_EX_TOOLWINDOW;
                changed = true;
            }

            if ((exStyle & WS_EX_APPWINDOW) != 0)
            {
                exStyle &= ~WS_EX_APPWINDOW;
                changed = true;
            }

            if (changed)
            {
                SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));
            }

            var owner = GetOffscreenOwner();
            if (owner != IntPtr.Zero && GetWindowLongPtr(hwnd, GWLP_HWNDPARENT) == IntPtr.Zero)
            {
                SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, owner);
                changed = true;
            }

            if (changed)
            {
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            }

            // owner / 扩展样式改完 shell 未必重算，DeleteTab 推一把（对已摘掉的窗口无副作用）。
            try
            {
                GetTaskbar()?.DeleteTab(hwnd);
            }
            catch
            {
                // 忽略。
            }
        }
        catch
        {
            // 忽略单个窗口失败。
        }
    }

    /// <summary>取 Avalonia 的隐藏窗口句柄（复用它自己的窗口，避免再造一个）。</summary>
    private static IntPtr GetOffscreenOwner()
    {
        if (_offscreenOwner != IntPtr.Zero)
        {
            return _offscreenOwner;
        }

        try
        {
            var type = Type.GetType("Avalonia.Win32.OffscreenParentWindow, Avalonia.Win32");
            var value = type?.GetProperty("Handle", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (value is IntPtr handle)
            {
                _offscreenOwner = handle;
            }
        }
        catch
        {
            // 忽略，下次再试。
        }

        return _offscreenOwner;
    }

    private static ITaskbarList? GetTaskbar()
    {
        if (_taskbar != null)
        {
            return _taskbar;
        }

        var taskbar = (ITaskbarList)new CTaskbarList();
        taskbar.HrInit();
        _taskbar = taskbar;
        return _taskbar;
    }

    [ComImport]
    [Guid("56FDF342-FD6D-11D0-958A-006097C9A090")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList
    {
        void HrInit();

        void AddTab(IntPtr hwnd);

        void DeleteTab(IntPtr hwnd);

        void ActivateTab(IntPtr hwnd);

        void SetActiveAlt(IntPtr hwnd);
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11D0-958A-006097C9A090")]
    [ClassInterface(ClassInterfaceType.None)]
    private class CTaskbarList
    {
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy,
        uint uFlags);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
            : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
}