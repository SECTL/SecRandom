# 探针：列出指定进程的所有顶层窗口，报告扩展样式 / 可见性 / 位置 / 类名
param([int]$ProcessId = 0)

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class WindowProbe
{
    public const int GWL_EXSTYLE = -20;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(IntPtr hWnd, StringBuilder name, int size);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder name, int size);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    public class Info
    {
        public IntPtr Handle;
        public bool Visible;
        public long ExStyle;
        public string ClassName;
        public string Title;
        public RECT Rect;
    }

    public static List<Info> GetWindows(uint pid)
    {
        List<Info> result = new List<Info>();
        EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
        {
            uint windowPid;
            GetWindowThreadProcessId(hWnd, out windowPid);
            if (windowPid != pid) return true;
            Info info = new Info();
            info.Handle = hWnd;
            info.Visible = IsWindowVisible(hWnd);
            info.ExStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
            StringBuilder buffer = new StringBuilder(256);
            GetClassNameW(hWnd, buffer, buffer.Capacity);
            info.ClassName = buffer.ToString();
            buffer.Clear();
            GetWindowTextW(hWnd, buffer, buffer.Capacity);
            info.Title = buffer.ToString();
            RECT rect;
            GetWindowRect(hWnd, out rect);
            info.Rect = rect;
            result.Add(info);
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
'@

$WS_EX_TOPMOST = 0x00000008L
$WS_EX_TOOLWINDOW = 0x00000080L
$WS_EX_NOACTIVATE = 0x08000000L
$WS_EX_LAYERED = 0x00080000L

$windows = [WindowProbe]::GetWindows([uint32]$ProcessId)
Write-Output "pid=$ProcessId top-level-windows=$($windows.Count)"
foreach ($w in $windows) {
    $topmost = if (($w.ExStyle -band $WS_EX_TOPMOST) -ne 0) { "TOPMOST" } else { "------ " }
    $tool = if (($w.ExStyle -band $WS_EX_TOOLWINDOW) -ne 0) { "tool" } else { "app " }
    $noact = if (($w.ExStyle -band $WS_EX_NOACTIVATE) -ne 0) { "noact" } else { "     " }
    $layered = if (($w.ExStyle -band $WS_EX_LAYERED) -ne 0) { "layered" } else { "       " }
    $size = "$($w.Rect.Right - $w.Rect.Left)x$($w.Rect.Bottom - $w.Rect.Top)"
    Write-Output ("hwnd={0,-8} {1} {2} {3} {4} vis={5,-5} pos=({6},{7}) size={8,-11} class={9,-24} title='{10}'" -f `
        ("0x{0:X}" -f $w.Handle.ToInt64()), $topmost, $tool, $noact, $layered, $w.Visible, `
        $w.Rect.Left, $w.Rect.Top, $size, $w.ClassName, $w.Title)
}