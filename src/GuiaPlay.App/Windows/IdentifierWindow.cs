using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GuiaPlay.App.Models;
using GuiaPlay.App.Services;

namespace GuiaPlay.App.Windows;

internal sealed class IdentifierWindow : Window
{
    private const double IdentifierSize = 112;
    private readonly Border _container;
    private readonly TextBlock _number;

    public IdentifierWindow(MonitorInfo monitor)
    {
        MonitorId = monitor.Id;
        Width = IdentifierSize;
        Height = IdentifierSize;
        SizeToContent = SizeToContent.Manual;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;

        _number = new TextBlock
        {
            Text = monitor.Number.ToString(),
            FontSize = 62,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _container = new Border
        {
            Width = IdentifierSize,
            Height = IdentifierSize,
            CornerRadius = new CornerRadius(18),
            Child = _number
        };
        Content = _container;
        ApplyContrastColors();

        SourceInitialized += (_, _) => WindowPlacement.PlaceIdentifier(this, monitor, IdentifierSize);
        SystemParameters.StaticPropertyChanged += SystemParameters_OnStaticPropertyChanged;
        Closed += (_, _) => SystemParameters.StaticPropertyChanged -= SystemParameters_OnStaticPropertyChanged;
    }

    public string MonitorId { get; }

    private void SystemParameters_OnStaticPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
        {
            if (Dispatcher.CheckAccess())
            {
                ApplyContrastColors();
            }
            else
            {
                _ = Dispatcher.BeginInvoke(ApplyContrastColors);
            }
        }
    }

    private void ApplyContrastColors()
    {
        _container.Background = SystemParameters.HighContrast
            ? SystemColors.HighlightBrush
            : Application.Current.TryFindResource("IdentifierBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(55, 184, 120));
        _number.Foreground = SystemParameters.HighContrast ? SystemColors.HighlightTextBrush : Brushes.White;
    }
}
