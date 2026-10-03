using System.Windows;
using System.Windows.Controls;
using GuiaPlay.Core;

namespace GuiaPlay.App.Windows;

internal sealed class TextPromptWindow : Window
{
    private readonly TextBox _textBox;

    public TextPromptWindow(
        string title,
        string prompt,
        string initialValue = "",
        string acceptText = "OK")
    {
        Title = title;
        Width = 430;
        MinWidth = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });

        _textBox = new TextBox
        {
            Text = initialValue,
            Margin = new Thickness(0, 12, 0, 16),
            MinHeight = 36,
            MaxLength = 80,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(_textBox, 1);
        grid.Children.Add(_textBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = new Button { Content = "Cancelar", MinWidth = 88, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var accept = new Button
        {
            Content = acceptText,
            MinWidth = 88,
            IsDefault = true,
            IsEnabled = PlaylistGroupName.IsValid(initialValue)
        };
        cancel.SetResourceReference(StyleProperty, "StandardButtonStyle");
        accept.SetResourceReference(StyleProperty, "PrimaryButtonStyle");
        _textBox.TextChanged += (_, _) => accept.IsEnabled = PlaylistGroupName.IsValid(_textBox.Text);
        accept.Click += (_, _) =>
        {
            if (PlaylistGroupName.IsValid(_textBox.Text))
            {
                DialogResult = true;
            }
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(accept);
        Grid.SetRow(buttons, 2);
        grid.Children.Add(buttons);
        Content = grid;
        ContentRendered += (_, _) =>
        {
            _textBox.Focus();
            _textBox.SelectAll();
        };
    }

    public string Value => _textBox.Text.Trim();
}
