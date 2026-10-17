using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using GuiaPlay.App.Models;
using GuiaPlay.App.Services;

namespace GuiaPlay.App.Windows;

/// <summary>
/// Operator-only fullscreen view of the existing preview. It never creates a VLC
/// player, sends an output to the operator display or changes output selections.
/// Escape/Alt+F4 return to the control desk.
/// </summary>
internal sealed class OperatorPreviewWindow : Window
{
    public OperatorPreviewWindow(MonitorInfo operatorMonitor, Image sourcePreview)
    {
        Title = "GuiaPlay — Prévia em tela cheia (Esc para sair)";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = true;
        Background = Brushes.Black;

        var preview = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        preview.SetBinding(Image.SourceProperty, new Binding(nameof(Image.Source))
        {
            Source = sourcePreview,
            Mode = BindingMode.OneWay
        });
        Content = preview;

        SourceInitialized += (_, _) => WindowPlacement.FillMonitor(this, operatorMonitor);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape || e.Key == Key.F11)
            {
                Close();
                e.Handled = true;
            }
        };
    }
}
