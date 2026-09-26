using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GuiaPlay.App.Models;
using GuiaPlay.App.Playback;
using GuiaPlay.App.Services;
using GuiaPlay.Core;

namespace GuiaPlay.App.Windows;

internal sealed record ScreenConfigurationResult(
    MonitorInfo OperatorMonitor,
    IReadOnlyDictionary<string, string> PersistentNames,
    AppearancePreference Appearance,
    AccentColorPreference AccentColor,
    AudioOutputPreference AudioOutput,
    EqualizerConfiguration Equalizer,
    bool CheckUpdatesAutomatically,
    bool InstallUpdatesAutomatically);

public partial class ScreenConfigurationWindow : Window
{
    private readonly IReadOnlyList<ScreenConfigurationItem> _items;
    private readonly UpdateManager _updateManager;
    private readonly WindowsIntegrationService _windowsIntegration;
    private readonly Func<DiagnosticSnapshot> _diagnosticSnapshot;
    private readonly DispatcherTimer _diagnosticTimer;
    private readonly bool _playbackActive;
    private readonly LibVlcEqualizerCatalog _equalizerCatalog;
    private readonly EqualizerConfiguration _savedEqualizer;
    private readonly ObservableCollection<EqualizerBandItem> _equalizerBands = [];
    private readonly IReadOnlyList<SettingsEqualizerPresetChoice> _equalizerPresets;
    private bool _loadingEqualizer;
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
        LibVlcEqualizerCatalog equalizerCatalog,
        UpdateManager updateManager,
        WindowsIntegrationService windowsIntegration,
        Func<DiagnosticSnapshot> diagnosticSnapshot,
        bool openUpdates,
        bool playbackActive)
    {
        InitializeComponent();
        _updateManager = updateManager;
        _windowsIntegration = windowsIntegration;
        _diagnosticSnapshot = diagnosticSnapshot;
        _playbackActive = playbackActive;
        _equalizerCatalog = equalizerCatalog;
        _savedEqualizer = settings.Equalizer;
        _items = monitors.Select(monitor => new ScreenConfigurationItem
        {
            Monitor = monitor,
            IsOperator = string.Equals(monitor.Id, selectedOperatorSessionId, StringComparison.OrdinalIgnoreCase),
            CustomName = monitor.PersistenceKey is { } key && settings.MonitorNames.TryGetValue(key, out var name) ? name : string.Empty
        }).ToArray();
        ScreensList.ItemsSource = _items;
        AppearanceCombo.ItemsSource = _appearanceChoices;
        AppearanceCombo.SelectedItem = _appearanceChoices.First(choice => choice.Preference == settings.Appearance);
        SetAppearanceSelection(settings.Appearance, settings.AccentColor);

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
        AudioOutputCombo.IsEnabled = !playbackActive;
        AudioHelpText.Text = audioDevices.Count == 0
            ? "O LibVLC não retornou dispositivos. Isso não prova ausência de áudio; o padrão do Windows permanece disponível."
            : "A seleção é aplicada pelo identificador fornecido pelo LibVLC no início de cada reprodução.";

        _equalizerPresets = equalizerCatalog.Presets
            .Select(preset => new SettingsEqualizerPresetChoice(preset, preset.Name))
            .Append(new SettingsEqualizerPresetChoice(null, EqualizerConfiguration.CustomPresetName))
            .ToArray();
        EqualizerPresetCombo.ItemsSource = _equalizerPresets;
        InitializeEqualizer(settings.Equalizer);

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
        SettingsTabs.SelectedItem = openUpdates ? UpdatesTab : playbackActive || monitors.Count == 0 ? DiagnosticsTab : ScreensTab;
        ScreensTab.IsEnabled = !playbackActive;
        _updateManager.StateChanged += UpdateManager_OnStateChanged;
        _diagnosticTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(2),
            DispatcherPriority.Background,
            (_, _) => RefreshDiagnostics(),
            Dispatcher);
        _diagnosticTimer.Start();
        Closing += ScreenConfigurationWindow_OnClosing;
        Closed += (_, _) =>
        {
            _diagnosticTimer.Stop();
            _updateManager.StateChanged -= UpdateManager_OnStateChanged;
        };
        RefreshUpdateUi();
        RefreshWindowsIntegrationUi();
        RefreshDiagnostics();
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
            SelectedAccentColor(),
            audio.Preference,
            ReadEqualizerConfiguration(),
            CheckUpdatesAutomaticallyCheckBox.IsChecked == true,
            InstallUpdatesAutomaticallyCheckBox.IsChecked == true);
        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void AppearanceSelection_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || AppearancePreviewAccent is null)
        {
            return;
        }

        var palette = AppearancePaletteResolver.Resolve(
            SelectedAccentColor(),
            AppearancePaletteResolver.ResolveMode(
                SelectedAppearance(),
                App.WindowsUsesDarkMode(),
                SystemParameters.HighContrast));
        if (palette.UsesSystemColors)
        {
            AppearancePreviewAccent.Background = SystemColors.HighlightBrush;
            AppearancePreviewAccent.Foreground = SystemColors.HighlightTextBrush;
            AppearancePreviewCard.BorderBrush = SystemColors.HighlightBrush;
            AppearancePreviewCard.Background = SystemColors.WindowBrush;
            return;
        }

        var accent = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette.AccentHex));
        AppearancePreviewAccent.Background = accent;
        AppearancePreviewAccent.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette.ForegroundHex));
        AppearancePreviewCard.BorderBrush = accent;
        AppearancePreviewCard.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette.SubtleHex));
    }

    private void EqualizerEnabledCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (EqualizerControlsPanel is not null)
        {
            EqualizerControlsPanel.IsEnabled = EqualizerEnabledCheckBox.IsChecked == true && _equalizerCatalog.IsAvailable;
        }
    }

    private void EqualizerPresetCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingEqualizer || EqualizerPresetCombo.SelectedItem is not SettingsEqualizerPresetChoice { Preset: { } preset })
        {
            return;
        }

        _loadingEqualizer = true;
        try
        {
            PreampSlider.Value = preset.Preamp;
            for (var index = 0; index < _equalizerBands.Count; index++)
            {
                _equalizerBands[index].Gain = preset.BandGains[index];
            }
        }
        finally
        {
            _loadingEqualizer = false;
        }
    }

    private void EqualizerValue_OnChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingEqualizer ||
            EqualizerPresetCombo?.SelectedItem is not SettingsEqualizerPresetChoice { Preset: { } preset })
        {
            return;
        }

        var stillMatchesPreset = Math.Abs(PreampSlider.Value - preset.Preamp) < 0.001 &&
                                 _equalizerBands.Count == preset.BandGains.Count &&
                                 _equalizerBands.Select((band, index) =>
                                     Math.Abs(band.Gain - preset.BandGains[index]) < 0.001f).All(matches => matches);
        if (stillMatchesPreset)
        {
            return;
        }

        EqualizerPresetCombo.SelectedItem = _equalizerPresets.First(choice => choice.Preset is null);
    }

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

    private void RefreshDiagnostics()
    {
        try
        {
            DiagnosticText.Text = DiagnosticReportFormatter.Format(_diagnosticSnapshot());
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            DiagnosticText.Text = $"Diagnóstico temporariamente indisponível: {exception.Message}";
        }
    }

    private void CopyDiagnosticsButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            RefreshDiagnostics();
            Clipboard.SetText(DiagnosticText.Text);
            DiagnosticCopyStatusText.Text = "Diagnóstico copiado para a área de transferência.";
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            DiagnosticCopyStatusText.Text = $"Não foi possível copiar o diagnóstico: {exception.Message}";
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

    private void SetAppearanceSelection(
        AppearancePreference appearance,
        AccentColorPreference accentColor)
    {
        AppearanceCombo.SelectedItem = _appearanceChoices.First(choice => choice.Preference == appearance);
        BlueAccentRadio.IsChecked = accentColor == AccentColorPreference.GuiaPlayBlue;
        CyanAccentRadio.IsChecked = accentColor == AccentColorPreference.Cyan;
        PurpleAccentRadio.IsChecked = accentColor == AccentColorPreference.Purple;
        GreenAccentRadio.IsChecked = accentColor == AccentColorPreference.Green;
        OrangeAccentRadio.IsChecked = accentColor == AccentColorPreference.Orange;
        PinkAccentRadio.IsChecked = accentColor == AccentColorPreference.Pink;
        AppearanceSelection_OnChanged(this, new RoutedEventArgs());
    }

    private AppearancePreference SelectedAppearance() =>
        AppearanceCombo.SelectedItem is SettingsAppearanceChoice choice
            ? choice.Preference
            : AppearancePreference.System;

    private AccentColorPreference SelectedAccentColor() =>
        CyanAccentRadio.IsChecked == true ? AccentColorPreference.Cyan :
        PurpleAccentRadio.IsChecked == true ? AccentColorPreference.Purple :
        GreenAccentRadio.IsChecked == true ? AccentColorPreference.Green :
        OrangeAccentRadio.IsChecked == true ? AccentColorPreference.Orange :
        PinkAccentRadio.IsChecked == true ? AccentColorPreference.Pink :
        AccentColorPreference.GuiaPlayBlue;

    private void InitializeEqualizer(EqualizerConfiguration saved)
    {
        EqualizerBandsItems.ItemsSource = _equalizerBands;
        EqualizerEnabledCheckBox.IsChecked = saved.Enabled && _equalizerCatalog.IsAvailable;
        if (!_equalizerCatalog.IsAvailable)
        {
            EqualizerAvailabilityText.Text = $"Equalizador indisponível; reprodução original preservada. {_equalizerCatalog.Error}";
            EqualizerEnabledCheckBox.IsEnabled = false;
            EqualizerControlsPanel.IsEnabled = false;
            return;
        }

        EqualizerAvailabilityText.Text =
            $"LibVLC: {_equalizerCatalog.Presets.Count} presets e {_equalizerCatalog.Bands.Count} bandas; alterações salvas são aplicadas em tempo real e nas próximas mídias.";
        var matchingPreset = _equalizerCatalog.Presets.FirstOrDefault(preset =>
            string.Equals(preset.Name, saved.PresetName, StringComparison.OrdinalIgnoreCase));
        var configuration = saved.BandGains.Count == _equalizerCatalog.Bands.Count
            ? EqualizerConfigurationBehavior.Normalize(saved, _equalizerCatalog.Bands.Count)
            : matchingPreset is not null
                ? EqualizerConfigurationBehavior.FromPreset(matchingPreset, saved.Enabled)
                : EqualizerConfigurationBehavior.Normalize(saved, _equalizerCatalog.Bands.Count);

        _loadingEqualizer = true;
        try
        {
            for (var index = 0; index < _equalizerCatalog.Bands.Count; index++)
            {
                _equalizerBands.Add(new EqualizerBandItem(
                    _equalizerCatalog.Bands[index],
                    configuration.BandGains[index]));
            }

            PreampSlider.Value = configuration.Preamp;
            EqualizerPresetCombo.SelectedItem = _equalizerPresets.FirstOrDefault(choice =>
                string.Equals(choice.Label, configuration.PresetName, StringComparison.OrdinalIgnoreCase))
                ?? _equalizerPresets.First(choice => choice.Preset is null);
        }
        finally
        {
            _loadingEqualizer = false;
        }

        EqualizerControlsPanel.IsEnabled = EqualizerEnabledCheckBox.IsChecked == true;
    }

    private EqualizerConfiguration ReadEqualizerConfiguration()
    {
        if (!_equalizerCatalog.IsAvailable)
        {
            return _savedEqualizer;
        }

        var preset = EqualizerPresetCombo.SelectedItem is SettingsEqualizerPresetChoice choice
            ? choice.Label
            : EqualizerConfiguration.CustomPresetName;
        return EqualizerConfigurationBehavior.Normalize(
            new EqualizerConfiguration(
                EqualizerEnabledCheckBox.IsChecked == true,
                preset,
                (float)PreampSlider.Value,
                _equalizerBands.Select(band => band.Gain).ToArray()),
            _equalizerCatalog.Bands.Count);
    }

    private static bool AudioPreferencesEqual(AudioOutputPreference left, AudioOutputPreference right) =>
        left.Mode == right.Mode &&
        string.Equals(left.Module, right.Module, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.DeviceId, right.DeviceId, StringComparison.Ordinal);

    private sealed record SettingsAppearanceChoice(AppearancePreference Preference, string Label);
    private sealed record SettingsAudioChoice(AudioOutputPreference Preference, string Label, bool IsAvailable);
    private sealed record SettingsEqualizerPresetChoice(EqualizerPresetDefinition? Preset, string Label);

    private sealed class EqualizerBandItem(EqualizerBandDefinition band, float gain) : INotifyPropertyChanged
    {
        private float _gain = gain;

        public string FrequencyLabel => band.FrequencyHz >= 1000
            ? $"{band.FrequencyHz / 1000:0.#} kHz"
            : $"{band.FrequencyHz:0.##} Hz";

        public float Gain
        {
            get => _gain;
            set
            {
                var clamped = EqualizerConfigurationBehavior.Clamp(value);
                if (Math.Abs(_gain - clamped) < 0.001f)
                {
                    return;
                }

                _gain = clamped;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Gain)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GainLabel)));
            }
        }

        public string GainLabel => $"{Gain:+0.0;-0.0;0.0} dB";

        public event PropertyChangedEventHandler? PropertyChanged;
    }

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
