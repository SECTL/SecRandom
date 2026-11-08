# 截取 SecRandom 主窗口/设置窗口画面，用于核对 IPC 导航是否真的加载了页面内容。
param(
    [string]$OutDir = '',
    [string]$Tag = 'shot'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class WinShot
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public sealed class WindowInfo
    {
        public IntPtr Handle;
        public string Title = string.Empty;
        public RECT Rect;
    }

    public static List<WindowInfo> ListForProcess(int pid)
    {
        var result = new List<WindowInfo>();
        EnumWindows((hWnd, lParam) =>
        {
            uint pid2;
            GetWindowThreadProcessId(hWnd, out pid2);
            if (pid2 != (uint)pid) return true;
            if (!IsWindowVisible(hWnd)) return true;
            var length = GetWindowTextLength(hWnd);
            var builder = new StringBuilder(length + 2);
            GetWindowText(hWnd, builder, builder.Capacity);
            RECT rect;
            GetWindowRect(hWnd, out rect);
            if (rect.Right - rect.Left < 80 || rect.Bottom - rect.Top < 40) return true;
            result.Add(new WindowInfo { Handle = hWnd, Title = builder.ToString(), Rect = rect });
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
'@

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($OutDir)) { $OutDir = Join-Path $repoRoot 'artifacts\perf\shots' }
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$process = Get-Process -Name 'SecRandom.Desktop' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $process) { throw 'SecRandom.Desktop is not running.' }

$windows = [WinShot]::ListForProcess($process.Id)
if ($windows.Count -eq 0) { throw 'No visible window found for SecRandom.Desktop.' }

$index = 0
foreach ($window in $windows) {
    $index++
    [WinShot]::ShowWindow($window.Handle, 9) | Out-Null      # SW_RESTORE
    [WinShot]::SetForegroundWindow($window.Handle) | Out-Null
    Start-Sleep -Milliseconds 700

    $rect = $window.Rect
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($width, $height)))
    $graphics.Dispose()

    $safeTitle = ($window.Title -replace '[^\w\u4e00-\u9fff.-]', '_')
    if ([string]::IsNullOrWhiteSpace($safeTitle)) { $safeTitle = "window$index" }
    $path = Join-Path $OutDir ("{0}-{1}-{2}.png" -f $Tag, $index, $safeTitle)
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    "saved: $path  ($width x $height)  title='$($window.Title)'"
}
