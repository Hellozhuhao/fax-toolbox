using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace FaxToolbox.Windows;

// The SDL window keeps scrcpy's native video rendering and input handling.
// Only the child window belonging to the owned scrcpy process is reparented.
internal sealed class AndroidMirrorHost : HwndHost
{
    private nint container;
    private nint video;
    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        container = CreateWindowEx(0, "static", "", 0x50000000 | 0x02000000,
            0, 0, 1, 1, parent.Handle, 0, 0, 0);
        if (container == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return new HandleRef(this, container);
    }
    public void Attach(nint window)
    {
        if (container == 0 || window == 0) throw new InvalidOperationException("投屏窗口尚未准备好。");
        video = window;
        var style = GetWindowLong(video, -16);
        SetWindowLong(video, -16, (style & ~unchecked((int)0x80C40000)) | 0x40000000);
        Marshal.SetLastPInvokeError(0);
        if (SetParent(video, container) == 0 && Marshal.GetLastWin32Error() != 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        SetWindowPos(video, 0, 0, 0, 1, 1, 0x0020 | 0x0040 | 0x0004);
        ResizeVideo();
    }
    public void Detach() { video = 0; }
    protected override void OnWindowPositionChanged(System.Windows.Rect bounds)
    {
        base.OnWindowPositionChanged(bounds);
        ResizeVideo();
    }
    private void ResizeVideo()
    {
        if (video != 0 && GetClientRect(container, out var rect))
            MoveWindow(video, 0, 0, Math.Max(1, rect.Right), Math.Max(1, rect.Bottom), true);
    }
    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        video = 0;
        DestroyWindow(hwnd.Handle);
        container = 0;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowEx(int ex, string cls, string title, int style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetParent(nint hwnd, nint parent);
    [DllImport("user32.dll")] private static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(nint hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool MoveWindow(nint hwnd, int x, int y, int w, int h, bool repaint);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint insert, int x, int y, int w, int h, uint flags);
}
