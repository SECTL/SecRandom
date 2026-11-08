# 探针：验证 SetWindowPos(hwnd, HWND_TOP, ...) 是否会清除 WS_EX_TOPMOST
# 结论用于决定 WindowsWindowFeatureService.TrySetExtendedStyles 是否必须带 SWP_NOZORDER。
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class TopmostProbe
{
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOPMOST = 0x00000008L;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;

    public static readonly IntPtr HWND_TOP = IntPtr.Zero;
    public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName,
        uint style, int x, int y, int cx, int cy, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    public static bool IsTopmost(IntPtr hWnd)
    {
        long style = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
        return (style & WS_EX_TOPMOST) != 0;
    }
}
'@

$hwnd = [TopmostProbe]::CreateWindowExW(0, "STATIC", "topmost-probe", 0x80000000L,
    -2000, -2000, 100, 100, [IntPtr]::Zero, [IntPtr]::Zero, [IntPtr]::Zero, [IntPtr]::Zero)
if ($hwnd -eq [IntPtr]::Zero) {
    Write-Output "CREATE_FAILED error=$([Runtime.InteropServices.Marshal]::GetLastWin32Error())"
    exit 1
}

$flags = [TopmostProbe]::SWP_NOMOVE -bor [TopmostProbe]::SWP_NOSIZE -bor [TopmostProbe]::SWP_NOACTIVATE -bor [TopmostProbe]::SWP_FRAMECHANGED

try {
    [void][TopmostProbe]::SetWindowPos($hwnd, [TopmostProbe]::HWND_TOPMOST, 0, 0, 0, 0, $flags)
    Write-Output "after HWND_TOPMOST          -> topmost=$([TopmostProbe]::IsTopmost($hwnd))"

    [void][TopmostProbe]::SetWindowPos($hwnd, [TopmostProbe]::HWND_TOP, 0, 0, 0, 0, $flags)
    Write-Output "after HWND_TOP (no NOZORDER)-> topmost=$([TopmostProbe]::IsTopmost($hwnd))"

    [void][TopmostProbe]::SetWindowPos($hwnd, [TopmostProbe]::HWND_TOPMOST, 0, 0, 0, 0, $flags)
    [void][TopmostProbe]::SetWindowPos($hwnd, [TopmostProbe]::HWND_TOP, 0, 0, 0, 0, ([TopmostProbe]::SWP_NOMOVE -bor [TopmostProbe]::SWP_NOSIZE -bor [TopmostProbe]::SWP_NOACTIVATE -bor [TopmostProbe]::SWP_FRAMECHANGED -bor [TopmostProbe]::SWP_NOZORDER))
    Write-Output "after HWND_TOP (SWP_NOZORDER)-> topmost=$([TopmostProbe]::IsTopmost($hwnd))"

    [void][TopmostProbe]::SetWindowPos($hwnd, [TopmostProbe]::HWND_TOPMOST, 0, 0, 0, 0, $flags)
    [void][TopmostProbe]::SetWindowPos($hwnd, [TopmostProbe]::HWND_NOTOPMOST, 0, 0, 0, 0, $flags)
    Write-Output "after HWND_NOTOPMOST        -> topmost=$([TopmostProbe]::IsTopmost($hwnd))"
}
finally {
    [void][TopmostProbe]::DestroyWindow($hwnd)
}