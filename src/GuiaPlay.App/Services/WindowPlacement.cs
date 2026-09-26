using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using GuiaPlay.App.Models;

namespace GuiaPlay.App.Services;

internal static partial class WindowPlacement
{
    private static readonly nint TopMost = new(-1);
    private const int ExtendedStyleIndex = -20;
    private const long ExtendedStyleToolWindow = 0x00000080L;
    private const long ExtendedStyleAppWindow = 0x00040000L;
    private const long ExtendedStyleNoActivate = 0x08000000L;
    private const uint NoActivate = 0x0010;
    private const uint NoZOrder = 0x0004;
    private const uint ShowWindow = 0x0040;

    public static void FillMonitor(Window window, MonitorInfo monitor)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero)
        {
            return;
        }

        _ = SetWindowPos(
            handle,
            TopMost,
            monitor.Left,
            monitor.Top,
            monitor.Width,
            monitor.Height,
            NoActivate | ShowWindow);
    }

    public static void PlaceIdentifier(Window window, MonitorInfo monitor, double sizeDips = 112, double marginDips = 24)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero)
        {
            return;
        }

        MakeNonActivatingToolWindow(handle);
        var scale = Math.Max(0.5, monitor.DpiX / 96d);
        var size = Math.Min(
            (int)Math.Round(sizeDips * scale),
            Math.Min(monitor.WorkWidth, monitor.WorkHeight));
        var margin = (int)Math.Round(marginDips * scale);
        var x = monitor.WorkLeft + margin;
        var y = monitor.WorkTop + monitor.WorkHeight - margin - size;

        _ = SetWindowPos(handle, TopMost, x, y, size, size, NoActivate | ShowWindow);
    }

    public static void PlaceOperatorPanel(Window window, MonitorInfo monitor, double marginDips = 24)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero)
        {
            return;
        }

        var scale = Math.Max(0.5, monitor.DpiX / 96d);
        var margin = Math.Max(0, (int)Math.Round(marginDips * scale));
        var availableWidth = Math.Max(1, monitor.WorkWidth - margin * 2);
        var availableHeight = Math.Max(1, monitor.WorkHeight - margin * 2);
        var requestedWidth = (int)Math.Round(Math.Max(window.MinWidth, window.ActualWidth) * scale);
        var requestedHeight = (int)Math.Round(Math.Max(window.MinHeight, window.ActualHeight) * scale);
        var width = Math.Min(requestedWidth, availableWidth);
        var height = Math.Min(requestedHeight, availableHeight);
        var x = monitor.WorkLeft + Math.Max(margin, (monitor.WorkWidth - width) / 2);
        var y = monitor.WorkTop + Math.Max(margin, (monitor.WorkHeight - height) / 2);

        _ = SetWindowPos(handle, nint.Zero, x, y, width, height, NoActivate | NoZOrder | ShowWindow);
    }

    private static void MakeNonActivatingToolWindow(nint handle)
    {
        var style = GetWindowLongPtr(handle, ExtendedStyleIndex).ToInt64();
        style |= ExtendedStyleToolWindow | ExtendedStyleNoActivate;
        style &= ~ExtendedStyleAppWindow;
        _ = SetWindowLongPtr(handle, ExtendedStyleIndex, new nint(style));
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static partial nint GetWindowLongPtr(nint window, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static partial nint SetWindowLongPtr(nint window, int index, nint newValue);
}
