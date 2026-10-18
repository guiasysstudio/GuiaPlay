using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using GuiaPlay.App.Models;
using GuiaPlay.App.Services;

namespace GuiaPlay.App.Windows;

/// <summary>
/// Experimental in-app live window replication using the Windows compositor.
/// The source is the one visible YouTubeOutputWindow; the replicas own NO
/// WebView2, audio, decoder, network stream or independent playback clock.
/// This does not alter Windows monitor topology and is NOT a production
/// compliance clearance for redisplaying third-party content.
/// </summary>
internal sealed class YouTubeMirrorOutputWindow : Window
{
    private const uint DwmTnpRectDestination = 0x00000001;
    private const uint DwmTnpOpacity = 0x00000004;
    private const uint DwmTnpVisible = 0x00000008;
    private const uint DwmTnpSourceClientAreaOnly = 0x00000010;
    private readonly Window _source;
    private nint _thumbnail;

    public YouTubeMirrorOutputWindow(MonitorInfo monitor, Window source)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(source);
        Monitor = monitor;
        _source = source;
        Title = "GuiaPlay — réplica da saída YouTube";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Black;

        SourceInitialized += (_, _) =>
        {
            WindowPlacement.FillMonitor(this, Monitor);
            RegisterLiveThumbnail();
        };
        SizeChanged += (_, _) => UpdateLiveThumbnail();
        Closed += (_, _) =>
        {
            if (_thumbnail != nint.Zero)
            {
                _ = DwmUnregisterThumbnail(_thumbnail);
                _thumbnail = nint.Zero;
            }
        };
    }

    public MonitorInfo Monitor { get; private set; }

    public void Reposition(MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        Monitor = monitor;
        WindowPlacement.FillMonitor(this, monitor);
        UpdateLiveThumbnail();
    }

    private void RegisterLiveThumbnail()
    {
        var destination = new WindowInteropHelper(this).Handle;
        var source = new WindowInteropHelper(_source).Handle;
        if (source == nint.Zero || destination == nint.Zero)
        {
            throw new InvalidOperationException("Janelas de origem e destino devem estar abertas.");
        }

        Marshal.ThrowExceptionForHR(DwmRegisterThumbnail(destination, source, out _thumbnail));
        FileLogger.Info("GP-YT06: réplica DWM registrada " + Monitor.ShortLabel);
        UpdateLiveThumbnail();
    }

    private void UpdateLiveThumbnail()
    {
        if (_thumbnail == nint.Zero) return;
        var destination = new WindowInteropHelper(this).Handle;
        if (!GetClientRect(destination, out var client))
            throw new InvalidOperationException("Não foi possível medir a saída DWM.");

        Marshal.ThrowExceptionForHR(DwmQueryThumbnailSourceSize(_thumbnail, out var size));
        var width = client.Right - client.Left;
        var height = client.Bottom - client.Top;
        if (width <= 0 || height <= 0 || size.Width <= 0 || size.Height <= 0)
            return;

        // Preserve the source aspect ratio and center it on the destination.
        var scale = Math.Min(width / (double)size.Width, height / (double)size.Height);
        var targetWidth = Math.Max(1, (int)Math.Round(size.Width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(size.Height * scale));
        var left = (width - targetWidth) / 2;
        var top = (height - targetHeight) / 2;

        var properties = new DwmThumbnailProperties
        {
            Flags = DwmTnpRectDestination | DwmTnpOpacity |
                DwmTnpVisible | DwmTnpSourceClientAreaOnly,
            Destination = new NativeRect
            {
                Left = left, Top = top,
                Right = left + targetWidth, Bottom = top + targetHeight
            },
            Opacity = byte.MaxValue,
            Visible = true,
            SourceClientAreaOnly = true
        };
        Marshal.ThrowExceptionForHR(DwmUpdateThumbnailProperties(_thumbnail, ref properties));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width, Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmThumbnailProperties
    {
        public uint Flags;
        public NativeRect Destination;
        public NativeRect Source;
        public byte Opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool Visible;
        [MarshalAs(UnmanagedType.Bool)] public bool SourceClientAreaOnly;
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmRegisterThumbnail(nint destination, nint source,
        out nint thumbnail);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmUnregisterThumbnail(nint thumbnail);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmQueryThumbnailSourceSize(nint thumbnail, out NativeSize size);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmUpdateThumbnailProperties(nint thumbnail,
        ref DwmThumbnailProperties properties);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint hwnd, out NativeRect rect);
}
