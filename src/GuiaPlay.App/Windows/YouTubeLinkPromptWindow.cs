using System.Windows;
using System.Windows.Controls;
using GuiaPlay.Core;

namespace GuiaPlay.App.Windows;

internal sealed class YouTubeLinkPromptWindow : Window
{
    private readonly TextBox _urlBox;
    private readonly TextBox _nameBox;

    public YouTubeLinkPromptWindow()
    {
        Title = "GuiaPlay — Adicionar link do YouTube";
        Width = 580;
        MinWidth = 430;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock
        {
            Text = "Cole o link de um vídeo público do YouTube.",
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(new TextBlock
        {
            Text = "Sem tela pública selecionada, o player abre em uma janela do GuiaPlay. " +
                   "Com uma tela pública selecionada, o vídeo aparece nela. " +
                   "O player oficial mantém os controles e a identificação do YouTube.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 12)
        });
        _urlBox = new TextBox
        {
            MaxLength = 2048,
            MinHeight = 34,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        root.Children.Add(_urlBox);
        root.Children.Add(new TextBlock
        {
            Text = "Nome para a playlist (opcional, até 80 caracteres)",
            Margin = new Thickness(0, 13, 0, 4)
        });
        _nameBox = new TextBox
        {
            MaxLength = 80,
            MinHeight = 34,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        root.Children.Add(_nameBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var cancel = new Button { Content = "Cancelar", MinWidth = 90, IsCancel = true };
        var add = new Button
        {
            Content = "Adicionar e abrir",
            MinWidth = 125,
            IsDefault = true,
            Margin = new Thickness(8, 0, 0, 0)
        };
        cancel.SetResourceReference(StyleProperty, "StandardButtonStyle");
        add.SetResourceReference(StyleProperty, "PrimaryButtonStyle");
        add.Click += (_, _) =>
        {
            if (YouTubeVideoReferenceParser.TryParse(_urlBox.Text, out _))
            {
                DialogResult = true;
            }
            else
            {
                MessageBox.Show(this, "O link não corresponde a um vídeo do YouTube válido.",
                    "Link inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(add);
        root.Children.Add(buttons);
        Content = root;
        ContentRendered += (_, _) => _ = _urlBox.Focus();
    }

    public YouTubeVideoReference Reference =>
        YouTubeVideoReferenceParser.TryParse(_urlBox.Text, out var video)
            ? video!
            : throw new InvalidOperationException("O link informado não foi validado.");

    public string DisplayName => _nameBox.Text.Trim();
}
