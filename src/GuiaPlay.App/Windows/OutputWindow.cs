using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GuiaPlay.App.Models;
using GuiaPlay.App.Services;

namespace GuiaPlay.App.Windows;

internal sealed class OutputWindow : Window
{
    private readonly Image _image;

    public OutputWindow(MonitorInfo monitor, ImageSource? source)
    {
        Monitor = monitor;
        Title = "GuiaPlay — saída";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Black;
        _image = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Source = source
        };
        Content = _image;
        SourceInitialized += (_, _) => WindowPlacement.FillMonitor(this, Monitor);
    }

    public MonitorInfo Monitor { get; private set; }

    public void SetSource(ImageSource? source) => _image.Source = source;

    public void Reposition(MonitorInfo monitor)
    {
        Monitor = monitor;
        WindowPlacement.FillMonitor(this, monitor);
    }
}
