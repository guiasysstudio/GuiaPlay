using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GuiaPlay.Core;

namespace GuiaPlay.App.Windows;

/// <summary>
/// Discoverable help for actual window-scoped operator commands.
/// Rows come from the same policy that dispatches keyboard input.
/// </summary>
internal sealed class OperatorShortcutHelpWindow : Window
{
    public OperatorShortcutHelpWindow()
    {
        Title = "GuiaPlay — Atalhos de teclado";
        Width = 635;
        Height = 620;
        MinWidth = 420;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.CanResize;
        SetResourceReference(BackgroundProperty, "GuiaPlayWindowBackgroundBrush");
        SetResourceReference(ForegroundProperty, "GuiaPlayTextPrimaryBrush");

        var layout = new Grid { Margin = new Thickness(20) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        title.Children.Add(new TextBlock
        {
            Text = "Atalhos para o operador",
            FontSize = 23,
            FontWeight = FontWeights.SemiBold
        });
        title.Children.Add(new TextBlock
        {
            Text = "Funcionam com a janela principal ativa. Não interrompem a digitação em campos e formulários.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0)
        });
        Grid.SetRow(title, 0);
        layout.Children.Add(title);

        var commands = new StackPanel();
        foreach (var binding in OperatorShortcutPolicy.Bindings)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var gesture = new TextBlock
            {
                Text = binding.Gesture,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            gesture.SetResourceReference(TextBlock.ForegroundProperty, "GuiaPlayAccentBrush");
            row.Children.Add(gesture);

            var description = new TextBlock
            {
                Text = binding.Description,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(description, 1);
            row.Children.Add(description);
            commands.Children.Add(row);
        }

        var scroll = new ScrollViewer
        {
            Content = commands,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);

        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
        footer.Children.Add(new TextBlock
        {
            Text = "Esc fecha esta janela. Nenhum atalho controla outras aplicações.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        });
        var close = new Button
        {
            Content = "Fechar",
            MinWidth = 100,
            MinHeight = 34,
            Margin = new Thickness(12, 0, 0, 0),
            IsDefault = true,
            IsCancel = true
        };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        footer.Children.Add(close);
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);

        Content = layout;
        Loaded += (_, _) => _ = close.Focus();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }
}
