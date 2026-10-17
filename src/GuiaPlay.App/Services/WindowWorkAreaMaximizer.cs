using System.Runtime.InteropServices;

namespace GuiaPlay.App.Services;

/// <summary>
/// Limits the custom-chrome WPF window to the work area of its CURRENT monitor,
/// not the full display bounds (which would cover the taskbar when maximized).
/// All coordinates here are native physical pixels, as required by WM_GETMINMAXINFO.
/// </summary>
internal static class WindowWorkAreaMaximizer
{
    internal const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    internal static bool Apply(nint hwnd, nint nativeMinMaxInfo)
    {
        if (hwnd == 0 || nativeMinMaxInfo == 0)
        {
            return false;
        }

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == 0)
        {
            return false;
        }

        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        var work = monitorInfo.Work;
        var bounds = monitorInfo.Monitor;
        if (work.Right <= work.Left || work.Bottom <= work.Top)
        {
            return false;
        }

        var windowInfo = Marshal.PtrToStructure<MinMaxInfo>(nativeMinMaxInfo);
        windowInfo.MaxPosition.X = work.Left - bounds.Left;
        windowInfo.MaxPosition.Y = work.Top - bounds.Top;
        windowInfo.MaxSize.X = work.Right - work.Left;
        windowInfo.MaxSize.Y = work.Bottom - work.Top;
        Marshal.StructureToPtr(windowInfo, nativeMinMaxInfo, false);
        return true;
    }
}
