using System.Diagnostics;
using System.Windows;
using GuiaPlay.Core;

namespace GuiaPlay.App.Windows;

/// <summary>Dedicated product information, never the legacy settings notebook.</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Versão {ProductInfo.Version}";
        ReleaseDateText.Text = $"Data da versão: {ProductInfo.ReleaseDate:dd/MM/yyyy}";
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();

    private void StudioPage_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(ProductInfo.StudioPageUri.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Não foi possível abrir o site",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
