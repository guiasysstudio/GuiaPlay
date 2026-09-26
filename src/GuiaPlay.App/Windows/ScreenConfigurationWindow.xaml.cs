using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
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
    private readonly WindowsIntegrationService _windowsIntegration;
    private readonly bool _playbackActive;
    private CancellationTokenSource? _downloadCancellation;
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
        WindowsIntegrationService windowsIntegration,
        bool openUpdates,
        bool playbackActive)
    {
        InitializeComponent();
        _updateManager = updateManager;
        _windowsIntegration = windowsIntegration;
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
        AboutVersionText.Text = $"Versão {ProductInfo.Version}";
        AboutReleaseDateText.Text = $"Data da versão: {ProductInfo.ReleaseDate:dd/MM/yyyy}";
        CheckUpdatesAutomaticallyCheckBox.IsChecked = settings.CheckUpdatesAutomatically;
        InstallUpdatesAutomaticallyCheckBox.IsChecked = settings.InstallUpdatesAutomatically;
        SettingsTabs.SelectedItem = openUpdates ? UpdatesTab : ScreensTab;
        ScreensTab.IsEnabled = !playbackActive;
        AppearanceAudioTab.IsEnabled = !playbackActive;
        _updateManager.StateChanged += UpdateManager_OnStateChanged;
        Closing += ScreenConfigurationWindow_OnClosing;
        Closed += (_, _) => _updateManager.StateChanged -= UpdateManager_OnStateChanged;
        RefreshUpdateUi();
        RefreshWindowsIntegrationUi();
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

    private void OpenProjectPageButton_OnClick(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(ProductInfo.ProjectPageUri.AbsoluteUri)
        {
            UseShellExecute = true
        });

    private void ConfigureWindowsIntegrationButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var state = _windowsIntegration.Configure(
                RegisterAssociationsCheckBox.IsChecked == true,
                RegisterContextMenuCheckBox.IsChecked == true);
            ApplyWindowsIntegrationState(state);
            WindowsIntegrationMessageText.Text = state.ExplorerIntegrationEnabled
                ? "Integração atualizada. O Explorer pode levar alguns instantes para renovar os menus."
                : "Integração removida. Nenhuma associação de outros aplicativos foi alterada.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            WindowsIntegrationMessageText.Text = $"Não foi possível atualizar a integração: {exception.Message}";
            RefreshWindowsIntegrationUi();
        }
    }

    private void OpenDefaultAppsButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
            WindowsIntegrationMessageText.Text = "Escolha o aplicativo padrão diretamente nas Configurações do Windows.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            WindowsIntegrationMessageText.Text = $"Não foi possível abrir as Configurações do Windows: {exception.Message}";
        }
    }

    private void RefreshWindowsIntegrationUi()
    {
        try
        {
            ApplyWindowsIntegrationState(_windowsIntegration.GetState());
            ConfigureWindowsIntegrationButton.IsEnabled = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            ExplorerIntegrationStatusText.Text = "Indisponível";
            AssociationsStatusText.Text = "Indisponíveis";
            ConfigureWindowsIntegrationButton.IsEnabled = false;
            WindowsIntegrationMessageText.Text = $"Não foi possível consultar o Registry: {exception.Message}";
        }
    }

    private void ApplyWindowsIntegrationState(WindowsIntegrationState state)
    {
        ExplorerIntegrationStatusText.Text = state.ContextMenuRegistered ? "Ativada" : "Desativada";
        AssociationsStatusText.Text = state.AssociationsRegistered ? "Registradas" : "Não registradas";
        RegisterAssociationsCheckBox.IsChecked = state.AssociationsRegistered;
        RegisterContextMenuCheckBox.IsChecked = state.ContextMenuRegistered;
    }

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
        _downloadCancellation = new CancellationTokenSource();
        try
        {
            var result = await _updateManager.PrepareAndLaunchInstallerAsync(_downloadCancellation.Token);
            UpdateStatusText.Text = result.Message;
            if (result.Launched)
            {
                Application.Current.Shutdown();
            }
        }
        finally
        {
            _downloadCancellation.Dispose();
            _downloadCancellation = null;
            CheckUpdatesButton.IsEnabled = true;
            InstallUpdateButton.IsEnabled = true;
        }
    }

    private void CancelDownloadButton_OnClick(object sender, RoutedEventArgs e)
    {
        CancelDownloadButton.IsEnabled = false;
        _downloadCancellation?.Cancel();
    }

    private void ScreenConfigurationWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_updateManager.IsPreparing)
        {
            return;
        }

        if (_updateManager.PreparationProgress.Stage == UpdatePreparationStage.Downloading)
        {
            _downloadCancellation?.Cancel();
            UpdateStatusText.Text = "Cancelando download...";
        }

        e.Cancel = true;
    }

    private void UpdateManager_OnStateChanged(object? sender, EventArgs e) =>
        _ = Dispatcher.BeginInvoke(RefreshUpdateUi);

    private void RefreshUpdateUi()
    {
        CheckUpdatesButton.IsEnabled = !_updateManager.IsChecking;
        if (_updateManager.IsPreparing)
        {
            RefreshPreparationUi(_updateManager.PreparationProgress);
            return;
        }

        UpdateProgressPanel.Visibility = Visibility.Collapsed;
        CancelDownloadButton.Visibility = Visibility.Collapsed;
        CloseSettingsButton.IsEnabled = true;
        SaveSettingsButton.IsEnabled = true;
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

    private void RefreshPreparationUi(UpdatePreparationProgress progress)
    {
        CheckUpdatesButton.IsEnabled = false;
        CloseSettingsButton.IsEnabled = false;
        SaveSettingsButton.IsEnabled = false;
        InstallUpdateButton.Visibility = Visibility.Collapsed;
        UpdateProgressPanel.Visibility = Visibility.Visible;
        CancelDownloadButton.Visibility = progress.Stage == UpdatePreparationStage.Downloading
            ? Visibility.Visible
            : Visibility.Collapsed;
        CancelDownloadButton.IsEnabled = true;

        UpdateStatusText.Text = progress.Stage switch
        {
            UpdatePreparationStage.Downloading => "Baixando atualização...",
            UpdatePreparationStage.VerifyingIntegrity => "Verificando integridade...",
            UpdatePreparationStage.PreparingFiles => "Preparando arquivos...",
            UpdatePreparationStage.StartingUpdater => "Iniciando atualizador...",
            _ => "Preparando atualização..."
        };

        var downloading = progress.Stage == UpdatePreparationStage.Downloading;
        UpdateProgressBar.IsIndeterminate = !downloading || progress.TotalBytes is null;
        UpdateProgressBar.Value = downloading ? progress.Percentage ?? 0 : 0;
        UpdateProgressText.Text = progress.Stage switch
        {
            UpdatePreparationStage.Downloading when progress.TotalBytes is { } total =>
                $"{FormatBytes(progress.BytesReceived)} / {FormatBytes(total)}" +
                (progress.Percentage is { } percentage ? $" — {percentage}%" : string.Empty),
            UpdatePreparationStage.Downloading => $"{FormatBytes(progress.BytesReceived)} baixados",
            _ => "Esta etapa não possui percentual estimado."
        };
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = Math.Max(0, bytes);
        var unit = 0;
        var display = (double)value;
        while (display >= 1024 && unit < units.Length - 1)
        {
            display /= 1024;
            unit++;
        }

        return $"{display:0.0} {units[unit]}";
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
