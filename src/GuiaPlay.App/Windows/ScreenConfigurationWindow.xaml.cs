using System.ComponentModel;
using System.Windows;
using GuiaPlay.App.Models;
using GuiaPlay.App.Services;
using GuiaPlay.Core;

namespace GuiaPlay.App.Windows;

internal sealed record ScreenConfigurationResult(
    MonitorInfo OperatorMonitor,
    IReadOnlyDictionary<string, string> PersistentNames,
    AppearancePreference Appearance,
    AudioOutputPreference AudioOutput,
    bool CheckUpdatesAutomatically,
    bool InstallUpdatesAutomatically);

public partial class ScreenConfigurationWindow : Window
{
    private readonly IReadOnlyList<ScreenConfigurationItem> _items;
    private readonly UpdateManager _updateManager;
    private readonly bool _playbackActive;
    private readonly SettingsAppearanceChoice[] _appearanceChoices =
    [
        new(AppearancePreference.System, "Sistema"),
        new(AppearancePreference.Light, "Claro"),
        new(AppearancePreference.Dark, "Escuro")
    ];

    internal ScreenConfigurationWindow(
        IReadOnlyList<MonitorInfo> monitors,
        string? selectedOperatorSessionId,
        AppSettings settings,
        IReadOnlyList<AudioDeviceIdentity> audioDevices,
        UpdateManager updateManager,
        bool openUpdates,
        bool playbackActive)
    {
        InitializeComponent();
        _updateManager = updateManager;
        _playbackActive = playbackActive;
        _items = monitors.Select(monitor => new ScreenConfigurationItem
        {
            Monitor = monitor,
            IsOperator = string.Equals(monitor.Id, selectedOperatorSessionId, StringComparison.OrdinalIgnoreCase),
            CustomName = monitor.PersistenceKey is { } key && settings.MonitorNames.TryGetValue(key, out var name) ? name : string.Empty
        }).ToArray();
        ScreensList.ItemsSource = _items;
        AppearanceCombo.ItemsSource = _appearanceChoices;
        AppearanceCombo.SelectedItem = _appearanceChoices.First(choice => choice.Preference == settings.Appearance);

        var audioChoices = new List<SettingsAudioChoice>
        {
            new(AudioOutputPreference.Default, "Padrão do Windows", true)
        };
        audioChoices.AddRange(audioDevices.Select(device => new SettingsAudioChoice(
            new AudioOutputPreference(AudioOutputMode.Explicit, device.Module, device.DeviceId, device.DisplayName),
            $"{device.DisplayName} ({device.Module})",
            true)));
        var savedAudioIsPresent = audioChoices.Any(choice => AudioPreferencesEqual(choice.Preference, settings.AudioOutput));
        if (!savedAudioIsPresent && settings.AudioOutput.IsExplicit)
        {
            audioChoices.Add(new SettingsAudioChoice(
                settings.AudioOutput,
                $"{settings.AudioOutput.DisplayName ?? settings.AudioOutput.DeviceId ?? "Dispositivo salvo"} — indisponível",
                false));
        }

        AudioOutputCombo.ItemsSource = audioChoices;
        AudioOutputCombo.SelectedItem = audioChoices.First(choice => AudioPreferencesEqual(choice.Preference, settings.AudioOutput));
        AudioHelpText.Text = audioDevices.Count == 0
            ? "O LibVLC não retornou dispositivos. Isso não prova ausência de áudio; o padrão do Windows permanece disponível."
            : "A seleção é aplicada pelo identificador fornecido pelo LibVLC no início de cada reprodução.";

        var connectedIds = monitors.Select(monitor => monitor.PersistenceKey).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var disconnectedNames = settings.MonitorNames
            .Where(pair => !connectedIds.Contains(pair.Key))
            .Select(pair => pair.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        DisconnectedText.Text = disconnectedNames.Length == 0
            ? string.Empty
            : $"Telas conhecidas ausentes (preferências preservadas): {string.Join(", ", disconnectedNames)}.";

        CurrentVersionText.Text = ProductInfo.Version;
        CurrentReleaseDateText.Text = ProductInfo.ReleaseDate.ToString("dd/MM/yyyy");
        CheckUpdatesAutomaticallyCheckBox.IsChecked = settings.CheckUpdatesAutomatically;
        InstallUpdatesAutomaticallyCheckBox.IsChecked = settings.InstallUpdatesAutomatically;
        SettingsTabs.SelectedItem = openUpdates ? UpdatesTab : ScreensTab;
        ScreensTab.IsEnabled = !playbackActive;
        AppearanceAudioTab.IsEnabled = !playbackActive;
        _updateManager.StateChanged += UpdateManager_OnStateChanged;
        Closed += (_, _) => _updateManager.StateChanged -= UpdateManager_OnStateChanged;
        RefreshUpdateUi();
    }

    internal ScreenConfigurationResult? Result { get; private set; }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = _items.Where(item => item.IsOperator).ToArray();
        if (selected.Length != 1)
        {
            ValidationText.Text = "Escolha exatamente uma tela para o operador.";
            return;
        }

        if (AppearanceCombo.SelectedItem is not SettingsAppearanceChoice appearance ||
            AudioOutputCombo.SelectedItem is not SettingsAudioChoice { IsAvailable: true } audio)
        {
            ValidationText.Text = "Escolha uma aparência e uma saída de áudio disponível.";
            return;
        }

        var names = _items
            .Where(item => item.Monitor.PersistenceKey is not null && !string.IsNullOrWhiteSpace(item.CustomName))
            .ToDictionary(
                item => item.Monitor.PersistenceKey!,
                item => item.CustomName.Trim(),
                StringComparer.OrdinalIgnoreCase);
        Result = new ScreenConfigurationResult(
            selected[0].Monitor,
            names,
            appearance.Preference,
            audio.Preference,
            CheckUpdatesAutomaticallyCheckBox.IsChecked == true,
            InstallUpdatesAutomaticallyCheckBox.IsChecked == true);
        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private async void CheckUpdatesButton_OnClick(object sender, RoutedEventArgs e)
    {
        UpdateStatusText.Text = "Verificando...";
        await _updateManager.CheckAsync(manual: true);
        RefreshUpdateUi();
    }

    private async void InstallUpdateButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_playbackActive)
        {
            UpdateStatusText.Text = "A atualização está pendente. Pare a reprodução antes de instalar.";
            return;
        }

        CheckUpdatesButton.IsEnabled = false;
        InstallUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "Baixando e preparando a atualização...";
        var result = await _updateManager.PrepareAndLaunchInstallerAsync();
        UpdateStatusText.Text = result.Message;
        CheckUpdatesButton.IsEnabled = true;
        InstallUpdateButton.IsEnabled = true;
        if (result.Launched)
        {
            Application.Current.Shutdown();
        }
    }

    private void UpdateManager_OnStateChanged(object? sender, EventArgs e) =>
        _ = Dispatcher.BeginInvoke(RefreshUpdateUi);

    private void RefreshUpdateUi()
    {
        CheckUpdatesButton.IsEnabled = !_updateManager.IsChecking;
        if (_updateManager.IsChecking)
        {
            UpdateStatusText.Text = "Verificando...";
            InstallUpdateButton.Visibility = Visibility.Collapsed;
            return;
        }

        var result = _updateManager.LastResult;
        UpdateStatusText.Text = result?.Status switch
        {
            UpdateCheckStatus.UpToDate => "Você está usando a versão mais recente.",
            UpdateCheckStatus.UpdateAvailable => $"Nova versão disponível: {result.Release!.Version}",
            UpdateCheckStatus.NoPublishedVersion => "Nenhuma versão publicada para este canal.",
            UpdateCheckStatus.Failed => "Não foi possível verificar atualizações. Verifique sua conexão.",
            _ => "Ainda não verificado."
        };
        var hasUpdate = result?.Status == UpdateCheckStatus.UpdateAvailable;
        AvailableVersionText.Visibility = hasUpdate ? Visibility.Visible : Visibility.Collapsed;
        ReleaseNotesText.Visibility = hasUpdate && !string.IsNullOrWhiteSpace(result?.Release?.Notes)
            ? Visibility.Visible
            : Visibility.Collapsed;
        InstallUpdateButton.Visibility = hasUpdate ? Visibility.Visible : Visibility.Collapsed;
        InstallUpdateButton.IsEnabled = !_playbackActive;
        if (hasUpdate)
        {
            AvailableVersionText.Text = $"Publicada em {result!.Release!.PublishedAt.ToLocalTime():dd/MM/yyyy}.";
            ReleaseNotesText.Text = result.Release.Notes;
            InstallUpdateButton.ToolTip = _playbackActive
                ? "Pare a reprodução para instalar com segurança."
                : "Baixar, validar e instalar a atualização";
        }
    }

    private static bool AudioPreferencesEqual(AudioOutputPreference left, AudioOutputPreference right) =>
        left.Mode == right.Mode &&
        string.Equals(left.Module, right.Module, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.DeviceId, right.DeviceId, StringComparison.Ordinal);

    private sealed record SettingsAppearanceChoice(AppearancePreference Preference, string Label);
    private sealed record SettingsAudioChoice(AudioOutputPreference Preference, string Label, bool IsAvailable);

    private sealed class ScreenConfigurationItem : INotifyPropertyChanged
    {
        private bool _isOperator;
        private string _customName = string.Empty;

        public required MonitorInfo Monitor { get; init; }
        public bool CanPersist => Monitor.PersistenceKey is not null;
        public string IdentitySummary => CanPersist
            ? $"{Monitor.Details} · identidade persistente confirmada"
            : $"{Monitor.Details} · identidade ambígua; nome e restauração não serão salvos";

        public bool IsOperator
        {
            get => _isOperator;
            set
            {
                if (_isOperator == value)
                {
                    return;
                }

                _isOperator = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOperator)));
            }
        }

        public string CustomName
        {
            get => _customName;
            set
            {
                if (_customName == value)
                {
                    return;
                }

                _customName = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CustomName)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
