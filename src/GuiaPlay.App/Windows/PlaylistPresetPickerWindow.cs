using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using GuiaPlay.Core;

namespace GuiaPlay.App.Windows;

internal sealed class PlaylistPresetPickerWindow : Window
{
    private readonly ObservableCollection<PlaylistPresetChoice> _choices;
    private readonly Func<PlaylistPreset, PlaylistPresetDeleteResult> _delete;
    private readonly ListView _list;
    private readonly Button _loadButton;
    private readonly Button _deleteButton;

    public PlaylistPresetPickerWindow(
        IReadOnlyList<PlaylistPreset> presets,
        int warningCount,
        Func<PlaylistPreset, PlaylistPresetDeleteResult> delete)
    {
        ArgumentNullException.ThrowIfNull(presets);
        ArgumentNullException.ThrowIfNull(delete);
        _delete = delete;
        _choices = new ObservableCollection<PlaylistPresetChoice>(
            presets.Select(preset => new PlaylistPresetChoice(preset)));

        Title = "Carregar playlist";
        Width = 560;
        Height = 410;
        MinWidth = 460;
        MinHeight = 330;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.CanResize;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var introduction = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        introduction.Children.Add(new TextBlock
        {
            Text = "Playlists salvas",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        });
        introduction.Children.Add(new TextBlock
        {
            Text = warningCount == 0
                ? "Escolha uma playlist para substituir a lista de trabalho. Nenhuma mídia será reproduzida automaticamente."
                : $"{warningCount} arquivo(s) de preset com problema foram ignorados; consulte o log.",
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetRow(introduction, 0);
        root.Children.Add(introduction);

        _list = new ListView
        {
            ItemsSource = _choices,
            MinHeight = 180
        };
        var view = new GridView();
        view.Columns.Add(new GridViewColumn
        {
            Header = "Nome",
            Width = 300,
            DisplayMemberBinding = new Binding(nameof(PlaylistPresetChoice.Name))
        });
        view.Columns.Add(new GridViewColumn
        {
            Header = "Atualizada em",
            Width = 170,
            DisplayMemberBinding = new Binding(nameof(PlaylistPresetChoice.UpdatedDisplay))
        });
        _list.View = view;
        _list.SelectionChanged += (_, _) => RefreshButtons();
        _list.MouseDoubleClick += List_OnMouseDoubleClick;
        Grid.SetRow(_list, 1);
        root.Children.Add(_list);

        var buttons = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _deleteButton = new Button
        {
            Content = "Excluir",
            MinWidth = 88,
            IsEnabled = false
        };
        _deleteButton.SetResourceReference(StyleProperty, "StandardButtonStyle");
        _deleteButton.Click += DeleteButton_OnClick;
        Grid.SetColumn(_deleteButton, 0);
        buttons.Children.Add(_deleteButton);

        var cancelButton = new Button
        {
            Content = "Cancelar",
            MinWidth = 88,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true
        };
        cancelButton.SetResourceReference(StyleProperty, "StandardButtonStyle");
        Grid.SetColumn(cancelButton, 2);
        buttons.Children.Add(cancelButton);

        _loadButton = new Button
        {
            Content = "Carregar",
            MinWidth = 88,
            IsDefault = true,
            IsEnabled = false
        };
        _loadButton.SetResourceReference(StyleProperty, "PrimaryButtonStyle");
        _loadButton.Click += (_, _) => LoadSelected();
        Grid.SetColumn(_loadButton, 3);
        buttons.Children.Add(_loadButton);

        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        Content = root;

        ContentRendered += (_, _) =>
        {
            if (_choices.Count > 0)
            {
                _list.SelectedIndex = 0;
                _list.Focus();
            }
        };
    }

    public PlaylistPreset? SelectedPreset { get; private set; }

    private PlaylistPresetChoice? SelectedChoice => _list.SelectedItem as PlaylistPresetChoice;

    private void RefreshButtons()
    {
        var hasSelection = SelectedChoice is not null;
        _loadButton.IsEnabled = hasSelection;
        _deleteButton.IsEnabled = hasSelection;
    }

    private void List_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedChoice is not null)
        {
            LoadSelected();
        }
    }

    private void LoadSelected()
    {
        if (SelectedChoice is not { } choice)
        {
            return;
        }

        SelectedPreset = choice.Preset;
        DialogResult = true;
    }

    private void DeleteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedChoice is not { } choice)
        {
            return;
        }

        if (MessageBox.Show(
                this,
                $"Excluir a playlist salva “{choice.Name}”?\n\nSomente o arquivo JSON do preset será excluído. As mídias originais não serão alteradas.",
                "Excluir playlist salva",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var result = _delete(choice.Preset);
        if (!result.Succeeded)
        {
            MessageBox.Show(
                this,
                result.Error ?? "Não foi possível excluir a playlist salva.",
                "GuiaPlay — excluir playlist",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        var index = _choices.IndexOf(choice);
        _choices.Remove(choice);
        if (_choices.Count > 0)
        {
            _list.SelectedIndex = Math.Min(index, _choices.Count - 1);
        }

        RefreshButtons();
    }

    private sealed class PlaylistPresetChoice(PlaylistPreset preset)
    {
        public PlaylistPreset Preset { get; } = preset;
        public string Name => Preset.Name;
        public string UpdatedDisplay => Preset.UpdatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    }
}
