using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GuiaPlay.App.Models;
using GuiaPlay.App.Playback;
using GuiaPlay.App.Services;
using GuiaPlay.App.Windows;
using GuiaPlay.Core;
using Microsoft.Win32;

namespace GuiaPlay.App;

public partial class MainWindow : Window
{
    private enum MediaLoadOutcome
    {
        Loaded,
        Cancelled,
        Missing,
        TimedOut,
        Unsupported,
        InvalidPath,
        DecodeFailed
    }

    private const int WmDisplayChange = 0x007E;
    private const string PlaylistDragFormat = "GuiaPlay.PlaylistNode";
    private const string MediaGridDragFormat = "GuiaPlay.MediaGridRow";
    private readonly PlaybackCoordinator _coordinator = new();
    // Loading LibVLC at window construction time starts native services before any
    // playback is requested. Defer native initialization until a local-media action.
    // This also keeps YouTube-only sessions independent of LibVLC.
    private LibVlcPlaybackEngine? _engineInstance;
    private LibVlcPlaybackEngine _engine => _engineInstance ??= CreatePlaybackEngine();
    private readonly ObservableCollection<MonitorChoice> _outputChoices = [];
    private readonly ObservableCollection<PlaylistGroupNode> _playlistGroups = [];
    private readonly ObservableCollection<SavedPlaylistEntry> _savedPlaylists = [];
    private readonly ObservableCollection<MediaGridRow> _mediaRows = [];
    private ICollectionView? _mediaGridView;
    private readonly PlaylistWorkspace _playlistWorkspace;
    private readonly PlaylistPresetStore _playlistPresetStore;
    private readonly PlaylistCatalog _playlistCatalog;
    private readonly Dictionary<string, OutputWindow> _outputWindows = new(StringComparer.OrdinalIgnoreCase);
    private YouTubeOutputWindow? _youtubeWindow;
    private YouTubeVideoReference? _youtubeReference;
    private YouTubePlaybackSnapshot? _youtubeSnapshot;
    private readonly List<IdentifierWindow> _identifierWindows = [];
    private OperatorPreviewWindow? _fullscreenPreviewWindow;
    private readonly DispatcherTimer _monitorTimer;
    private readonly DispatcherTimer _settingsSaveTimer;
    private readonly ReconnectAuthorization _reconnectAuthorization = new();
    private readonly NotificationDebouncer _displayChangeDebouncer = new(MonitorRefreshPolicy.DisplayChangeDebounceInterval);
    private readonly SessionDiagnostics _diagnostics = new();
    private readonly ProcessMetricsSampler _processMetrics = new();
    private readonly HashSet<string> _selectedOutputIdsThisSession = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _explicitOutputIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action _renderFrameCallback;
    private IReadOnlyList<MonitorInfo> _monitors = [];
    private WriteableBitmap? _bitmap;
    private long _frameGeneration;
    private int _renderPending;
    private bool _isDraggingProgress;
    private Slider? _draggingProgressSlider;
    private bool _automaticAdvanceInProgress;
    private bool _suppressAutoAdvance;
    private Guid? _loadedPlaylistItemId;
    private long _loadedPlaylistRevision;
    private long _mediaLoadRequestId;
    private PlaylistPlaybackMode _playbackMode = PlaylistPlaybackMode.Manual;
    private PlaylistRepeatMode _repeatMode = PlaylistRepeatMode.Off;
    private bool _shufflePlayback;
    private readonly HashSet<Guid> _shufflePlayedItems = [];
    private bool _isClosing;
    private bool _allowClose;
    private long _frameCallbacks;
    private long _framesRendered;
    private long _copyTicks;
    private bool _outputsAwaitingFirstFrame;
    private bool _settingsInitialized;
    private bool _startupMonitorPreferencesApplied;
    private bool _updatingMonitorChoices;
    private bool _controlsBusy;
    private bool _licensingAllowsAccess;
    private bool _operatorRequiresConfiguration;
    private bool _operatorRecoverySettingsAllowed;
    private bool _audioPreferenceAvailable = true;
    private bool _audioReconfigurationRequired;
    private bool _audioInventoryInitialized;
    private string? _audioDeviceSignature;
    private IReadOnlyList<AudioDeviceIdentity> _audioDevices = [];
    private MonitorInfo? _operatorMonitor;
    private CancellationTokenSource? _identifierCancellation;
    private CancellationTokenSource? _playlistAvailabilityCancellation;
    private Point _playlistDragStart;
    private object? _playlistDragSource;
    private CancellationTokenSource? _folderImportCancellation;
    private long _playlistRevision;
    private bool _folderImportActive;
    private Point _mediaGridDragStart;
    private MediaGridRow? _mediaGridDragSource;
    private bool _autoInstallInProgress;

    public MainWindow()
    {
        InitializeComponent();
        Title = $"GuiaPlay {ProductInfo.Version}";
        VersionHeaderText.Text = $"v{ProductInfo.Version}";
        _renderFrameCallback = RenderLatestFrame;
        OutputsList.ItemsSource = _outputChoices;
        SidebarOutputsList.ItemsSource = _outputChoices;
        PlaylistTree.ItemsSource = _playlistGroups;
        SavedPlaylistsList.ItemsSource = _savedPlaylists;
        _mediaGridView = CollectionViewSource.GetDefaultView(_mediaRows);
        _mediaGridView.Filter = MediaGridFilter;
        MediaGrid.ItemsSource = _mediaGridView;
        var app = Application.Current as App;
        var settings = app?.Settings ?? AppSettings.Default;
        _playlistWorkspace = new PlaylistWorkspace(app?.Playlist ?? PlaylistDocument.Empty);
        _playlistCatalog = _playlistWorkspace.Catalog;
        _playlistCatalog.Changed += (_, _) =>
        {
            _playlistRevision++;
            _shufflePlayedItems.Clear();
        };
        var presetPathOverride = Environment.GetEnvironmentVariable("GUIAPLAY_PLAYLIST_PRESETS_PATH");
        _playlistPresetStore = new PlaylistPresetStore(
            string.IsNullOrWhiteSpace(presetPathOverride)
                ? PlaylistPresetStore.DefaultDirectoryPath
                : Path.GetFullPath(presetPathOverride));
        RebuildPlaylistTree();
        RefreshSavedPlaylists();
        foreach (var id in settings.SelectedOutputIds)
        {
            _explicitOutputIds.Add(id);
        }

        VolumeSlider.Value = settings.Volume;
        MuteButton.Tag = settings.Muted;
        UpdateMuteVisual(settings.Muted);
        VolumeText.Text = settings.Muted ? "mudo" : $"{settings.Volume}%";
        FileLogger.Info("GP-I11: Janela iniciada sem carregar o motor LibVLC antecipadamente.");

        _monitorTimer = new DispatcherTimer(MonitorRefreshPolicy.SafetyPollInterval, DispatcherPriority.Background, (_, _) =>
        {
            DevelopmentDiagnostics.Trace("monitor safety poll start");
            RefreshConnectedDevices(false);
            DevelopmentDiagnostics.Trace("monitor safety poll end");
        }, Dispatcher);
        _settingsSaveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(650), DispatcherPriority.Background, (_, _) => FlushDeferredSettings(), Dispatcher)
        {
            IsEnabled = false
        };
        _settingsInitialized = true;
        PlaybackModeCombo.SelectedIndex = 0;
        RefreshPlaylistTransport();
        if (app?.UpdateManager is { } updateManager)
        {
            updateManager.StateChanged += UpdateManager_OnStateChanged;
        }

        if (app?.Licensing is { } licensing)
        {
            licensing.StateChanged += LicensingManager_OnStateChanged;
        }
        Loaded += (_, _) =>
        {
            FitToWorkArea();
            DevelopmentDiagnostics.Trace("initial monitor + audio refresh begins");
            RefreshConnectedDevices(false);
            DevelopmentDiagnostics.Trace("initial monitor + audio refresh finished");
            if (_operatorMonitor is not null)
            {
                WindowPlacement.PlaceOperatorPanel(this, _operatorMonitor);
            }

            if (!DevelopmentDiagnostics.SkipMonitorPolling)
            {
                _monitorTimer.Start();
            }
            else
            {
                DevelopmentDiagnostics.Trace("periodic monitor refresh isolated in Debug.");
            }

            if ((Application.Current as App)?.StartupSettingsWarning is { } warning)
            {
                StatusText.Text = "Configurações recuperadas com valores seguros; consulte o log.";
                FileLogger.Info(warning);
            }

            if ((Application.Current as App)?.StartupPlaylistWarning is { } playlistWarning)
            {
                StatusText.Text = "A playlist não pôde ser lida; uma lista vazia foi aberta.";
                FileLogger.Info(playlistWarning);
            }

            FileLogger.Info("Painel do operador carregado.");
            if (_playlistAvailabilityCancellation is { } mediaCancellation)
            {
                _ = RefreshMediaGridMetadataAsync(_mediaRows.ToArray(), mediaCancellation.Token);
            }
            RefreshUpdateIndicator();
            ApplyLicensingState((Application.Current as App)?.Licensing?.Current ?? LicensingSnapshot.Checking);
            if (!DevelopmentDiagnostics.SkipStartupUpdate)
            {
                _ = CheckForUpdatesOnStartupAsync();
            }
            else
            {
                DevelopmentDiagnostics.Trace("startup GitHub updater isolated in Debug.");
            }
        };
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessageHook);
        Closing += MainWindow_OnClosing;
    }

    private LibVlcPlaybackEngine CreatePlaybackEngine()
    {
        var settings = (Application.Current as App)?.Settings ?? AppSettings.Default;
        FileLogger.Info("GP-I11: Inicializando LibVLC sob demanda.");
        var engine = new LibVlcPlaybackEngine(settings.Equalizer);
        engine.FrameReady += Engine_OnFrameReady;
        engine.Playing += generation => Dispatch(() => Engine_OnPlaying(generation));
        engine.Paused += generation => Dispatch(() => Engine_OnPaused(generation));
        engine.EndReached += generation => Dispatch(() => Engine_OnEndReached(generation));
        engine.Error += (generation, message) => Dispatch(() => Engine_OnError(generation, message));
        engine.TimeChanged += (generation, time) => Dispatch(() => UpdateTime(generation, time));
        engine.LengthChanged += (generation, length) => Dispatch(() => UpdateLength(generation, length));
        engine.AudioDeviceChanged += (generation, deviceId) => Dispatch(() => Engine_OnAudioDeviceChanged(generation, deviceId));
        engine.EqualizerFailed += message => FileLogger.Error($"Falha ao aplicar equalizador; reprodução original preservada: {message}");
        FileLogger.Info("GP-I11: LibVLC inicializado e callbacks associados.");
        return engine;
    }

    private void FitToWorkArea()
    {
        var workArea = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(Width, workArea.Width - 24));
        Height = Math.Max(MinHeight, Math.Min(Height, workArea.Height - 24));
    }

    internal async Task HandleExternalMediaAsync(string path)
    {
        BringToFront();
        if (!_licensingAllowsAccess)
        {
            StatusText.Text = "Ative o GuiaPlay ou valide a avaliação gratuita antes de abrir uma mídia.";
            return;
        }

        await LoadMediaAsync(path, playImmediately: false);
    }

    internal void ShowExternalRequestStatus(string message)
    {
        BringToFront();
        StatusText.Text = message;
    }

    internal void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        _ = Activate();
    }

    private async void OpenButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Abrir mídia",
            Filter = "Mídia compatível|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.webm;*.m4v;*.mpg;*.mpeg;*.ts;*.m2ts;*.3gp;*.ogv;*.mp3;*.wav;*.ogg;*.flac;*.aac;*.m4a;*.wma;*.opus;*.aiff;*.alac|Vídeo|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.webm;*.m4v;*.mpg;*.mpeg;*.ts;*.m2ts;*.3gp;*.ogv|Áudio|*.mp3;*.wav;*.ogg;*.flac;*.aac;*.m4a;*.wma;*.opus;*.aiff;*.alac",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await LoadMediaAsync(dialog.FileName, playImmediately: false);
    }

    private async Task<MediaLoadOutcome> LoadMediaAsync(
        string path,
        bool playImmediately,
        MediaProbeStatus? verifiedProbe = null,
        Guid? playlistItemId = null,
        bool fromAutomaticSequence = false)
    {
        var requestId = ++_mediaLoadRequestId;
        if (!_licensingAllowsAccess)
        {
            StatusText.Text = "Ative o GuiaPlay ou valide a avaliação gratuita antes de abrir uma mídia.";
            return MediaLoadOutcome.Cancelled;
        }

        var kind = MediaTypeDetector.Detect(path);
        if (kind == MediaKind.Unknown)
        {
            StatusText.Text = "Formato de mídia não reconhecido.";
            return MediaLoadOutcome.Unsupported;
        }

        StatusText.Text = "Verificando disponibilidade da mídia…";
        var probe = verifiedProbe ?? await MediaFileProbe.ProbeAsync(path, TimeSpan.FromSeconds(4));
        if (requestId != _mediaLoadRequestId || _isClosing || !_licensingAllowsAccess ||
            (fromAutomaticSequence &&
                (_playbackMode != PlaylistPlaybackMode.Sequential ||
                 _playlistRevision != _loadedPlaylistRevision)))
        {
            return MediaLoadOutcome.Cancelled;
        }

        if (probe != MediaProbeStatus.Available)
        {
            StatusText.Text = probe switch
            {
                MediaProbeStatus.TimedOut => "A mídia ou unidade de rede não respondeu no tempo esperado.",
                MediaProbeStatus.InvalidPath => "O caminho da mídia é inválido.",
                MediaProbeStatus.Unsupported => "Formato de mídia não reconhecido.",
                _ => "Arquivo não encontrado; a referência permanece na playlist."
            };
            return probe switch
            {
                MediaProbeStatus.TimedOut => MediaLoadOutcome.TimedOut,
                MediaProbeStatus.InvalidPath => MediaLoadOutcome.InvalidPath,
                MediaProbeStatus.Unsupported => MediaLoadOutcome.Unsupported,
                _ => MediaLoadOutcome.Missing
            };
        }

        var replacementConfirmed = true;
        if (_coordinator.IsActive || _youtubeWindow is not null)
        {
            replacementConfirmed = MessageBox.Show(
                this,
                "Há uma mídia em reprodução ou pausada. Deseja interrompê-la e carregar a nova mídia?",
                "Confirmar troca de mídia",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }

        if (_coordinator.TryLoad(path, kind, replacementConfirmed) == LoadDecision.Cancelled)
        {
            StatusText.Text = "Troca cancelada; a reprodução atual foi preservada.";
            return MediaLoadOutcome.Cancelled;
        }

        CloseYouTubeWindow();
        _loadedPlaylistItemId = null;
        _audioReconfigurationRequired = false;
        try
        {
            SetControlsBusy(true);
            CloseOutputWindows();
            StatusText.Text = "Carregando mídia…";
            // Creating the native engine is now part of the controlled media-load
            // operation, not the initial WPF window / display discovery path.
            _ = _engine;
            RefreshAudioDevices(force: true);
            var audioOutput = (Application.Current as App)?.Settings.AudioOutput ?? AudioOutputPreference.Default;
            await _engine.LoadAsync(path, kind, _coordinator.Generation, audioOutput);
            _engine.Volume = (int)VolumeSlider.Value;
            _engine.Muted = MuteButton.Tag as bool? ?? false;
            _loadedPlaylistItemId = playlistItemId;
            _loadedPlaylistRevision = _playlistRevision;
            if (!_automaticAdvanceInProgress)
            {
                _shufflePlayedItems.Clear();
            }

            if (playlistItemId is { } playedId)
            {
                _shufflePlayedItems.Add(playedId);
            }
            _bitmap = null;
            PreviewImage.Source = null;
            ProgramImage.Source = null;
            PreviewPlaceholder.Text = kind == MediaKind.Audio ? "Áudio carregado" : "Prévia de vídeo";
            PreviewEmptyHint.Text = kind == MediaKind.Audio
                ? "A reprodução de áudio não exibe imagem na prévia"
                : "Aguardando o primeiro quadro da mídia";
            PreviewEmptyState.Visibility = Visibility.Visible;
            FileNameText.Text = Path.GetFileName(path);
            UpdateTransportArtwork(kind, path);
            FileNameText.ToolTip = Path.GetFileName(path);
            ProgressSlider.Value = 0;
            ProgressSlider.Maximum = 1;
            UpdateTimeLabels(0, 0);
            RefreshPlaylistTransport();
            StatusText.Text = kind == MediaKind.Audio
                ? "Áudio carregado; pronto para reproduzir."
                : _outputChoices.Any(choice => choice.IsSelected)
                    ? "Vídeo carregado; pronto para reproduzir."
                    : "Vídeo carregado; selecione ao menos uma saída.";
            _diagnostics.RecordMediaChanged();
            LogDiagnosticSnapshot("Mídia carregada");
            RefreshPlaybackControls();
        }
        catch (Exception exception)
        {
            _coordinator.PlaybackFailed(_coordinator.Generation);
            FileLogger.Error($"Falha ao carregar '{Path.GetFileName(path)}'; geração {_coordinator.Generation}; tipo {kind}.", exception);
            StatusText.Text = $"O arquivo existe, mas não pôde ser decodificado: {Path.GetFileName(path)}.";
            return MediaLoadOutcome.DecodeFailed;
        }
        finally
        {
            SetControlsBusy(false);
        }

        if (playImmediately && _licensingAllowsAccess &&
            _coordinator.CanPlay(SelectedOutputCount()) &&
            (!fromAutomaticSequence ||
                (_playbackMode == PlaylistPlaybackMode.Sequential &&
                 requestId == _mediaLoadRequestId && !_isClosing)))
        {
            await StartOrResumePlaybackAsync();
        }

        return MediaLoadOutcome.Loaded;
    }

    private void NewGroupButton_OnClick(object sender, RoutedEventArgs e)
    {
        var prompt = new TextPromptWindow("Novo grupo", "Nome do grupo virtual:") { Owner = this };
        if (prompt.ShowDialog() != true)
        {
            return;
        }

        _playlistCatalog.CreateGroup(prompt.Value);
        SavePlaylistAndRefresh("Grupo criado.");
    }

    private void RenameGroupButton_OnClick(object sender, RoutedEventArgs e)
    {
        var group = SelectedPlaylistGroup();
        if (group is null)
        {
            StatusText.Text = "Selecione um grupo para renomear.";
            return;
        }

        var prompt = new TextPromptWindow("Renomear grupo", "Novo nome do grupo:", group.Name) { Owner = this };
        if (prompt.ShowDialog() == true && _playlistCatalog.RenameGroup(group.Id, prompt.Value))
        {
            SavePlaylistAndRefresh("Grupo renomeado.");
        }
    }

    private void DeleteGroupButton_OnClick(object sender, RoutedEventArgs e)
    {
        var group = SelectedPlaylistGroup();
        if (group is null)
        {
            StatusText.Text = "Selecione um grupo para excluir.";
            return;
        }

        if (MessageBox.Show(
                this,
                $"Excluir o grupo virtual “{group.Name}” e suas referências? Os arquivos originais não serão alterados.",
                "Excluir grupo",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes &&
            _playlistCatalog.RemoveGroup(group.Id))
        {
            SavePlaylistAndRefresh("Grupo removido; nenhum arquivo original foi alterado.");
        }
    }

    private void SavePlaylistPresetButton_OnClick(object sender, RoutedEventArgs e) =>
        TrySavePlaylistPreset();

    private void NewPlaylistButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_licensingAllowsAccess || _controlsBusy)
        {
            return;
        }

        if (_playlistCatalog.Snapshot.Groups.Count > 0 || _playlistWorkspace.ActivePresetId is not null)
        {
            var createNew = MessageBox.Show(
                this,
                "Iniciar uma nova playlist vazia? A lista de trabalho será substituída, mas as playlists já salvas e os arquivos originais não serão apagados.",
                "Nova playlist",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (createNew != MessageBoxResult.Yes)
            {
                return;
            }
        }

        var replacementMode = ConfirmWorkspaceReplacement();
        if (replacementMode is null)
        {
            return;
        }

        if (Application.Current is not App app || app.PlaylistStore is null)
        {
            StatusText.Text = "O armazenamento da playlist ainda não está disponível.";
            return;
        }

        var persisted = app.PlaylistStore.Save(PlaylistDocument.Empty);
        if (!persisted.Succeeded)
        {
            FileLogger.Error($"Falha ao iniciar nova playlist: {persisted.Error}");
            MessageBox.Show(
                this,
                "A nova playlist não foi criada porque a lista de trabalho não pôde ser persistida.",
                "GuiaPlay — nova playlist",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        if (!_playlistWorkspace.StartNew(replacementMode.Value).Succeeded)
        {
            StatusText.Text = "Alterações não salvas foram preservadas; nenhuma playlist foi substituída.";
            return;
        }

        app.Playlist = _playlistCatalog.Snapshot;
        RebuildPlaylistTree();
        RefreshSavedPlaylists();
        StatusText.Text = "Nova playlist vazia criada. As mídias e playlists salvas foram preservadas.";
    }

    private SavedPlaylistEntry? SelectedSavedPlaylist() =>
        SavedPlaylistsList.SelectedItem as SavedPlaylistEntry;

    private void SavedPlaylistsList_OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RefreshSavedPlaylistActions();

    private void SavedPlaylistsList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject origin &&
            ItemsControl.ContainerFromElement(SavedPlaylistsList, origin) is ListBoxItem)
        {
            OpenSelectedPresetButton_OnClick(sender, e);
        }
    }

    private void OpenSelectedPresetButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_licensingAllowsAccess || _controlsBusy || SelectedSavedPlaylist() is not { } entry)
        {
            return;
        }

        TryLoadPlaylistPreset(entry.Id);
    }

    private void DeleteSelectedPresetButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_licensingAllowsAccess || _controlsBusy || SelectedSavedPlaylist() is not { } entry)
        {
            return;
        }

        if (MessageBox.Show(
                this,
                $"Excluir a playlist salva “{entry.Name}”?\n\nSomente o preset JSON será excluído. A lista de trabalho e os arquivos originais serão preservados.",
                "Excluir playlist salva",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var result = _playlistPresetStore.Delete(entry.Id);
        if (!result.Succeeded)
        {
            FileLogger.Error($"Falha ao excluir preset {entry.Id:D}: {result.Error}");
            MessageBox.Show(
                this,
                result.Error ?? "Não foi possível excluir a playlist selecionada.",
                "GuiaPlay — excluir playlist",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            RefreshSavedPlaylists();
            return;
        }

        _playlistWorkspace.DetachPreset(entry.Id);
        FileLogger.Info($"Preset excluído da biblioteca: {entry.Name} ({entry.Id:D}).");
        RefreshSavedPlaylists();
        StatusText.Text = $"Playlist salva “{entry.Name}” excluída; mídias originais preservadas.";
    }

    private void LoadPlaylistPresetButton_OnClick(object sender, RoutedEventArgs e)
    {
        var listResult = _playlistPresetStore.LoadAll();
        foreach (var warning in listResult.Warnings)
        {
            FileLogger.Info(warning);
        }

        if (listResult.Presets.Count == 0)
        {
            MessageBox.Show(
                this,
                listResult.Warnings.Count == 0
                    ? "Ainda não há playlists salvas. Use o botão Salvar playlist para criar a primeira."
                    : "Nenhuma playlist válida pôde ser carregada. Consulte o log para ver os arquivos ignorados.",
                "GuiaPlay — playlists salvas",
                MessageBoxButton.OK,
                listResult.Warnings.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            RefreshSavedPlaylists();
            return;
        }

        var picker = new PlaylistPresetPickerWindow(
            listResult.Presets,
            listResult.Warnings.Count,
            preset =>
            {
                var result = _playlistPresetStore.Delete(preset.Id);
                if (result.Succeeded)
                {
                    _playlistWorkspace.DetachPreset(preset.Id);
                    FileLogger.Info($"Preset de playlist excluído: {preset.Name} ({preset.Id:D}).");
                }
                else
                {
                    FileLogger.Error($"Falha ao excluir preset de playlist {preset.Id:D}: {result.Error}");
                }

                return result;
            })
        {
            Owner = this
        };
        var selected = picker.ShowDialog() == true ? picker.SelectedPreset : null;
        RefreshSavedPlaylists();
        if (selected is not null)
        {
            TryLoadPlaylistPreset(selected.Id);
        }
    }

    private PlaylistReplacementMode? ConfirmWorkspaceReplacement()
    {
        if (!_playlistWorkspace.IsDirty)
        {
            return PlaylistReplacementMode.RequireCleanWorkspace;
        }

        var decision = MessageBox.Show(
            this,
            "A playlist de trabalho contém alterações que não foram salvas em um preset.\n\n" +
            "Sim — Salvar antes de continuar\nNão — Continuar sem salvar as alterações\nCancelar — manter a playlist atual",
            "Alterações não salvas",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        return decision switch
        {
            MessageBoxResult.Yes => TrySavePlaylistPreset()
                ? PlaylistReplacementMode.RequireCleanWorkspace
                : null,
            MessageBoxResult.No => PlaylistReplacementMode.DiscardUnsavedChanges,
            _ => null
        };
    }

    private bool TryLoadPlaylistPreset(Guid presetId)
    {
        var loaded = _playlistPresetStore.Load(presetId);
        if (loaded.Preset is not { } preset)
        {
            FileLogger.Error($"A playlist salva {presetId:D} não pôde ser carregada: {loaded.Error}");
            MessageBox.Show(
                this,
                loaded.Error ?? "A playlist salva não foi encontrada.",
                "GuiaPlay — abrir playlist",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            RefreshSavedPlaylists();
            return false;
        }

        if (!string.IsNullOrWhiteSpace(loaded.Error))
        {
            FileLogger.Info($"A playlist salva foi lida parcialmente: {loaded.Error}");
        }

        var replacementMode = ConfirmWorkspaceReplacement();
        if (replacementMode is null)
        {
            return false;
        }

        // A Save confirmation can overwrite the very preset being opened.
        // Re-read after that decision so the current on-disk snapshot wins.
        loaded = _playlistPresetStore.Load(presetId);
        if (loaded.Preset is not { } latestPreset)
        {
            FileLogger.Error($"A playlist salva {presetId:D} mudou durante a confirmação: {loaded.Error}");
            RefreshSavedPlaylists();
            StatusText.Text = "A playlist salva mudou ou desapareceu; a lista de trabalho foi preservada.";
            return false;
        }

        preset = latestPreset;

        if (Application.Current is not App app || app.PlaylistStore is null)
        {
            StatusText.Text = "O armazenamento da playlist ainda não está disponível.";
            return false;
        }

        // Persist the selected snapshot before swapping the live in-memory workspace.
        var persisted = app.PlaylistStore.Save(preset.Playlist);
        if (!persisted.Succeeded)
        {
            FileLogger.Error($"Falha ao persistir playlist carregada do preset {preset.Id:D}: {persisted.Error}");
            MessageBox.Show(
                this,
                "A playlist salva não foi carregada porque a lista de trabalho não pôde ser persistida.",
                "GuiaPlay — abrir playlist",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }

        if (!_playlistWorkspace.LoadPreset(preset, replacementMode.Value).Succeeded)
        {
            StatusText.Text = "A playlist atual tem alterações não salvas e foi preservada.";
            return false;
        }

        app.Playlist = _playlistCatalog.Snapshot;
        RebuildPlaylistTree();
        RefreshSavedPlaylists(preset.Id);
        StatusText.Text = $"Playlist “{preset.Name}” aberta sem iniciar reprodução.";
        FileLogger.Info($"Preset carregado pela biblioteca: {preset.Name} ({preset.Id:D}).");
        return true;
    }

    private void RefreshSavedPlaylists(Guid? preferredId = null)
    {
        var selectedId = preferredId ?? SelectedSavedPlaylist()?.Id ?? _playlistWorkspace.ActivePresetId;
        var result = _playlistPresetStore.LoadAll();
        foreach (var warning in result.Warnings)
        {
            FileLogger.Info(warning);
        }

        _savedPlaylists.Clear();
        foreach (var preset in result.Presets)
        {
            var active = _playlistWorkspace.ActivePresetId == preset.Id;
            _savedPlaylists.Add(new SavedPlaylistEntry(preset, active, active && _playlistWorkspace.IsDirty));
        }

        SavedPlaylistsList.SelectedItem = _savedPlaylists.FirstOrDefault(item => item.Id == selectedId);
        SavedPlaylistsCountText.Text = _savedPlaylists.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
        SavedPlaylistsEmptyText.Text = result.Warnings.Count > 0
            ? "Algumas playlists não puderam ser lidas. Consulte o log."
            : "Nenhuma playlist salva. Use Salvar para criar uma.";
        SavedPlaylistsEmptyText.Visibility = _savedPlaylists.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshSavedPlaylistActions();
    }

    private void RefreshSavedPlaylistActions()
    {
        var canManage = _licensingAllowsAccess && !_controlsBusy && SelectedSavedPlaylist() is not null;
        OpenSelectedPresetButton.IsEnabled = canManage;
        DeleteSelectedPresetButton.IsEnabled = canManage;
    }

    private void RefreshActivePlaylistMarkers()
    {
        var selectedId = SelectedSavedPlaylist()?.Id;
        for (var index = 0; index < _savedPlaylists.Count; index++)
        {
            var preset = _savedPlaylists[index].Preset;
            var active = _playlistWorkspace.ActivePresetId == preset.Id;
            _savedPlaylists[index] = new SavedPlaylistEntry(preset, active, active && _playlistWorkspace.IsDirty);
        }

        if (selectedId is { } id)
        {
            SavedPlaylistsList.SelectedItem = _savedPlaylists.FirstOrDefault(entry => entry.Id == id);
        }

        RefreshSavedPlaylistActions();
    }

    private bool TrySavePlaylistPreset()
    {
        var prompt = new TextPromptWindow(
            "Salvar playlist",
            "Nome da playlist:",
            _playlistWorkspace.ActivePresetName ?? string.Empty,
            "Salvar")
        {
            Owner = this
        };
        if (prompt.ShowDialog() != true)
        {
            return false;
        }

        var snapshot = _playlistCatalog.Snapshot;
        var result = _playlistPresetStore.Save(prompt.Value, snapshot);
        if (result.Status == PlaylistPresetSaveStatus.NameConflict)
        {
            var overwrite = MessageBox.Show(
                this,
                "Já existe uma playlist com esse nome.\nDeseja substituir?",
                "Substituir playlist salva",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (overwrite != MessageBoxResult.Yes)
            {
                StatusText.Text = "A playlist salva existente foi preservada.";
                return false;
            }

            result = _playlistPresetStore.Save(prompt.Value, snapshot, overwriteExisting: true);
        }

        if (result.Preset is not { } savedPreset)
        {
            FileLogger.Error($"Falha ao salvar preset de playlist: {result.Error}");
            MessageBox.Show(
                this,
                result.Error ?? "Não foi possível salvar a playlist.",
                "GuiaPlay — salvar playlist",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }

        _playlistWorkspace.AcceptSavedPreset(savedPreset);
        RefreshSavedPlaylists(savedPreset.Id);
        StatusText.Text = $"Playlist “{savedPreset.Name}” salva.";
        FileLogger.Info($"Preset de playlist salvo: {savedPreset.Name} ({savedPreset.Id:D}).");
        return true;
    }

    private void AddMediaMenuButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_licensingAllowsAccess || _controlsBusy || _folderImportActive)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = AddMediaMenuButton, Placement = PlacementMode.Bottom };
        // 'Abrir mídia' lives in Adicionar, not in the transport footer.
        var openDirect = new MenuItem { Header = "Abrir mídia para reprodução…" };
        openDirect.Click += OpenButton_OnClick;
        menu.Items.Add(openDirect);
        menu.Items.Add(new Separator());
        var addFiles = new MenuItem { Header = "Adicionar arquivo(s)…" };
        addFiles.Click += AddPlaylistItemButton_OnClick;
        var addFolder = new MenuItem { Header = "Adicionar pasta e subpastas…" };
        addFolder.Click += AddFolderButton_OnClick;
        menu.Items.Add(addFiles);
        menu.Items.Add(addFolder);
        var addYouTube = new MenuItem { Header = "Adicionar link do YouTube…" };
        addYouTube.Click += AddYouTubeLinkButton_OnClick;
        menu.Items.Add(addYouTube);
        menu.IsOpen = true;
    }

    // Playback modes are moved to toolbar options; the ComboBox remains
    // the single state entry point so existing safety rules are unchanged.
    private void PlaylistOptionsMenuButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_licensingAllowsAccess || _controlsBusy)
        {
            return;
        }

        var menu = new ContextMenu
        {
            PlacementTarget = PlaylistOptionsMenuButton,
            Placement = PlacementMode.Bottom
        };
        var manual = new MenuItem
        {
            Header = "Modo manual (sem avançar automaticamente)",
            IsCheckable = true,
            IsChecked = PlaybackModeCombo.SelectedIndex != 1
        };
        manual.Click += (_, _) => PlaybackModeCombo.SelectedIndex = 0;
        var sequential = new MenuItem
        {
            Header = "Modo sequencial (avançar ao terminar)",
            IsCheckable = true,
            IsChecked = PlaybackModeCombo.SelectedIndex == 1
        };
        sequential.Click += (_, _) => PlaybackModeCombo.SelectedIndex = 1;
        menu.Items.Add(manual);
        menu.Items.Add(sequential);

        if (!string.IsNullOrWhiteSpace(MediaSearchBox.Text))
        {
            menu.Items.Add(new Separator());
            var clear = new MenuItem { Header = "Limpar pesquisa" };
            clear.Click += ClearMediaSearchButton_OnClick;
            menu.Items.Add(clear);
        }

        menu.IsOpen = true;
    }

    private async void AddPlaylistItemButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_licensingAllowsAccess || _controlsBusy || _folderImportActive)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Adicionar referências à playlist",
            Filter = "Mídia compatível|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.webm;*.m4v;*.mpg;*.mpeg;*.ts;*.m2ts;*.3gp;*.ogv;*.mp3;*.wav;*.ogg;*.flac;*.aac;*.m4a;*.wma;*.opus;*.aiff;*.alac",
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            await ImportDroppedFilesAsync(dialog.FileNames, SelectedPlaylistGroup());
        }
    }

    private async void AddFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_licensingAllowsAccess || _controlsBusy || _folderImportActive)
        {
            return;
        }

        var picker = new OpenFolderDialog
        {
            Title = "Selecionar pasta de vídeos e músicas",
            Multiselect = false
        };
        if (picker.ShowDialog(this) != true)
        {
            return;
        }

        await ImportFolderAsync(picker.FolderName);
    }

    private void AddYouTubeLinkButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_licensingAllowsAccess || _controlsBusy || _folderImportActive)
        {
            return;
        }

        var dialog = new YouTubeLinkPromptWindow { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (Application.Current is not App app || app.PlaylistStore is null)
        {
            StatusText.Text = "O armazenamento da playlist ainda não está disponível.";
            return;
        }

        var catalog = new PlaylistCatalog(_playlistCatalog.Snapshot);
        var groupId = SelectedPlaylistGroup()?.Id ??
            catalog.Snapshot.Groups.FirstOrDefault()?.Id ??
            catalog.CreateGroup("YouTube").Id;
        try
        {
            catalog.AddYouTubeItem(groupId, dialog.Reference, dialog.DisplayName);
        }
        catch (ArgumentException exception)
        {
            StatusText.Text = exception.Message;
            return;
        }

        var saved = app.PlaylistStore.Save(catalog.Snapshot);
        if (!saved.Succeeded)
        {
            StatusText.Text = "Não foi possível salvar o link na playlist.";
            FileLogger.Error($"Falha ao salvar link YouTube: {saved.Error}");
            return;
        }

        _playlistCatalog.ApplyPersistedChanges(catalog.Snapshot);
        app.Playlist = _playlistCatalog.Snapshot;
        RebuildPlaylistTree();
        RefreshActivePlaylistMarkers();
        StatusText.Text = "Link YouTube salvo. Para reproduzir, selecione somente uma tela pública.";
    }

    private void CancelFolderImportButton_OnClick(object sender, RoutedEventArgs e) =>
        _folderImportCancellation?.Cancel();

    private async Task ImportFolderAsync(string folder)
    {
        if (_folderImportActive || !_licensingAllowsAccess || _controlsBusy)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _folderImportCancellation = cancellation;
        _folderImportActive = true;
        var revisionAtStart = _playlistRevision;
        CancelFolderImportButton.Visibility = Visibility.Visible;
        FolderImportStatusText.Visibility = Visibility.Visible;
        FolderImportStatusText.Text = "Lendo pasta e subpastas…";
        StatusText.Text = "Procurando vídeos e músicas na pasta selecionada…";
        // Folder inspection runs in the background; do not disable live transport controls.

        try
        {
            var scanned = await FolderMediaScanner.ScanAsync(folder, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!_licensingAllowsAccess || _isClosing || revisionAtStart != _playlistRevision)
            {
                StatusText.Text = "A lista mudou ou o acesso foi revogado; importação descartada.";
                return;
            }

            if (scanned.Candidates.Count == 0)
            {
                FolderImportStatusText.Text = "Nenhuma mídia compatível encontrada";
                StatusText.Text = "Nenhuma mídia compatível foi encontrada; a playlist não foi modificada.";
                return;
            }

            var partial = scanned.Limited || scanned.Warnings.Count > 0 || scanned.SkippedUnsafe > 0;
            if (partial)
            {
                var decision = MessageBox.Show(
                    this,
                    $"A varredura encontrou {scanned.Candidates.Count} mídia(s), mas nem todos os caminhos foram lidos. " +
                    $"Pastas verificadas: {scanned.DirectoriesVisited}. Entradas ignoradas por proteção/erro: {scanned.SkippedUnsafe}. " +
                    $"Limite alcançado: {(scanned.Limited ? "sim" : "não")}.\n\n" +
                    "Deseja importar somente os arquivos encontrados?",
                    "Importação parcial de pasta",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (decision != MessageBoxResult.Yes)
                {
                    StatusText.Text = "Importação parcial cancelada; playlist preservada.";
                    return;
                }
            }

            cancellation.Token.ThrowIfCancellationRequested();
            var merged = FolderMediaPlaylistMerge.Merge(_playlistCatalog.Snapshot, scanned);
            if (merged.Added == 0)
            {
                FolderImportStatusText.Text = "Todas as mídias já estavam na lista";
                StatusText.Text = $"{merged.SkippedDuplicates} arquivo(s) duplicado(s) ignorado(s).";
                return;
            }

            if (revisionAtStart != _playlistRevision || !_licensingAllowsAccess || _isClosing)
            {
                StatusText.Text = "A lista mudou antes de salvar; importação descartada.";
                return;
            }

            if (Application.Current is not App app || app.PlaylistStore is null)
            {
                StatusText.Text = "O armazenamento da playlist não está disponível.";
                return;
            }

            // Commit on disk first; the current playlist stays intact if persistence fails.
            var persisted = app.PlaylistStore.Save(merged.Playlist);
            if (!persisted.Succeeded)
            {
                FileLogger.Error($"Importação de pasta não persistida: {persisted.Error}");
                StatusText.Text = "Erro ao salvar as mídias importadas; lista anterior preservada.";
                return;
            }

            _playlistCatalog.ApplyPersistedChanges(merged.Playlist);
            app.Playlist = _playlistCatalog.Snapshot;
            RebuildPlaylistTree();
            RefreshActivePlaylistMarkers();

            FolderImportStatusText.Text = $"{merged.Added} mídia(s) importada(s)";
            StatusText.Text = $"Pasta importada: {merged.Added} mídia(s), {merged.GroupsCreated} grupo(s) novo(s), " +
                              $"{merged.SkippedDuplicates} duplicada(s), {scanned.SkippedUnsupported} incompatível(is).";
            FileLogger.Info($"Importação de pasta: {folder}; {StatusText.Text}");
            foreach (var warning in scanned.Warnings)
            {
                FileLogger.Info(warning);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            FolderImportStatusText.Text = "Importação cancelada";
            StatusText.Text = "Importação cancelada antes de salvar; playlist preservada.";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            FileLogger.Error("Falha ao importar pasta de mídias.", exception);
            FolderImportStatusText.Text = "Importação não concluída";
            StatusText.Text = "Não foi possível ler a pasta; playlist preservada. Consulte o log.";
        }
        finally
        {
            _folderImportActive = false;
            _folderImportCancellation = null;
            CancelFolderImportButton.Visibility = Visibility.Collapsed;
            // Playback and monitor controls remain usable during folder inspection.
        }
    }

    private void RemovePlaylistItemButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (PlaylistTree.SelectedItem is not PlaylistItemNode item ||
            !_playlistCatalog.RemoveItem(item.GroupId, item.Item.Id))
        {
            StatusText.Text = "Selecione um item de mídia para remover.";
            return;
        }

        SavePlaylistAndRefresh("Referência removida; o arquivo original não foi alterado.");
    }

    private async void PlaylistTree_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PlaylistTree.SelectedItem is not PlaylistItemNode item)
        {
            return;
        }

        await ActivatePlaylistItemAsync(item);
    }

    private async Task ActivatePlaylistItemAsync(PlaylistItemNode item)
    {
        if (!_licensingAllowsAccess || _controlsBusy)
        {
            return;
        }

        if (item.Item.Kind == MediaKind.YouTube)
        {
            await LoadYouTubeItemAsync(item);
            return;
        }

        StatusText.Text = "Verificando disponibilidade da mídia…";
        var probe = await MediaFileProbe.ProbeAsync(item.Item.OriginalPath, TimeSpan.FromSeconds(4));
        item.SetAvailability(probe);
        var action = PlaylistActivationPolicy.Resolve(
            item.Item.Kind,
            probe == MediaProbeStatus.Available,
            SelectedOutputCount());
        if (action == PlaylistActivationAction.None)
        {
            StatusText.Text = probe switch
            {
                MediaProbeStatus.Missing => $"Arquivo não encontrado: {item.Name}",
                MediaProbeStatus.TimedOut => $"A disponibilidade de {item.Name} não pôde ser confirmada no tempo esperado.",
                MediaProbeStatus.InvalidPath => $"Caminho inválido: {item.Name}",
                _ => $"A mídia não está disponível: {item.Name}"
            };
            return;
        }

        var outcome = await LoadMediaAsync(
            item.Item.OriginalPath,
            playImmediately: action == PlaylistActivationAction.LoadAndPlay,
            verifiedProbe: probe,
            playlistItemId: item.Item.Id);
        if (outcome == MediaLoadOutcome.DecodeFailed)
        {
            item.SetDecodeFailure();
        }
    }

    private async Task LoadYouTubeItemAsync(PlaylistItemNode node)
    {
        if (!_licensingAllowsAccess || _controlsBusy || _isClosing ||
            !YouTubeVideoReferenceParser.TryParse(node.Item.OriginalPath, out var reference))
        {
            StatusText.Text = "Link do YouTube inválido ou GuiaPlay indisponível.";
            return;
        }

        var selected = _outputChoices.Where(choice => choice.IsSelected)
            .Select(choice => choice.Monitor).ToArray();
        if (_operatorRequiresConfiguration || _operatorMonitor is null ||
            selected.Length != 1 || selected[0].Id == _operatorMonitor.Id)
        {
            StatusText.Text = "YouTube exige uma única tela pública selecionada, separada do operador.";
            return;
        }

        var monitor = selected[0];
        var eligible = YouTubeEmbedGate.Assess(
            monitor.Width, monitor.Height, hasAppIdentification: true,
            isActuallyVisible: true, automaticPlayback: false,
            visibleViewportFraction: 1, otherAutoPlayingYouTubePlayersOnSameScreen: 0);
        if (!eligible.Allowed)
        {
            StatusText.Text = eligible.Reason ?? "A saída pública não atende às exigências do YouTube.";
            return;
        }

        if ((_coordinator.IsActive || _youtubeWindow is not null) &&
            MessageBox.Show(this,
                "Interromper a mídia atual e abrir o player oficial do YouTube na tela selecionada?",
                "Trocar reprodução", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                != MessageBoxResult.Yes)
        {
            return;
        }

        var window = new YouTubeOutputWindow(monitor);
        SetControlsBusy(true);
        _suppressAutoAdvance = true;
        try
        {
            // One backend at a time: stop LibVLC before showing the official player.
            if (_coordinator.MediaPath is not null)
            {
                await _engine.StopAsync();
                _coordinator.Stop();
            }

            CloseOutputWindows();
            CloseYouTubeWindow();
            _youtubeWindow = window;
            _youtubeReference = reference;
            _youtubeSnapshot = new YouTubePlaybackSnapshot(
                "CARREGANDO", false, 0, 0, null, null);
            window.PlaybackStateChanged += YouTubeWindow_OnStateChanged;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_youtubeWindow, window))
                {
                    _youtubeWindow = null;
                    _youtubeReference = null;
                    _youtubeSnapshot = null;
                    if (!_isClosing)
                    {
                        StatusText.Text = "Player do YouTube fechado; conteúdo local preservado.";
                        RefreshAudioPanel();
                        UpdateActiveOutputs();
                        RefreshPlaybackControls();
                    }
                }
            };

            window.Show();
            await window.InitializeAsync(reference!);
            if (_isClosing || !_licensingAllowsAccess)
            {
                CloseYouTubeWindow();
                return;
            }

            _loadedPlaylistItemId = node.Item.Id;
            _loadedPlaylistRevision = _playlistRevision;
            // Official IFrame playback is manual-only in the constrained mode.
            PlaybackModeCombo.SelectedIndex = 0;
            RefreshAudioPanel();
            _bitmap = null;
            PreviewImage.Source = null;
            ProgramImage.Source = null;
            PreviewPlaceholder.Text = "YouTube · prévia informativa";
            PreviewEmptyHint.Text = "A imagem do YouTube aparece somente na tela pública.";
            PreviewEmptyState.Visibility = Visibility.Visible;
            FileNameText.Text = node.Name;
            UpdateTransportArtwork(MediaKind.YouTube, node.Item.OriginalPath);
            FileNameText.ToolTip = reference!.WatchUri.AbsoluteUri;
            ProgressSlider.Value = 0;
            ProgressSlider.Maximum = 1;
            UpdateTimeLabels(0, 0);
            StatusText.Text = "YouTube aberto. Pressione Reproduzir ou utilize o player visível no projetor.";
            RefreshPlaybackControls();
        }
        catch (Exception exception)
        {
            FileLogger.Error("Falha ao preparar o player oficial do YouTube.", exception);
            CloseYouTubeWindow();
            StatusText.Text = "WebView2/YouTube não disponível. Verifique Runtime Edge e conexão.";
        }
        finally
        {
            _suppressAutoAdvance = false;
            SetControlsBusy(false);
        }
    }

    private void YouTubeWindow_OnStateChanged(object? sender, YouTubePlaybackSnapshot status)
    {
        if (_isClosing || !_licensingAllowsAccess || !ReferenceEquals(sender, _youtubeWindow))
        {
            return;
        }

        var firstReady = _youtubeSnapshot?.Ready != true && status.Ready;
        _youtubeSnapshot = status;
        if (firstReady && _youtubeWindow is { } readyWindow)
        {
            readyWindow.Command("volume", VolumeSlider.Value);
            readyWindow.Command(MuteButton.Tag as bool? == true ? "mute" : "unmute");
        }

        if (!_isDraggingProgress)
        {
            ProgressSlider.Maximum = Math.Max(1, status.DurationMilliseconds);
            ProgressSlider.Value = Math.Clamp(
                status.ElapsedMilliseconds, 0, Math.Max(1, status.DurationMilliseconds));
        }

        UpdateTimeLabels(status.ElapsedMilliseconds, status.DurationMilliseconds);
        if (!string.IsNullOrWhiteSpace(status.Message))
        {
            StatusText.Text = status.Message;
        }

        RefreshPlaybackControls();
    }

    private void CloseYouTubeWindow()
    {
        var old = _youtubeWindow;
        _youtubeWindow = null;
        _youtubeReference = null;
        _youtubeSnapshot = null;
        if (old is not null)
        {
            old.PlaybackStateChanged -= YouTubeWindow_OnStateChanged;
            old.Close();
        }

        if (_settingsInitialized && !_isClosing)
        {
            RefreshAudioPanel();
            UpdateActiveOutputs();
        }
    }

    // GP-I03: table search is a view filter. It never reorders persisted playback data.
    private bool MediaGridFilter(object value)
    {
        if (value is not MediaGridRow row)
        {
            return false;
        }

        var query = MediaSearchBox?.Text?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            return true;
        }

        return row.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               row.GroupName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               row.FilePath.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               row.Kind.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private void MediaSearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (MediaSearchHint is not null)
        {
            MediaSearchHint.Visibility = string.IsNullOrWhiteSpace(MediaSearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (_mediaGridView is null)
        {
            return;
        }

        _mediaGridView.Refresh();
        UpdateMediaGridStatus();
    }

    private void ClearMediaSearchButton_OnClick(object sender, RoutedEventArgs e)
    {
        MediaSearchBox.Clear();
        _ = MediaSearchBox.Focus();
    }

    private MediaGridRow? SelectedMediaRow() => MediaGrid.SelectedItem as MediaGridRow;

    private void MediaGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateMediaGridActions();

    private void UpdateMediaGridStatus()
    {
        var visible = _mediaGridView?.Cast<MediaGridRow>().Count() ?? _mediaRows.Count;
        MediaGridCountText.Text = visible == _mediaRows.Count
            ? $"{_mediaRows.Count} mídias"
            : $"{visible} de {_mediaRows.Count} mídias";
        MediaGridEmptyText.Text = _mediaRows.Count == 0
            ? "Crie um grupo e adicione mídias para montar a reprodução."
            : "Nenhuma mídia corresponde à pesquisa.";
        MediaGridEmptyText.Visibility = visible == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateMediaGridActions();
    }

    private void UpdateMediaGridActions()
    {
        var row = SelectedMediaRow();
        var canMove = row is not null && _licensingAllowsAccess && !_controlsBusy;
        MediaRemoveSelectedButton.IsEnabled = canMove;
        var items = canMove
            ? _playlistCatalog.Snapshot.Groups.FirstOrDefault(group => group.Id == row!.GroupId)?.Items
            : null;
        var index = items is null ? -1 : items.ToList().FindIndex(item => item.Id == row!.ItemId);
        MediaMoveUpButton.IsEnabled = canMove && index > 0;
        MediaMoveDownButton.IsEnabled = canMove && index >= 0 && index < (items?.Count ?? 0) - 1;
    }

    private void RemoveSelectedMediaButton_OnClick(object sender, RoutedEventArgs e) =>
        RemoveMediaGridRow(SelectedMediaRow());

    private void MediaMoveUpButton_OnClick(object sender, RoutedEventArgs e) =>
        MoveMediaGridRow(SelectedMediaRow(), -1);

    private void MediaMoveDownButton_OnClick(object sender, RoutedEventArgs e) =>
        MoveMediaGridRow(SelectedMediaRow(), 1);

    private void MoveMediaGridRow(MediaGridRow? row, int direction)
    {
        if (!_licensingAllowsAccess || _controlsBusy || row is null || Math.Abs(direction) != 1)
        {
            return;
        }

        var group = _playlistCatalog.Snapshot.Groups.FirstOrDefault(item => item.Id == row.GroupId);
        if (group is null)
        {
            return;
        }

        var index = group.Items.ToList().FindIndex(item => item.Id == row.ItemId);
        var next = index + direction;
        if (index < 0 || next < 0 || next >= group.Items.Count)
        {
            return;
        }

        if (_playlistCatalog.MoveItem(row.GroupId, row.ItemId, row.GroupId, next))
        {
            SavePlaylistAndRefresh("Ordem das mídias atualizada.");
        }
    }

    private async void MediaGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is MediaGridRow row)
        {
            await ActivateMediaGridRowAsync(row);
        }
    }

    private async Task ActivateMediaGridRowAsync(MediaGridRow row)
    {
        var node = _playlistGroups.FirstOrDefault(group => group.Id == row.GroupId)?
            .Items.FirstOrDefault(item => item.Item.Id == row.ItemId);
        if (node is not null)
        {
            await ActivatePlaylistItemAsync(node);
        }
    }

    private void RemoveMediaGridRow(MediaGridRow? row)
    {
        if (!_licensingAllowsAccess || _controlsBusy || row is null)
        {
            return;
        }

        if (_playlistCatalog.RemoveItem(row.GroupId, row.ItemId))
        {
            SavePlaylistAndRefresh("Referência removida da playlist; arquivo original preservado.");
        }
    }

    private void MediaRowActionsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not MediaGridRow row ||
            !_licensingAllowsAccess || _controlsBusy)
        {
            return;
        }

        MediaGrid.SelectedItem = row;
        var menu = new ContextMenu();
        var play = new MenuItem { Header = "Reproduzir", IsEnabled = row.Node.IsAvailable };
        play.Click += async (_, _) => await ActivateMediaGridRowAsync(row);
        var moveUp = new MenuItem { Header = "Mover para cima" };
        moveUp.Click += (_, _) => MoveMediaGridRow(row, -1);
        var moveDown = new MenuItem { Header = "Mover para baixo" };
        moveDown.Click += (_, _) => MoveMediaGridRow(row, 1);
        var remove = new MenuItem { Header = "Remover referência" };
        remove.Click += (_, _) => RemoveMediaGridRow(row);
        menu.Items.Add(play);
        menu.Items.Add(moveUp);
        menu.Items.Add(moveDown);
        menu.Items.Add(new Separator());
        menu.Items.Add(remove);

        var group = _playlistCatalog.Snapshot.Groups.FirstOrDefault(item => item.Id == row.GroupId);
        var index = group?.Items.ToList().FindIndex(item => item.Id == row.ItemId) ?? -1;
        moveUp.IsEnabled = index > 0;
        moveDown.IsEnabled = group is not null && index >= 0 && index < group.Items.Count - 1;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void MediaGrid_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _mediaGridDragStart = e.GetPosition(MediaGrid);
        // Buttons and scrollbars must not accidentally initiate media reordering.
        if (FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null ||
            FindAncestor<ScrollBar>(e.OriginalSource as DependencyObject) is not null)
        {
            _mediaGridDragSource = null;
            return;
        }

        _mediaGridDragSource = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item
            as MediaGridRow;
    }

    private void MediaGrid_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_licensingAllowsAccess || _controlsBusy ||
            e.LeftButton != MouseButtonState.Pressed || _mediaGridDragSource is null)
        {
            return;
        }

        var current = e.GetPosition(MediaGrid);
        if (Math.Abs(current.X - _mediaGridDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _mediaGridDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var data = new DataObject();
        data.SetData(MediaGridDragFormat, _mediaGridDragSource);
        _ = DragDrop.DoDragDrop(MediaGrid, data, DragDropEffects.Move);
        _mediaGridDragSource = null;
    }

    private void MediaGrid_OnPreviewDragOver(object sender, DragEventArgs e)
    {
        var target = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item
            as MediaGridRow;
        e.Effects = target is not null && e.Data.GetDataPresent(MediaGridDragFormat) &&
                    _licensingAllowsAccess && !_controlsBusy
            ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void MediaGrid_OnDrop(object sender, DragEventArgs e)
    {
        var target = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject)?.Item
            as MediaGridRow;
        var source = e.Data.GetDataPresent(MediaGridDragFormat)
            ? e.Data.GetData(MediaGridDragFormat) as MediaGridRow
            : null;
        if (source is null || target is null || !_licensingAllowsAccess || _controlsBusy)
        {
            e.Handled = true;
            return;
        }

        var destGroup = _playlistCatalog.Snapshot.Groups.FirstOrDefault(group => group.Id == target.GroupId);
        var targetIndex = destGroup?.Items.ToList().FindIndex(item => item.Id == target.ItemId) ?? -1;
        if (targetIndex >= 0 && _playlistCatalog.MoveItem(
                source.GroupId, source.ItemId, target.GroupId, targetIndex))
        {
            SavePlaylistAndRefresh("Mídia reposicionada; ordem de reprodução salva.");
        }

        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? origin) where T : DependencyObject
    {
        var current = origin;
        while (current is not null)
        {
            if (current is T target)
            {
                return target;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    private async Task RefreshMediaGridMetadataAsync(
        IReadOnlyList<MediaGridRow> snapshotRows, CancellationToken token)
    {
        await MediaGridMetadataService.InspectAsync(snapshotRows, async (row, metadata) =>
        {
            if (token.IsCancellationRequested || _isClosing)
            {
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (!token.IsCancellationRequested && !_isClosing && _mediaRows.Contains(row))
                {
                    row.ApplyMetadata(metadata);
                    if (_coordinator.MediaPath is { } loadedPath &&
                        string.Equals(row.FilePath, loadedPath, StringComparison.OrdinalIgnoreCase))
                    {
                        UpdateTransportArtwork(row.Node.Item.Kind, row.FilePath);
                    }
                }
            }, DispatcherPriority.Background, token);
        }, token);
    }

    private void PlaylistTree_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _playlistDragStart = e.GetPosition(PlaylistTree);
        _playlistDragSource = FindPlaylistNode(e.OriginalSource as DependencyObject);
    }

    private void PlaylistTree_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _playlistDragSource is null)
        {
            return;
        }

        var position = e.GetPosition(PlaylistTree);
        if (Math.Abs(position.X - _playlistDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _playlistDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var data = new DataObject();
        data.SetData(PlaylistDragFormat, _playlistDragSource);
        _ = DragDrop.DoDragDrop(PlaylistTree, data, DragDropEffects.Move);
        _playlistDragSource = null;
    }

    private void PlaylistTree_OnPreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Link
            : e.Data.GetDataPresent(PlaylistDragFormat)
                ? DragDropEffects.Move
                : DragDropEffects.None;
        e.Handled = true;
    }

    private async void PlaylistTree_OnDrop(object sender, DragEventArgs e)
    {
        var target = FindPlaylistNode(e.OriginalSource as DependencyObject);
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            e.Handled = true;
            await ImportDroppedFilesAsync(paths, target);
            return;
        }

        if (!e.Data.GetDataPresent(PlaylistDragFormat))
        {
            return;
        }

        var source = e.Data.GetData(PlaylistDragFormat);
        var changed = source switch
        {
            PlaylistGroupNode group => MovePlaylistGroup(group, target),
            PlaylistItemNode item => MovePlaylistItem(item, target),
            _ => false
        };
        if (changed)
        {
            SavePlaylistAndRefresh("Ordem da playlist atualizada.");
        }

        e.Handled = true;
    }

    private async Task ImportDroppedFilesAsync(IEnumerable<string> paths, object? target)
    {
        if (!_licensingAllowsAccess || _controlsBusy || _folderImportActive)
        {
            return;
        }

        var receivedPaths = paths.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
        if (receivedPaths.Length == 0)
        {
            StatusText.Text = "Nenhum arquivo foi recebido.";
            return;
        }

        // Explorer drops may contain folders mixed with loose media files.
        // Enumerate types off the dispatcher; folders use the same safe scanner as Add > Pasta.
        var folders = await Task.Run(() => receivedPaths.Where(Directory.Exists).ToArray());
        foreach (var folder in folders)
        {
            await ImportFolderAsync(folder);
            if (_isClosing || !_licensingAllowsAccess)
            {
                return;
            }
        }

        var folderPaths = new HashSet<string>(folders, StringComparer.OrdinalIgnoreCase);
        var existingPaths = new HashSet<string>(
            _playlistCatalog.Snapshot.Groups.SelectMany(group => group.Items)
                .Select(item => item.OriginalPath),
            StringComparer.OrdinalIgnoreCase);
        var uniquePaths = new List<string>();
        var duplicates = 0;
        foreach (var path in receivedPaths.Where(path => !folderPaths.Contains(path)))
        {
            try
            {
                var normalized = Path.GetFullPath(path);
                if (!existingPaths.Add(normalized))
                {
                    duplicates++;
                    continue;
                }

                uniquePaths.Add(normalized);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Invalid paths are rejected later by the existing media probe.
                uniquePaths.Add(path);
            }
        }

        if (uniquePaths.Count == 0)
        {
            if (duplicates > 0)
            {
                StatusText.Text = $"{duplicates} mídia(s) duplicada(s) ignorada(s).";
            }

            return;
        }

        var targetGroupId = ResolveDropGroup(target)?.Id;
        var cancellationToken = _playlistAvailabilityCancellation?.Token ?? CancellationToken.None;
        var revision = _playlistRevision;
        IReadOnlyList<MediaProbeResult> probes;
        try
        {
            StatusText.Text = "Verificando arquivos recebidos…";
            probes = await MediaFileProbe.ProbeManyAsync(
                uniquePaths, TimeSpan.FromSeconds(3), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            FileLogger.Error("Falha ao verificar os arquivos importados.", exception);
            StatusText.Text = "Não foi possível verificar arquivos; consulte o log.";
            return;
        }

        if (_isClosing || !_licensingAllowsAccess || revision != _playlistRevision || _controlsBusy)
        {
            StatusText.Text = "A lista mudou durante a importação; operação descartada.";
            return;
        }

        // Stage on an independent catalog. Failed persistence leaves the live
        // catalog and the saved working playlist untouched.
        var staged = new PlaylistCatalog(_playlistCatalog.Snapshot);
        var result = staged.ImportProbedItems(targetGroupId, "Mídias", probes);
        if (result.Added.Count == 0 || result.TargetGroup is not { } targetGroup)
        {
            StatusText.Text = probes.Any(probe => probe.Status == MediaProbeStatus.TimedOut)
                ? "Nenhum arquivo adicionado; ao menos um caminho não respondeu."
                : "Nenhum arquivo de mídia disponível e reconhecido foi adicionado.";
            return;
        }

        if (Application.Current is not App app || app.PlaylistStore is null)
        {
            StatusText.Text = "O armazenamento da playlist não está disponível.";
            return;
        }

        var stagedDocument = staged.Snapshot;
        var saved = app.PlaylistStore.Save(stagedDocument);
        if (!saved.Succeeded)
        {
            FileLogger.Error($"Falha ao persistir arquivos recebidos: {saved.Error}");
            StatusText.Text = "Não foi possível salvar os arquivos importados; lista anterior preservada.";
            return;
        }

        _playlistCatalog.ApplyPersistedChanges(stagedDocument);
        app.Playlist = _playlistCatalog.Snapshot;
        RebuildPlaylistTree();
        RefreshActivePlaylistMarkers();
        var ignored = result.Rejected.Count + duplicates;
        StatusText.Text = $"{result.Added.Count} mídia(s) em {targetGroup.Name}. " +
                          $"{ignored} arquivo(s) ignorado(s); mídias originais preservadas.";
    }

    private bool MovePlaylistGroup(PlaylistGroupNode source, object? target)
    {
        var targetGroup = ResolveDropGroup(target);
        var targetIndex = targetGroup is null
            ? _playlistGroups.Count - 1
            : _playlistGroups.IndexOf(targetGroup);
        return _playlistCatalog.MoveGroup(source.Id, targetIndex);
    }

    private bool MovePlaylistItem(PlaylistItemNode source, object? target)
    {
        if (target is PlaylistItemNode targetItem)
        {
            var targetGroup = _playlistGroups.First(group => group.Id == targetItem.GroupId);
            var targetIndex = targetGroup.Items.IndexOf(targetItem);
            return _playlistCatalog.MoveItem(source.GroupId, source.Item.Id, targetItem.GroupId, targetIndex);
        }

        var group = ResolveDropGroup(target) ?? _playlistGroups.FirstOrDefault(candidate => candidate.Id == source.GroupId);
        return group is not null &&
               _playlistCatalog.MoveItem(source.GroupId, source.Item.Id, group.Id, group.Items.Count);
    }

    private PlaylistGroupNode? ResolveDropGroup(object? target) => target switch
    {
        PlaylistGroupNode group => group,
        PlaylistItemNode item => _playlistGroups.FirstOrDefault(group => group.Id == item.GroupId),
        _ => SelectedPlaylistGroup() ?? _playlistGroups.FirstOrDefault()
    };

    private static object? FindPlaylistNode(DependencyObject? origin)
    {
        var current = origin;
        while (current is not null)
        {
            if (current is TreeViewItem item)
            {
                return item.DataContext;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private PlaylistGroupNode? SelectedPlaylistGroup() => PlaylistTree.SelectedItem switch
    {
        PlaylistGroupNode group => group,
        PlaylistItemNode item => _playlistGroups.FirstOrDefault(group => group.Id == item.GroupId),
        _ => null
    };

    private void RebuildPlaylistTree()
    {
        var previousSelection = SelectedMediaRow()?.ItemId;
        _playlistAvailabilityCancellation?.Cancel();
        _playlistAvailabilityCancellation?.Dispose();
        _playlistAvailabilityCancellation = new CancellationTokenSource();
        _playlistGroups.Clear();
        _mediaRows.Clear();
        var order = 0;
        foreach (var group in _playlistCatalog.Snapshot.Groups)
        {
            var node = new PlaylistGroupNode(group);
            _playlistGroups.Add(node);
            foreach (var item in node.Items)
            {
                _mediaRows.Add(new MediaGridRow(++order, group.Name, item));
            }
        }

        PlaylistEmptyState.Visibility = _playlistGroups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var mediaCount = _mediaRows.Count;
        WorkingPlaylistSummaryText.Text = $"{_playlistGroups.Count} grupos · {mediaCount} mídias";
        _mediaGridView?.Refresh();
        MediaGrid.SelectedItem = _mediaRows.FirstOrDefault(row => row.ItemId == previousSelection);
        UpdateMediaGridStatus();
        if (_settingsInitialized)
        {
            RefreshPlaylistTransport();
        }

        var token = _playlistAvailabilityCancellation.Token;
        _ = RefreshLocalPlaylistAvailabilityAsync(token);
        if (_settingsInitialized && !_isClosing)
        {
            _ = RefreshMediaGridMetadataAsync(_mediaRows.ToArray(), token);
        }
    }

    private async Task RefreshLocalPlaylistAvailabilityAsync(CancellationToken cancellationToken)
    {
        var items = _playlistGroups.SelectMany(group => group.Items)
            .Where(item => item.Item.Kind != MediaKind.YouTube).ToArray();
        if (items.Length == 0)
        {
            return;
        }

        try
        {
            var results = await MediaFileProbe.ProbeManyAsync(
                items.Select(item => item.Item.OriginalPath),
                TimeSpan.FromSeconds(2),
                cancellationToken);
            await Dispatcher.InvokeAsync(() =>
            {
                for (var index = 0; index < items.Length; index++)
                {
                    items[index].SetAvailability(results[index].Status);
                }
            }, DispatcherPriority.Background, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void SavePlaylistAndRefresh(string successMessage)
    {
        var snapshot = _playlistCatalog.Snapshot;
        if (Application.Current is not App app || app.PlaylistStore is null)
        {
            StatusText.Text = "O armazenamento da playlist ainda não está disponível.";
            return;
        }

        var result = app.PlaylistStore.Save(snapshot);
        if (!result.Succeeded)
        {
            StatusText.Text = "Não foi possível salvar a playlist; consulte o log.";
            FileLogger.Error($"Falha ao salvar playlist: {result.Error}");
            return;
        }

        app.Playlist = snapshot;
        RebuildPlaylistTree();
        RefreshActivePlaylistMarkers();
        StatusText.Text = successMessage;
    }

    // GP-I06: playlist selection is independent of the search filter and is never
    // treated as a queued automatic command unless the operator enables Sequencial.
    private void PlaybackModeCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _playbackMode = PlaybackModeCombo.SelectedIndex == 1
            ? PlaylistPlaybackMode.Sequential
            : PlaylistPlaybackMode.Manual;
        if (_settingsInitialized)
        {
            RefreshPlaylistTransport();
            StatusText.Text = _playbackMode == PlaylistPlaybackMode.Manual
                ? "Modo manual: nenhuma mídia será iniciada automaticamente ao terminar."
                : "Modo sequencial: a próxima mídia será iniciada ao terminar, se a playlist não mudar.";
        }
    }

    private void RepeatModeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_licensingAllowsAccess || _controlsBusy)
        {
            return;
        }

        _repeatMode = _repeatMode switch
        {
            PlaylistRepeatMode.Off => PlaylistRepeatMode.Current,
            PlaylistRepeatMode.Current => PlaylistRepeatMode.All,
            _ => PlaylistRepeatMode.Off
        };
        RefreshPlaylistTransport();
        StatusText.Text = _repeatMode == PlaylistRepeatMode.Off
            ? "Repetição desativada."
            : _repeatMode == PlaylistRepeatMode.Current
                ? "Repetir mídia: aplicado somente no modo sequencial ao terminar."
                : "Repetir playlist: a última mídia retorna à primeira no modo sequencial.";
    }

    private void ShufflePlaylistCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        _shufflePlayback = ShufflePlaylistCheckBox.IsChecked == true;
        _shufflePlayedItems.Clear();
        if (_loadedPlaylistItemId is { } current)
        {
            _shufflePlayedItems.Add(current);
        }

        if (_settingsInitialized)
        {
            RefreshPlaylistTransport();
            StatusText.Text = _shufflePlayback
                ? "Próxima mídia aleatória habilitada. No modo manual nada será iniciado automaticamente."
                : "Ordem original da playlist restaurada para a próxima seleção.";
        }
    }

    private async void PreviousMediaButton_OnClick(object sender, RoutedEventArgs e) =>
        await NavigatePlaylistAsync(previous: true);

    private async void NextMediaButton_OnClick(object sender, RoutedEventArgs e) =>
        await NavigatePlaylistAsync(previous: false);

    private async Task NavigatePlaylistAsync(bool previous)
    {
        if (!_licensingAllowsAccess || _controlsBusy || _automaticAdvanceInProgress)
        {
            return;
        }

        var playlist = _playlistCatalog.Snapshot;
        var id = previous
            ? PlaylistPlaybackNavigation.Previous(playlist, _loadedPlaylistItemId)
            : PlaylistPlaybackNavigation.Next(
                playlist, _loadedPlaylistItemId, _playbackMode, _repeatMode,
                _shufflePlayback, automatic: false, randomCandidateIndex: Random.Shared.Next(),
                playedInShuffleCycle: _shufflePlayedItems);
        if (id is not { } target)
        {
            StatusText.Text = previous
                ? "Não existe mídia anterior na playlist."
                : "Não existe próxima mídia na playlist.";
            return;
        }

        var item = _playlistGroups.SelectMany(group => group.Items)
            .FirstOrDefault(candidate => candidate.Item.Id == target);
        if (item is null)
        {
            StatusText.Text = "A mídia de destino não está mais na playlist.";
            return;
        }

        // Explicit Previous/Next loads without autoplay, for either backend.
        if (item.Item.Kind == MediaKind.YouTube)
        {
            await LoadYouTubeItemAsync(item);
        }
        else
        {
            await LoadMediaAsync(item.Item.OriginalPath,
                playImmediately: false, playlistItemId: target);
        }
    }

    private void FullscreenPreviewButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_fullscreenPreviewWindow is { } alreadyOpen)
        {
            alreadyOpen.Close();
            return;
        }

        if (!_licensingAllowsAccess || _controlsBusy ||
            _operatorMonitor is not { } operatorMonitor || _operatorRequiresConfiguration)
        {
            StatusText.Text = "Configure a tela do operador antes de abrir a prévia em tela cheia.";
            return;
        }

        var window = new OperatorPreviewWindow(operatorMonitor, PreviewImage) { Owner = this };
        _fullscreenPreviewWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_fullscreenPreviewWindow, window))
            {
                _fullscreenPreviewWindow = null;
            }

            if (!_isClosing)
            {
                _ = Activate();
            }
        };
        window.Show();
        StatusText.Text = "Prévia ampliada no monitor do operador. Esc ou F11 retorna à mesa de controle.";
    }

    private void BackFiveButton_OnClick(object sender, RoutedEventArgs e) =>
        SeekRelative(-5_000);

    private void ForwardFiveButton_OnClick(object sender, RoutedEventArgs e) =>
        SeekRelative(5_000);

    private void SeekRelative(long delta)
    {
        if (!_licensingAllowsAccess || _controlsBusy || !CanSeekNow())
        {
            return;
        }

        var duration = _youtubeWindow is null ? _engine.Length :
            _youtubeSnapshot?.DurationMilliseconds ?? 0;
        var time = _youtubeWindow is null ? _engine.Time :
            _youtubeSnapshot?.ElapsedMilliseconds ?? 0;
        var current = Math.Clamp(time, 0, duration);
        _ = TrySeek(Math.Clamp(current + delta, 0, duration));
    }

    private bool CanSeekNow() =>
        _youtubeWindow is not null
            ? _licensingAllowsAccess && !_controlsBusy &&
              _youtubeWindow.Ready && (_youtubeSnapshot?.DurationMilliseconds ?? 0) > 0
            : _licensingAllowsAccess &&
        !_controlsBusy &&
        _coordinator.MediaPath is not null &&
        _engine.Length > 0 &&
        _engine.IsSeekable &&
        _coordinator.Status is (PlaybackStatus.Ready or PlaybackStatus.Preparing or
            PlaybackStatus.Playing or PlaybackStatus.Paused or PlaybackStatus.Stopped);

    private void RefreshSeekAvailability()
    {
        if (!_settingsInitialized)
        {
            return;
        }

        var enabled = CanSeekNow();
        ProgressSlider.IsEnabled = enabled;
        PreviewProgressSlider.IsEnabled = enabled;
        BackFiveButton.IsEnabled = enabled;
        ForwardFiveButton.IsEnabled = enabled;
    }

    private void RefreshPlaylistTransport()
    {
        if (!_settingsInitialized)
        {
            return;
        }

        RepeatModeButtonText.Text = _repeatMode switch
        {
            PlaylistRepeatMode.Current => "Repetir: Atual",
            PlaylistRepeatMode.All => "Repetir: Lista",
            _ => "Repetir: Não"
        };
        var eligible = _licensingAllowsAccess && !_controlsBusy &&
                       !_automaticAdvanceInProgress;
        FullscreenPreviewButton.IsEnabled = _licensingAllowsAccess && !_controlsBusy &&
                                           _youtubeWindow is null &&
                                           _operatorMonitor is not null && !_operatorRequiresConfiguration;
        var playlist = _playlistCatalog.Snapshot;
        var previous = PlaylistPlaybackNavigation.Previous(playlist, _loadedPlaylistItemId);
        var next = PlaylistPlaybackNavigation.Next(
            playlist, _loadedPlaylistItemId, _playbackMode, _repeatMode,
            _shufflePlayback, automatic: false,
            playedInShuffleCycle: _shufflePlayedItems);
        PreviousMediaButton.IsEnabled = eligible && previous is not null;
        NextMediaButton.IsEnabled = eligible && next is not null;
        RepeatModeButton.IsEnabled = _youtubeWindow is null && _licensingAllowsAccess && !_controlsBusy;
        ShufflePlaylistCheckBox.IsEnabled = _youtubeWindow is null && _licensingAllowsAccess && !_controlsBusy;
        PlaybackModeCombo.IsEnabled = _youtubeWindow is null && _licensingAllowsAccess && !_controlsBusy;
        RefreshSeekAvailability();

        if (_loadedPlaylistItemId is null)
        {
            NextPlaylistItemText.Text = "Próxima: abra uma mídia da playlist";
            return;
        }

        if (!PlaylistPlaybackNavigation.Contains(playlist, _loadedPlaylistItemId))
        {
            NextPlaylistItemText.Text = "Mídia atual removida da playlist";
            return;
        }

        if (_playbackMode == PlaylistPlaybackMode.Sequential &&
            _loadedPlaylistRevision != _playlistRevision)
        {
            NextPlaylistItemText.Text = "Próxima automática suspensa: playlist foi alterada";
            return;
        }

        if (_shufflePlayback && next is not null)
        {
            NextPlaylistItemText.Text = _playbackMode == PlaylistPlaybackMode.Manual
                ? "Próxima: aleatória ao clicar (manual)"
                : "Próxima: aleatória ao terminar";
            return;
        }

        var name = next is { } id
            ? playlist.Groups.SelectMany(group => group.Items)
                .FirstOrDefault(item => item.Id == id)?.DisplayName
            : null;
        NextPlaylistItemText.Text = name is null
            ? "Próxima: fim da playlist"
            : $"Próxima: {name}{(_playbackMode == PlaylistPlaybackMode.Manual ? " (manual)" : "")}";
    }

    private async Task<bool> TryAutomaticAdvanceAsync(long endedGeneration)
    {
        if (_playbackMode != PlaylistPlaybackMode.Sequential ||
            !_licensingAllowsAccess || _controlsBusy || _isClosing ||
            _automaticAdvanceInProgress || _loadedPlaylistItemId is null ||
            _playlistRevision != _loadedPlaylistRevision ||
            endedGeneration != _coordinator.Generation)
        {
            return false;
        }

        var playlist = _playlistCatalog.Snapshot;
        var nextId = PlaylistPlaybackNavigation.Next(
            playlist, _loadedPlaylistItemId, _playbackMode, _repeatMode,
            _shufflePlayback, automatic: true, randomCandidateIndex: Random.Shared.Next(),
            playedInShuffleCycle: _shufflePlayedItems);
        if (nextId is not { } targetId)
        {
            return false;
        }

        var item = _playlistGroups.SelectMany(group => group.Items)
            .FirstOrDefault(candidate => candidate.Item.Id == targetId);
        if (item is null)
        {
            return false;
        }

        // When Repeat All wraps a completed random cycle, start a fresh cycle
        // before recording the next successful load.
        if (_shufflePlayback && _repeatMode == PlaylistRepeatMode.All &&
            _shufflePlayedItems.Contains(targetId) && targetId != _loadedPlaylistItemId)
        {
            _shufflePlayedItems.Clear();
        }

        if (item.Item.Kind == MediaKind.YouTube)
        {
            StatusText.Text = "Sequência pausada: YouTube precisa de abertura explícita na saída pública.";
            return false;
        }

        _automaticAdvanceInProgress = true;
        try
        {
            var result = await LoadMediaAsync(
                item.Item.OriginalPath, playImmediately: true,
                playlistItemId: targetId, fromAutomaticSequence: true);
            if (result != MediaLoadOutcome.Loaded ||
                _coordinator.Status is not (PlaybackStatus.Preparing or PlaybackStatus.Playing))
            {
                StatusText.Text = $"Reprodução sequencial interrompida: {item.Name} não pôde iniciar.";
                return false;
            }

            FileLogger.Info($"Avanço sequencial para {item.Name}, mídia {targetId:D}.");
            return true;
        }
        catch (Exception exception)
        {
            FileLogger.Error("Não foi possível avançar automaticamente a playlist.", exception);
            StatusText.Text = "A sequência foi interrompida; reprodução manual permanece disponível.";
            return false;
        }
        finally
        {
            _automaticAdvanceInProgress = false;
            RefreshPlaylistTransport();
        }
    }

    // Central transport has exactly one visible Play/Pause control.
    // Route through the existing playback handlers; do not create
    // a parallel state machine or assume YouTube command acknowledgement.
    private void MainPlayPauseButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_youtubeWindow is not null)
        {
            if (_youtubeSnapshot?.State == "REPRODUZINDO")
            {
                PauseButton_OnClick(sender, e);
            }
            else
            {
                PlayButton_OnClick(sender, e);
            }

            return;
        }

        if (_coordinator.Status == PlaybackStatus.Playing)
        {
            PauseButton_OnClick(sender, e);
        }
        else
        {
            PlayButton_OnClick(sender, e);
        }
    }

    private async void PlayButton_OnClick(object sender, RoutedEventArgs e)
        => await StartOrResumePlaybackAsync();

    private async Task StartOrResumePlaybackAsync()
    {
        if (_youtubeWindow is { Ready: true } youtube && _licensingAllowsAccess && !_controlsBusy)
        {
            youtube.Command("play");
            StatusText.Text = "Reprodução solicitada ao YouTube.";
            return;
        }

        if (_isClosing || !_licensingAllowsAccess || _controlsBusy)
        {
            return;
        }

        var selected = _outputChoices.Where(choice => choice.IsSelected).Select(choice => choice.Monitor).ToArray();
        if (!_coordinator.CanPlay(selected.Length))
        {
            RefreshPlaybackControls();
            return;
        }

        if (_coordinator.Status == PlaybackStatus.Paused)
        {
            if (!CanStartPlayback(_coordinator.MediaKind, selected.Length))
            {
                return;
            }

            _engine.Resume();
            _coordinator.Resume();
            StatusText.Text = "Reprodução retomada";
            RefreshPlaybackControls();
            return;
        }

        if (_coordinator.MediaPath is null)
        {
            return;
        }

        if (!CanStartPlayback(_coordinator.MediaKind, selected.Length))
        {
            return;
        }

        try
        {
            SetControlsBusy(true);
            var audioOutput = (Application.Current as App)?.Settings.AudioOutput ?? AudioOutputPreference.Default;
            await _engine.ApplyAudioOutputAsync(audioOutput);
            _engine.Volume = (int)VolumeSlider.Value;
            _engine.Muted = MuteButton.Tag as bool? ?? false;
            var generation = _coordinator.BeginPlayback();
            if (PlaybackRouting.UsesVideoOutputs(_coordinator.MediaKind))
            {
                PrepareOutputWindows(selected);
            }
            else
            {
                CloseOutputWindows();
            }

            Interlocked.Exchange(ref _frameCallbacks, 0);
            Interlocked.Exchange(ref _framesRendered, 0);
            Interlocked.Exchange(ref _copyTicks, 0);
            if (!_engine.Play())
            {
                throw new InvalidOperationException("O motor recusou o início da reprodução.");
            }

            StatusText.Text = "Iniciando reprodução…";
            SetConfigurationControlsEnabled(false);
            RefreshPlaybackControls();
            FileLogger.Info($"Reprodução solicitada. Geração {generation}; tipo: {_coordinator.MediaKind}; saídas públicas: {(PlaybackRouting.UsesVideoOutputs(_coordinator.MediaKind) ? selected.Length : 0)}.");
            LogDiagnosticSnapshot("Início de reprodução");
            SetControlsBusy(false);
        }
        catch (Exception exception)
        {
            CloseOutputWindows();
            _coordinator.PlaybackFailed(_coordinator.Generation);
            FileLogger.Error("Falha ao iniciar reprodução.", exception);
            MessageBox.Show(this, exception.Message, "Falha na reprodução", MessageBoxButton.OK, MessageBoxImage.Error);
            SetControlsBusy(false);
        }
    }

    private void PauseButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_youtubeWindow is { Ready: true } youtube && _licensingAllowsAccess)
        {
            youtube.Command("pause");
            StatusText.Text = "Pausa solicitada ao YouTube.";
            return;
        }

        if (_coordinator.Status == PlaybackStatus.Playing)
        {
            _engine.Pause();
            _coordinator.Pause();
            StatusText.Text = "Pausado — o quadro atual permanece nas saídas";
            PlayButtonText.Text = "Continuar";
            RefreshPlaybackControls();
        }
    }

    private async void StopButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_youtubeWindow is not null)
        {
            CloseYouTubeWindow();
            ProgressSlider.Value = 0;
            UpdateTimeLabels(0, 0);
            StatusText.Text = "Player oficial do YouTube fechado na tela pública.";
            RefreshPlaybackControls();
            return;
        }

        if (_coordinator.MediaPath is null)
        {
            return;
        }

        _suppressAutoAdvance = true;
        SetControlsBusy(true);
        CloseOutputWindows();
        var stoppedSafely = false;
        try
        {
            await _engine.StopAsync();
            _coordinator.Stop();
            _operatorRecoverySettingsAllowed = false;
            _audioReconfigurationRequired = false;
            RefreshAudioDevices(force: true);
            ProgressSlider.Value = 0;
            UpdateTimeLabels(0, Math.Max(0, _engine.Length));
            StatusText.Text = "Parado; o arquivo continua carregado";
            LogPlaybackMetrics($"Parada da geração {_coordinator.Generation}");
            LogDiagnosticSnapshot("Playback parado");
            PlayButtonText.Text = "Reproduzir";
            SetConfigurationControlsEnabled(true);
            RefreshPlaybackControls();
            stoppedSafely = true;
        }
        catch (Exception exception)
        {
            _coordinator.StopFailed();
            try
            {
                _engine.Pause();
            }
            catch (Exception pauseException)
            {
                FileLogger.Error("Falha adicional ao tentar pausar o motor após erro de parada.", pauseException);
            }

            FileLogger.Error("Falha ao parar o motor.", exception);
            StatusText.Text = "Falha ao parar com segurança; o estado foi marcado como erro e as saídas foram fechadas";
            PlayButtonText.Text = "Tentar novamente";
            SetConfigurationControlsEnabled(true);
            RefreshPlaybackControls();
        }
        finally
        {
            _suppressAutoAdvance = false;
            SetControlsBusy(false);
        }

        if (stoppedSafely)
        {
            await TryAutomaticInstallationAsync();
        }
    }

    private async void IdentifyButton_OnClick(object sender, RoutedEventArgs e)
    {
        CloseIdentifierWindows();
        var cancellation = new CancellationTokenSource();
        _identifierCancellation = cancellation;
        foreach (var monitor in _monitors)
        {
            var window = new IdentifierWindow(monitor) { Owner = this };
            _identifierWindows.Add(window);
            window.Show();
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), cancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_identifierCancellation, cancellation))
            {
                CloseIdentifierWindows();
            }

            cancellation.Dispose();
        }
    }

    // GP-I07: shortcuts are exclusively window-scoped. Editing fields and native
    // Space interactions never trigger playback; Esc still closes monitor IDs.
    private void MainWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _identifierWindows.Count > 0)
        {
            CloseIdentifierWindows();
            e.Handled = true;
            return;
        }

        if (e.Handled || _isClosing || !IsActive || _identifierWindows.Count > 0)
        {
            return;
        }

        var focused = Keyboard.FocusedElement as DependencyObject ??
                      e.OriginalSource as DependencyObject;
        var textEditing = FindAncestor<TextBoxBase>(focused) is not null ||
                          FindAncestor<PasswordBox>(focused) is not null ||
                          FindAncestor<ComboBox>(focused) is not null;
        var nativeSpace = FindAncestor<ButtonBase>(focused) is not null ||
                          FindAncestor<Slider>(focused) is not null ||
                          FindAncestor<ScrollBar>(focused) is not null ||
                          FindAncestor<TreeViewItem>(focused) is not null ||
                          FindAncestor<DataGrid>(focused) is not null ||
                          FindAncestor<ListBox>(focused) is not null ||
                          FindAncestor<TabItem>(focused) is not null ||
                          FindAncestor<MenuItem>(focused) is not null;

        var modifiers = OperatorShortcutModifiers.None;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            modifiers |= OperatorShortcutModifiers.Control;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            modifiers |= OperatorShortcutModifiers.Shift;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
        {
            modifiers |= OperatorShortcutModifiers.Alt;
        }

        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
        {
            modifiers |= OperatorShortcutModifiers.Windows;
        }

        var action = OperatorShortcutPolicy.Resolve(
            ToOperatorShortcutKey(e.Key == Key.System ? e.SystemKey : e.Key),
            modifiers, textEditing, nativeSpace, e.IsRepeat);
        if (action == OperatorShortcutAction.None)
        {
            return;
        }

        // Mark handled only when a shortcut was actually performed. Disabled
        // actions remain disabled through keyboard as they are through mouse.
        if (ExecuteOperatorShortcut(action))
        {
            e.Handled = true;
        }
    }

    private static OperatorShortcutKey ToOperatorShortcutKey(Key key) => key switch
    {
        Key.Space => OperatorShortcutKey.Space,
        Key.F1 => OperatorShortcutKey.F1,
        Key.F5 => OperatorShortcutKey.F5,
        Key.F6 => OperatorShortcutKey.F6,
        Key.F7 => OperatorShortcutKey.F7,
        Key.F8 => OperatorShortcutKey.F8,
        Key.F9 => OperatorShortcutKey.F9,
        Key.F11 => OperatorShortcutKey.F11,
        Key.Left => OperatorShortcutKey.Left,
        Key.Right => OperatorShortcutKey.Right,
        Key.O => OperatorShortcutKey.O,
        Key.S => OperatorShortcutKey.S,
        Key.N => OperatorShortcutKey.N,
        Key.F => OperatorShortcutKey.F,
        Key.L => OperatorShortcutKey.L,
        Key.M => OperatorShortcutKey.M,
        _ => OperatorShortcutKey.None
    };

    private void KeyboardShortcutsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var help = new OperatorShortcutHelpWindow { Owner = this };
        _ = help.ShowDialog();
    }

    private bool ExecuteOperatorShortcut(OperatorShortcutAction action)
    {
        if (action == OperatorShortcutAction.Help)
        {
            KeyboardShortcutsButton_OnClick(this, new RoutedEventArgs());
            return true;
        }

        if (!_licensingAllowsAccess || _controlsBusy || _isClosing)
        {
            return false;
        }

        var click = new RoutedEventArgs(ButtonBase.ClickEvent);
        switch (action)
        {
            case OperatorShortcutAction.PlayPause:
                if (PauseButton.IsEnabled)
                {
                    PauseButton_OnClick(PauseButton, click);
                    return true;
                }

                if (PlayButton.IsEnabled)
                {
                    PlayButton_OnClick(PlayButton, click);
                    return true;
                }

                return false;
            case OperatorShortcutAction.Play when PlayButton.IsEnabled:
                PlayButton_OnClick(PlayButton, click);
                return true;
            case OperatorShortcutAction.Pause when PauseButton.IsEnabled:
                PauseButton_OnClick(PauseButton, click);
                return true;
            case OperatorShortcutAction.Stop when StopButton.IsEnabled:
                StopButton_OnClick(StopButton, click);
                return true;
            case OperatorShortcutAction.Previous when PreviousMediaButton.IsEnabled:
                PreviousMediaButton_OnClick(PreviousMediaButton, click);
                return true;
            case OperatorShortcutAction.Next when NextMediaButton.IsEnabled:
                NextMediaButton_OnClick(NextMediaButton, click);
                return true;
            case OperatorShortcutAction.SeekBackwardFiveSeconds when BackFiveButton.IsEnabled:
                BackFiveButton_OnClick(BackFiveButton, click);
                return true;
            case OperatorShortcutAction.SeekForwardFiveSeconds when ForwardFiveButton.IsEnabled:
                ForwardFiveButton_OnClick(ForwardFiveButton, click);
                return true;
            case OperatorShortcutAction.ToggleFullscreenPreview when FullscreenPreviewButton.IsEnabled:
                FullscreenPreviewButton_OnClick(FullscreenPreviewButton, click);
                return true;
            case OperatorShortcutAction.OpenMedia when OpenButton.IsEnabled:
                OpenButton_OnClick(OpenButton, click);
                return true;
            case OperatorShortcutAction.ImportFolder when !_folderImportActive:
                AddFolderButton_OnClick(this, click);
                return true;
            case OperatorShortcutAction.SavePlaylist when !_folderImportActive:
                SavePlaylistPresetButton_OnClick(this, click);
                return true;
            case OperatorShortcutAction.NewPlaylist when !_folderImportActive:
                NewPlaylistButton_OnClick(this, click);
                return true;
            case OperatorShortcutAction.Search when MediaSearchBox.IsEnabled:
                _ = MediaSearchBox.Focus();
                MediaSearchBox.SelectAll();
                return true;
            case OperatorShortcutAction.FocusPlaylists when SavedPlaylistsList.IsEnabled:
                _ = SavedPlaylistsList.Focus();
                return true;
            case OperatorShortcutAction.ToggleMute when MuteButton.IsEnabled:
                MuteButton_OnClick(MuteButton, click);
                return true;
            default:
                return false;
        }
    }

    // Chrome buttons follow the normal Window close pipeline and cleanup.
    private void TitleBarMinimize_OnClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void TitleBarMaximize_OnClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void TitleBarClose_OnClick(object sender, RoutedEventArgs e) => Close();

    // GP-I01: every navigation control has a live destination, never a decorative placeholder.
    private void HeaderPlaylistButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_licensingAllowsAccess)
        {
            return;
        }

        _ = SavedPlaylistsList.Focus();
        StatusText.Text = "Biblioteca de playlists selecionada.";
    }

    private void HeaderMediaButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_licensingAllowsAccess && !_controlsBusy)
        {
            OpenButton_OnClick(sender, e);
        }
    }

    private async void HeaderSettingsButton_OnClick(object sender, RoutedEventArgs e) =>
        await OpenSettingsAsync(openUpdates: false, openLicense: false);

    private async void HeaderMonitorsButton_OnClick(object sender, RoutedEventArgs e) =>
        await OpenSettingsAsync(openUpdates: false, openLicense: false);

    private async void HeaderAboutButton_OnClick(object sender, RoutedEventArgs e) =>
        await OpenSettingsAsync(openUpdates: false, openLicense: false, openAbout: true);

    private async void ChangeAudioOutputButton_OnClick(object sender, RoutedEventArgs e) =>
        await OpenSettingsAsync(openUpdates: false, openLicense: false, openAudio: true);

    private async void ConfigureScreensButton_OnClick(object sender, RoutedEventArgs e) =>
        await OpenSettingsAsync(openUpdates: false, openLicense: false);

    private async void UpdateAvailableButton_OnClick(object sender, RoutedEventArgs e) =>
        await OpenSettingsAsync(openUpdates: true, openLicense: false);

    private async void LicenseStatusButton_OnClick(object sender, RoutedEventArgs e) =>
        await OpenSettingsAsync(openUpdates: false, openLicense: true);

    internal Task OpenLicenseSettingsAsync() =>
        OpenSettingsAsync(openUpdates: false, openLicense: true);

    private async Task OpenSettingsAsync(
        bool openUpdates, bool openLicense, bool openAbout = false, bool openAudio = false)
    {
        var app = Application.Current as App;
        if (app is not
            {
                UpdateManager: { } updateManager,
                WindowsIntegration: { } windowsIntegration,
                Licensing: { } licensing
            })
        {
            StatusText.Text = "Os serviços do GuiaPlay ainda não estão disponíveis.";
            return;
        }
        var currentSettings = app.Settings;
        var playbackActive = _coordinator.IsActive || _youtubeWindow is not null;
        var names = currentSettings.MonitorNames;
        // A license-only dialog MUST NOT instantiate the native playback engine.
        // That previously defeated deferred loading and crashed some PCs while
        // showing the activation screen, before any local video was requested.
        if (_engineInstance is not null)
        {
            RefreshAudioDevices(force: true);
        }

        var equalizerCatalog = _engineInstance?.EqualizerCatalog ??
            new LibVlcEqualizerCatalog(
                false, [], [],
                "O equalizador ficara disponivel apos o carregamento de uma midia local.");
        var dialog = new ScreenConfigurationWindow(
            _monitors,
            _operatorMonitor?.Id,
            currentSettings,
            _audioDevices,
            equalizerCatalog,
            updateManager,
            windowsIntegration,
            licensing,
            openUpdates,
            openLicense,
            playbackActive,
            _operatorRecoverySettingsAllowed,
            openAbout,
            openAudio)
        { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } result)
        {
            ApplyLicensingState(licensing.Current);
            RefreshAudioPanel();
            StatusText.Text = openLicense
                ? licensing.Current.Message ?? (licensing.Current.HasAccess ? "Estado da licença atualizado." : "Ativação necessária para usar o GuiaPlay.")
                : "Configurações fechadas; nenhuma preferência pendente foi salva.";
            return;
        }

        var recoveringOperatorDuringPlayback = _coordinator.IsActive && _operatorRecoverySettingsAllowed;
        if (playbackActive && !recoveringOperatorDuringPlayback)
        {
            if (!app.UpdateSettings(current => current with
            {
                Appearance = result.Appearance,
                AccentColor = result.AccentColor,
                Equalizer = result.Equalizer,
                CheckUpdatesAutomatically = result.CheckUpdatesAutomatically,
                InstallUpdatesAutomatically = result.InstallUpdatesAutomatically
            }, out var activeSaveError))
            {
                ShowSettingsSaveFailure(activeSaveError);
                return;
            }

            app.ApplyAppearance(result.Appearance, result.AccentColor, persist: false);
            if (_engineInstance is { } activeEngine)
            {
                await activeEngine.ApplyEqualizerAsync(result.Equalizer);
            }
            StatusText.Text = "Aparência, equalizador e atualizações salvos; reprodução preservada.";
            RefreshAudioPanel();
            return;
        }

        var connectedPersistentIds = _monitors
            .Select(monitor => monitor.PersistenceKey)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mergedNames = new Dictionary<string, string>(names, StringComparer.OrdinalIgnoreCase);
        foreach (var id in connectedPersistentIds)
        {
            mergedNames.Remove(id);
        }

        foreach (var pair in result.PersistentNames)
        {
            mergedNames[pair.Key] = pair.Value;
        }

        var nextOperator = result.OperatorMonitor;
        var nextExplicitOutputIds = new HashSet<string>(_explicitOutputIds, StringComparer.OrdinalIgnoreCase);
        if (nextOperator.PersistenceKey is { } nextOperatorKey)
        {
            nextExplicitOutputIds.Remove(nextOperatorKey);
        }

        if (!app.UpdateSettings(current => current with
        {
            OperatorMonitorId = nextOperator.PersistenceKey,
            MonitorNames = mergedNames,
            SelectedOutputIds = nextExplicitOutputIds,
            Appearance = result.Appearance,
            AccentColor = result.AccentColor,
            AudioOutput = result.AudioOutput,
            Equalizer = result.Equalizer,
            CheckUpdatesAutomatically = result.CheckUpdatesAutomatically,
            InstallUpdatesAutomatically = result.InstallUpdatesAutomatically
        }, out var error))
        {
            ShowSettingsSaveFailure(error);
            return;
        }

        _operatorMonitor = nextOperator;
        _operatorRequiresConfiguration = false;
        _operatorRecoverySettingsAllowed = false;
        if (_operatorMonitor.PersistenceKey is { } operatorKey)
        {
            _selectedOutputIdsThisSession.Remove(operatorKey);
        }

        _explicitOutputIds.Clear();
        foreach (var outputId in nextExplicitOutputIds)
        {
            _explicitOutputIds.Add(outputId);
        }

        if (_outputWindows.Remove(_operatorMonitor.Id, out var operatorOutput))
        {
            operatorOutput.Close();
        }

        app.ApplyAppearance(result.Appearance, result.AccentColor, persist: false);

        if (_engineInstance is { } engineForSettings)
        {
            await engineForSettings.ApplyEqualizerAsync(result.Equalizer);
        }

        if (!recoveringOperatorDuringPlayback)
        {
            _audioReconfigurationRequired = false;
            var audioAvailability = AudioPreferenceResolver.Resolve(result.AudioOutput, _audioDevices);
            _audioPreferenceAvailable = AudioOutputRuntimePolicy.CanApplySelection(audioAvailability);
            if (_coordinator.MediaPath is not null && _audioPreferenceAvailable)
            {
                try
                {
                    await _engine.ApplyAudioOutputAsync(result.AudioOutput);
                    _engine.Volume = (int)VolumeSlider.Value;
                    _engine.Muted = MuteButton.Tag as bool? ?? false;
                }
                catch (Exception exception)
                {
                    FileLogger.Error("Falha ao aplicar a saída de áudio.", exception);
                    _audioPreferenceAvailable = false;
                }
            }
        }

        ApplyMonitorNames();
        RebuildOutputChoices();
        UpdateOperatorIndicator();
        OperatorStatusText.Text = _operatorMonitor.PersistenceKey is null
            ? "Operador escolhido para esta sessão; a identidade desta tela é ambígua e não será restaurada automaticamente."
            : "O operador nunca aparece entre as saídas públicas.";
        WindowPlacement.PlaceOperatorPanel(this, _operatorMonitor);
        StatusText.Text = _audioPreferenceAvailable || recoveringOperatorDuringPlayback
            ? "Configurações salvas e aplicadas."
            : "Configurações salvas; a preferência de áudio indisponível foi preservada sem fallback.";
        RefreshAudioPanel();
        RefreshStageDashboard();
    }

    private void UpdateManager_OnStateChanged(object? sender, EventArgs e) => Dispatch(RefreshUpdateIndicator);

    private void RefreshUpdateIndicator()
    {
        var status = (Application.Current as App)?.UpdateManager?.LastResult?.Status;
        UpdateAvailableButton.Visibility = status is not null && UpdateIndicatorState.IsVisible(status.Value)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        if (Application.Current is not App { UpdateManager: { } manager } app) return;
        FileLogger.Info("Update startup: scheduled after window load (1.5 s)");
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        await manager.CheckOnStartupAsync();
        RefreshUpdateIndicator();
        if (UpdateInstallationPolicy.CanAutoInstall(
                app.Settings.InstallUpdatesAutomatically,
                manager.LastResult?.Status == UpdateCheckStatus.UpdateAvailable,
                _coordinator.IsActive,
                Environment.ProcessPath is { } path && ManagedInstallationDetector.Detect(path) is not null))
        {
            await TryAutomaticInstallationAsync();
        }
    }

    private async Task TryAutomaticInstallationAsync()
    {
        if (_autoInstallInProgress || _coordinator.IsActive || _youtubeWindow is not null ||
            Application.Current is not App { UpdateManager: { } manager } app ||
            !UpdateInstallationPolicy.CanAutoInstall(
                app.Settings.InstallUpdatesAutomatically,
                manager.LastResult?.Status == UpdateCheckStatus.UpdateAvailable,
                playbackActive: false,
                Environment.ProcessPath is { } path && ManagedInstallationDetector.Detect(path) is not null))
        {
            return;
        }

        _autoInstallInProgress = true;
        var result = await manager.PrepareAndLaunchInstallerAsync();
        if (result.Launched)
        {
            Application.Current.Shutdown();
        }
        else
        {
            _autoInstallInProgress = false;
            FileLogger.Info($"Instalação automática adiada: {result.Message}");
        }
    }

    private void MuteButton_OnClick(object sender, RoutedEventArgs e)
    {
        var state = MediaControlBehavior.ToggleMute((int)VolumeSlider.Value, MuteButton.Tag as bool? ?? false);
        MuteButton.Tag = state.Muted;
        UpdateMuteVisual(state.Muted);
        if (_engineInstance is { } nativeEngine)
        {
            nativeEngine.Muted = state.Muted;
        }

        _youtubeWindow?.Command(state.Muted ? "mute" : "unmute");
        VolumeText.Text = state.Muted ? "mudo" : $"{state.Volume}%";
        ScheduleSettingsSave();
    }

    // The reference screenshot's artwork is illustrative; the UI never substitutes
    // another file's cover. The placeholder uses the real media kind until a
    // verified thumbnail is available from the isolated metadata pipeline.
    private void UpdateTransportArtwork(MediaKind kind, string? path)
    {
        var matchingRow = string.IsNullOrWhiteSpace(path)
            ? null
            : _mediaRows.FirstOrDefault(row =>
                string.Equals(row.FilePath, path, StringComparison.OrdinalIgnoreCase));
        TransportThumbnailImage.Source = matchingRow?.Thumbnail;
        var showPlaceholder = matchingRow?.Thumbnail is null;
        TransportAudioPlaceholderIcon.Visibility = showPlaceholder && kind == MediaKind.Audio
            ? Visibility.Visible : Visibility.Collapsed;
        TransportVideoPlaceholderIcon.Visibility = showPlaceholder && kind != MediaKind.Audio
            ? Visibility.Visible : Visibility.Collapsed;
    }

    // GP-I13: user-supplied SVG levels; one indicator for local and YouTube audio.
    // 0% or explicit mute = mute; 1-80% = down; 81-100% = up.
    private void UpdateMuteVisual(bool muted)
    {
        var value = VolumeSlider?.Value ?? 0;
        var silent = muted || value <= 0;
        SpeakerIcon.Visibility = !silent && value > 80 ? Visibility.Visible : Visibility.Collapsed;
        SpeakerLowIcon.Visibility = !silent && value <= 80 ? Visibility.Visible : Visibility.Collapsed;
        SpeakerMuteIcon.Visibility = silent ? Visibility.Visible : Visibility.Collapsed;
        MuteButtonText.Text = silent ? "Ativar som" : "Mudo";
        MuteButton.ToolTip = silent ? "Ativar som" : $"Silenciar áudio ({Math.Round(value)}%)";
    }

    private void UpdateOperatorIndicator()
    {
        var savedOperatorId = (Application.Current as App)?.Settings.OperatorMonitorId;
        var hasSavedOperator = !string.IsNullOrWhiteSpace(savedOperatorId);
        var savedOperatorConnected = hasSavedOperator && _monitors.Any(monitor =>
            string.Equals(monitor.PersistenceKey, savedOperatorId, StringComparison.OrdinalIgnoreCase));
        var state = OperatorIndicatorState.Create(
            _operatorMonitor?.ShortLabel,
            hasSavedOperator,
            savedOperatorConnected,
            _operatorRequiresConfiguration);
        OperatorIndicatorText.Text = state.Text;
        OperatorPanelText.Text = state.Text;
        OperatorPanelText.ToolTip = state.Text;
        OperatorStatusText.Text = _operatorMonitor is null
            ? "Nenhuma tela do operador conectada. Abra Monitores para configurar."
            : _operatorRequiresConfiguration
                ? "Tela provisória: configure e salve o operador antes de enviar vídeo ao público."
                : "Esta tela é exclusiva do operador e não pode ser selecionada como saída pública.";
        OperatorIndicatorBorder.ToolTip = state.IsWarning
            ? "A tela do operador salva está desconectada ou precisa ser configurada. Abra Configurações."
            : "Tela configurada como operador";
        OperatorIndicatorText.SetResourceReference(
            TextBlock.ForegroundProperty,
            state.IsWarning ? "SystemFillColorCautionBrush" : "TextFillColorPrimaryBrush");
        OperatorIndicatorBorder.SetResourceReference(
            Border.BorderBrushProperty,
            state.IsWarning ? "SystemFillColorCautionBrush" : "ControlStrokeColorDefaultBrush");
    }

    private void VolumeSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var state = MediaControlBehavior.SetVolume((int)e.NewValue, MuteButton?.Tag as bool? ?? false);
        if (VolumeText is not null)
        {
            VolumeText.Text = state.Muted ? "mudo" : $"{state.Volume}%";
        }

        if (_engineInstance is { } nativeEngine)
        {
            nativeEngine.Volume = state.Volume;
        }

        _youtubeWindow?.Command("volume", state.Volume);
        UpdateMuteVisual(state.Muted);
        if (_settingsInitialized)
        {
            ScheduleSettingsSave();
        }
    }

    private void VolumeSlider_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var adjustment = MediaControlBehavior.AdjustVolume(
            (int)VolumeSlider.Value,
            MuteButton.Tag as bool? ?? false,
            e.Delta);
        VolumeSlider.Value = adjustment.Volume;
        e.Handled = true;
    }

    private void ProgressSlider_OnDragStarted(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider slider || !slider.IsEnabled || !CanSeekNow())
        {
            e.Handled = true;
            return;
        }

        if (IsWithinThumb(e.OriginalSource as DependencyObject))
        {
            _isDraggingProgress = true;
            _draggingProgressSlider = slider;
            return;
        }

        _ = TrySeek(MediaControlBehavior.PositionFromPoint(
            e.GetPosition(slider).X, slider.ActualWidth,
            _youtubeWindow is null ? _engine.Length :
                _youtubeSnapshot?.DurationMilliseconds ?? 0));
        e.Handled = true;
    }

    private void ProgressSlider_OnDragCompleted(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingProgress || !ReferenceEquals(sender, _draggingProgressSlider))
        {
            return;
        }

        var position = (long)_draggingProgressSlider.Value;
        _draggingProgressSlider = null;
        _isDraggingProgress = false;
        _ = TrySeek(position);
    }

    private void ProgressSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isDraggingProgress && ReferenceEquals(sender, _draggingProgressSlider))
        {
            UpdateTimeLabels((long)e.NewValue, Math.Max(0,
                _youtubeWindow is null ? _engine.Length :
                    _youtubeSnapshot?.DurationMilliseconds ?? 0));
        }
    }

    private void ProgressSlider_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is Slider slider && slider.IsEnabled &&
            TrySeek(MediaControlBehavior.AdjustPosition(
                _youtubeWindow is null ? _engine.Time :
                    _youtubeSnapshot?.ElapsedMilliseconds ?? 0,
                _youtubeWindow is null ? _engine.Length :
                    _youtubeSnapshot?.DurationMilliseconds ?? 0, e.Delta)))
        {
            e.Handled = true;
        }
    }

    private bool TrySeek(long targetMilliseconds)
    {
        if (!CanSeekNow())
        {
            return false;
        }

        var duration = _youtubeWindow is null
            ? _engine.Length : _youtubeSnapshot?.DurationMilliseconds ?? 0;
        var target = Math.Clamp(targetMilliseconds, 0, duration);
        if (_youtubeWindow is { } youtube)
        {
            youtube.Command("seek", target / 1000d);
            ProgressSlider.Maximum = Math.Max(1, duration);
            ProgressSlider.Value = target;
            UpdateTimeLabels(target, duration);
            return true;
        }
        ProgressSlider.Maximum = Math.Max(1, duration);
        ProgressSlider.Value = target;
        _engine.Seek(target);
        UpdateTimeLabels(target, duration);
        return true;
    }

    private static bool IsWithinThumb(DependencyObject? source)
    {
        for (var current = source; current is not null; current = current is Visual
                 ? VisualTreeHelper.GetParent(current)
                 : LogicalTreeHelper.GetParent(current))
        {
            if (current is Thumb)
            {
                return true;
            }
        }

        return false;
    }

    private void Engine_OnFrameReady(long generation)
    {
        if (_isClosing || !_coordinator.AcceptsCallback(PlaybackCallbackKind.FrameReady, generation))
        {
            return;
        }

        _diagnostics.RecordFrameReceived();
        Interlocked.Increment(ref _frameCallbacks);
        Volatile.Write(ref _frameGeneration, generation);
        if (Interlocked.Exchange(ref _renderPending, 1) == 0)
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Render, _renderFrameCallback);
        }
    }

    private void RenderLatestFrame()
    {
        Interlocked.Exchange(ref _renderPending, 0);
        var generation = Volatile.Read(ref _frameGeneration);
        if (!_coordinator.AcceptsCallback(PlaybackCallbackKind.FrameReady, generation) ||
            !_engine.TryAcquireLatestFrame(generation, out var frame))
        {
            return;
        }

        using (frame)
        {
            var stageNeedsRefresh = false;
            if (_bitmap is null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height)
            {
                stageNeedsRefresh = true;
                _bitmap = new WriteableBitmap((int)frame.Width, (int)frame.Height, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null);
                PreviewImage.Source = _bitmap;
                foreach (var window in _outputWindows.Values)
                {
                    window.SetSource(_bitmap);
                }

                PreviewEmptyState.Visibility = Visibility.Collapsed;
            }

            var copyStart = Stopwatch.GetTimestamp();
            _bitmap.WritePixels(
                new Int32Rect(0, 0, (int)frame.Width, (int)frame.Height),
                frame.Pointer,
                checked((int)(frame.Pitch * frame.Height)),
                (int)frame.Pitch);
            var copyElapsed = Stopwatch.GetElapsedTime(copyStart);
            Interlocked.Add(ref _copyTicks, copyElapsed.Ticks * Stopwatch.Frequency / TimeSpan.TicksPerSecond);
            Interlocked.Increment(ref _framesRendered);
            _diagnostics.RecordFrameRendered(copyElapsed);
            if (_outputsAwaitingFirstFrame)
            {
                foreach (var window in _outputWindows.Values)
                {
                    window.SetSource(_bitmap);
                }

                _outputsAwaitingFirstFrame = false;
                stageNeedsRefresh = true;
            }

            if (stageNeedsRefresh)
            {
                RefreshStageDashboard();
            }
        }
    }

    private void Engine_OnPlaying(long generation)
    {
        if (!_coordinator.MarkPlaying(generation))
        {
            if (generation != _coordinator.Generation || _coordinator.Status != PlaybackStatus.Playing)
            {
                return;
            }
        }

        StatusText.Text = "Reproduzindo";
        PlayButtonText.Text = "Reproduzir";
        RefreshPlaybackControls();
    }

    private void Engine_OnPaused(long generation)
    {
        if (!_coordinator.AcceptsCallback(PlaybackCallbackKind.Paused, generation))
        {
            return;
        }

        StatusText.Text = "Pausado";
    }

    private async void Engine_OnEndReached(long generation)
    {
        if (_suppressAutoAdvance)
        {
            return; // Explicit StopAsync is never interpreted as natural playlist end.
        }

        var transition = _coordinator.NaturalEnd(generation);
        if (!transition.Accepted)
        {
            return;
        }

        CloseOutputWindows();
        StatusText.Text = "Fim da mídia; saídas de vídeo fechadas automaticamente";
        PlayButtonText.Text = "Reproduzir novamente";
        SetConfigurationControlsEnabled(true);
        RefreshPlaybackControls();
        ProgressSlider.Value = ProgressSlider.Maximum;
        LogPlaybackMetrics($"Fim natural da geração {generation}");
        LogDiagnosticSnapshot("Fim natural");

        // Never chain playback in Manual. Autoplay is explicit and never skips missing
        // files, authorization failures or operator monitor/device protections.
        var advanced = await TryAutomaticAdvanceAsync(generation);
        if (!advanced && !_isClosing && !_coordinator.IsActive)
        {
            _ = TryAutomaticInstallationAsync();
        }
    }

    private void Engine_OnError(long generation, string message)
    {
        var transition = _coordinator.PlaybackFailed(generation);
        if (!transition.Accepted)
        {
            return;
        }

        CloseOutputWindows();
        StatusText.Text = "Erro de reprodução; o aplicativo continua disponível";
        SetConfigurationControlsEnabled(true);
        RefreshPlaybackControls();
        FileLogger.Error($"Erro do LibVLC na geração {generation}: {message}");
        LogDiagnosticSnapshot("Erro de reprodução");
    }

    private void LogPlaybackMetrics(string reason)
    {
        var callbacks = Interlocked.Exchange(ref _frameCallbacks, 0);
        var rendered = Interlocked.Exchange(ref _framesRendered, 0);
        var ticks = Interlocked.Exchange(ref _copyTicks, 0);
        var averageCopyMs = rendered == 0 ? 0 : ticks * 1000d / Stopwatch.Frequency / rendered;
        FileLogger.Info($"{reason}; quadros recebidos={callbacks}; apresentados={rendered}; coalescidos={Math.Max(0, callbacks - rendered)}; cópia média={averageCopyMs:F3} ms.");
    }

    private void UpdateTime(long generation, long time)
    {
        if (!_coordinator.AcceptsCallback(PlaybackCallbackKind.TimeChanged, generation))
        {
            return;
        }

        var length = Math.Max(0, _engine.Length);
        if (!_isDraggingProgress)
        {
            ProgressSlider.Maximum = Math.Max(1, length);
            ProgressSlider.Value = Math.Clamp(time, 0, length);
        }

        UpdateTimeLabels(time, length);
        RefreshSeekAvailability();
    }

    private void UpdateLength(long generation, long length)
    {
        if (!_coordinator.AcceptsCallback(PlaybackCallbackKind.LengthChanged, generation))
        {
            return;
        }

        ProgressSlider.Maximum = Math.Max(1, length);
        UpdateTimeLabels(_engine.Time, length);
        RefreshSeekAvailability();
    }

    private void UpdateTimeLabels(long elapsed, long total)
    {
        elapsed = Math.Max(0, elapsed);
        total = Math.Max(0, total);
        ElapsedText.Text = FormatTime(elapsed);
        RemainingText.Text = $"-{FormatTime(Math.Max(0, total - elapsed))} / {FormatTime(total)}";
    }

    private static string FormatTime(long milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(milliseconds);
        return time.TotalHours >= 1 ? time.ToString(@"hh\:mm\:ss") : time.ToString(@"mm\:ss");
    }

    private void PrepareOutputWindows(IEnumerable<MonitorInfo> monitors)
    {
        CloseOutputWindows();
        foreach (var monitor in monitors)
        {
            var window = new OutputWindow(monitor, null);
            _outputWindows.Add(monitor.Id, window);
            window.Show();
        }

        _outputsAwaitingFirstFrame = _outputWindows.Count > 0;
        UpdateActiveOutputs();
    }

    private void CloseOutputWindows()
    {
        foreach (var window in _outputWindows.Values)
        {
            window.Close();
        }

        _outputWindows.Clear();
        _outputsAwaitingFirstFrame = false;
        UpdateActiveOutputs();
    }

    private void UpdateActiveOutputs()
    {
        ActiveOutputsText.Text = _outputWindows.Count == 0
            ? "Nenhuma"
            : string.Join(Environment.NewLine, _outputWindows.Values.Select(window => window.Monitor.ShortLabel));
        RefreshStageDashboard();
    }

    private void RefreshStageDashboard()
    {
        if (ProgramImage is null || ProgramEmptyState is null)
        {
            return;
        }

        if (_youtubeWindow is { } youtube)
        {
            var status = _youtubeSnapshot?.State ?? "CARREGANDO";
            PreviewLiveStateText.Text = "YOUTUBE " + status;
            ProgramStatusBadgeText.Text = status;
            ProgramPlaceholderText.Text = "YouTube · player oficial na tela pública";
            ProgramDetailsText.Text = "Sem espelho de vídeo: a API não fornece quadros ao operador.";
            YouTubeStatusHintText.Visibility = Visibility.Visible;
            YouTubeStatusHintText.Text = _youtubeSnapshot?.Message ??
                "Controle pelo GuiaPlay. O player visível permanece na saída pública.";
            ProgramImage.Source = null;
            ProgramImage.Visibility = Visibility.Collapsed;
            ProgramEmptyState.Visibility = Visibility.Visible;
            ActiveOutputsText.Text = youtube.Monitor.ShortLabel + " (YouTube)";
            OutputSelectedCountText.Text = $"{SelectedOutputCount()} selecionada(s)";
            NoPublicMonitorsText.Visibility = Visibility.Collapsed;
            SidebarOutputsList.IsEnabled = false;
            OutputsList.IsEnabled = false;
            return;
        }

        YouTubeStatusHintText.Visibility = Visibility.Collapsed;
        // The program preview mirrors the frame used by the actual OutputWindow(s).
        // It never guesses what an external display physically shows.
        var selected = _outputChoices.Count(choice => choice.IsSelected);
        var active = _outputWindows.Count;
        var publicFrameReady = _bitmap is not null && !_outputsAwaitingFirstFrame &&
                               PlaybackRouting.UsesVideoOutputs(_coordinator.MediaKind);
        var presentation = ProgramStagePresentationPolicy.Resolve(
            _coordinator.Status, _coordinator.MediaKind, selected, active, publicFrameReady);
        PreviewLiveStateText.Text = presentation.OperatorBadge;
        ProgramStatusBadgeText.Text = presentation.Badge;
        ProgramPlaceholderText.Text = presentation.Message;
        ProgramDetailsText.Text = presentation.Hint;
        ProgramImage.Source = presentation.ShowFrame ? _bitmap : null;
        ProgramImage.Visibility = presentation.ShowFrame ? Visibility.Visible : Visibility.Collapsed;
        ProgramEmptyState.Visibility = presentation.ShowFrame ? Visibility.Collapsed : Visibility.Visible;

        OutputSelectedCountText.Text = $"{selected} selecionada(s)";
        NoPublicMonitorsText.Visibility = _outputChoices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        // Selecting a different set during playback could make the chips disagree
        // with already-open windows. Changes take effect only before starting.
        var editable = _licensingAllowsAccess && !_controlsBusy && !_coordinator.IsActive;
        SidebarOutputsList.IsEnabled = editable;
        OutputsList.IsEnabled = editable;
        SidebarOutputsList.ToolTip = editable
            ? "Escolha as telas para a próxima reprodução pública."
            : "Para trocar as telas públicas, pare a reprodução.";
    }

    private void RefreshAudioPanel()
    {
        if (AudioDeviceNameText is null)
        {
            return;
        }

        if (_youtubeWindow is not null)
        {
            AudioDeviceNameText.Text = "YouTube / padrão do Windows";
            AudioAvailabilityText.Text = "WEBVIEW2";
            AudioWarningText.Text =
                "Dispositivo escolhido no LibVLC não se aplica ao YouTube. " +
                "Selecione a saída do navegador pelo mixer de volume do Windows.";
            return;
        }

        var preference = (Application.Current as App)?.Settings.AudioOutput ?? AudioOutputPreference.Default;
        AudioDeviceNameText.Text = preference.IsExplicit
            ? preference.DisplayName ?? preference.DeviceId ?? "Dispositivo selecionado"
            : "Padrão do Windows";
        AudioDeviceNameText.ToolTip = preference.IsExplicit
            ? $"Saída configurada: {preference.Module} / {preference.DeviceId}"
            : "O Windows escolhe o dispositivo padrão quando a reprodução começa.";

        AudioAvailabilityText.Text = _audioReconfigurationRequired
            ? "RECONFIGURAR"
            : _audioPreferenceAvailable
                ? "DISPONÍVEL"
                : "INDISPONÍVEL";
        AudioAvailabilityText.ToolTip = AudioWarningText.Text;
    }

    private void RefreshConnectedDevices(bool causedByDisplayChange)
    {
        RefreshMonitors(causedByDisplayChange);
        RefreshAudioDevices(force: causedByDisplayChange);
    }

    private void RefreshMonitors(bool causedByDisplayChange)
    {
        var previousMonitors = _monitors;
        var current = MonitorService.GetActiveMonitors();
        if (_youtubeWindow is { } activeYouTube &&
            MonitorTopologyReconciler.FindCurrent(activeYouTube.Monitor, current) is null)
        {
            CloseYouTubeWindow();
            StatusText.Text = "Tela pública do YouTube desconectada; reprodução interrompida.";
        }
        else if (_youtubeWindow is { } connectedYouTube &&
                 MonitorTopologyReconciler.FindCurrent(connectedYouTube.Monitor, current) is { } routedMonitor)
        {
            connectedYouTube.Reposition(routedMonitor);
        }
        var names = (Application.Current as App)?.Settings.MonitorNames ?? new Dictionary<string, string>();
        current = current.Select(monitor => monitor with
        {
            CustomName = monitor.PersistenceKey is { } key && names.TryGetValue(key, out var name) ? name : null
        }).ToArray();
        var currentIds = current.Select(monitor => monitor.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var disconnectedMonitors = previousMonitors.Where(monitor => MonitorTopologyReconciler.FindCurrent(monitor, current) is null).ToArray();
        foreach (var monitor in disconnectedMonitors)
        {
            _selectedOutputIdsThisSession.Remove(SelectionKey(monitor));
            if (monitor.PersistenceKey is { } persistentId)
            {
                _reconnectAuthorization.MarkDisconnected(persistentId);
            }
        }

        var currentOperatorMatch = _operatorMonitor is null ? null : MonitorTopologyReconciler.FindCurrent(_operatorMonitor, current);
        var operatorWasLost = _operatorMonitor is not null && currentOperatorMatch is null;
        var hadPublicOutputs = _outputWindows.Count > 0;
        var outputReconciliations = _outputWindows.Values
            .Select(window => (Window: window, Current: MonitorTopologyReconciler.FindCurrent(window.Monitor, current)))
            .ToArray();
        var disconnectedOutputs = outputReconciliations
            .Where(reconciliation => reconciliation.Current is null)
            .Select(reconciliation => reconciliation.Window)
            .ToArray();
        var disconnectedOutputLabels = disconnectedOutputs.Select(window => window.Monitor.ShortLabel).ToArray();

        foreach (var identifier in _identifierWindows.Where(window => !currentIds.Contains(window.MonitorId)).ToArray())
        {
            identifier.Close();
            _identifierWindows.Remove(identifier);
        }

        foreach (var window in disconnectedOutputs)
        {
            window.Close();
        }

        // Rebuild keys only after every old entry has been removed. GDI session IDs may swap
        // between two still-connected physical monitors; renaming entries in-place would then
        // overwrite one OutputWindow and leave the other alive but untracked.
        _outputWindows.Clear();
        foreach (var (window, matchedMonitor) in outputReconciliations.Where(item => item.Current is not null))
        {
            var monitor = matchedMonitor!;
            if (!_outputWindows.TryAdd(monitor.Id, window))
            {
                FileLogger.Error($"Topologia de monitores produziu o ID de sessão duplicado '{monitor.Id}'; a saída duplicada foi fechada.");
                window.Close();
                continue;
            }

            window.Reposition(monitor);
            window.Show();
        }

        _monitors = current;
        if (!_startupMonitorPreferencesApplied)
        {
            ApplyStartupMonitorPreferences();
            _startupMonitorPreferencesApplied = true;
        }
        else if (operatorWasLost)
        {
            HandleOperatorDisconnection();
        }
        else if (_operatorMonitor is not null)
        {
            _operatorMonitor = currentOperatorMatch;
        }

        UpdateOperatorIndicator();
        RebuildOutputChoices();

        UpdateMonitorWarning();
        UpdateActiveOutputs();

        if (operatorWasLost)
        {
            var operatorRecoveryMessage = _operatorRecoverySettingsAllowed
                ? "A tela do operador foi desconectada. A reprodução foi pausada e o painel foi movido para uma tela disponível. " +
                  "Abra Configurar telas e escolha novamente o operador; a reprodução não será retomada automaticamente."
                : _coordinator.IsActive
                    ? "A tela do operador foi desconectada. O painel foi movido para uma tela disponível. " +
                      "Pare a reprodução antes de alterar a configuração de telas."
                    : "A tela do operador foi desconectada. O painel foi movido para uma tela disponível. " +
                      "Abra Configurar telas e escolha novamente o operador.";
            FileLogger.Info(_operatorRecoverySettingsAllowed
                ? "A tela do operador foi desconectada; reprodução pública pausada até nova configuração."
                : "A tela do operador foi desconectada; nova configuração necessária.");
            MessageBox.Show(
                this,
                operatorRecoveryMessage,
                "Tela do operador desconectada",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        else if (disconnectedOutputs.Length > 0)
        {
            FileLogger.Info($"Saídas desconectadas: {string.Join(", ", disconnectedOutputLabels)}.");
            var transition = hadPublicOutputs ? _coordinator.PublicOutputsChanged(_outputWindows.Count) : default;
            if (transition.PauseEngine)
            {
                PauseForRequiredReconfiguration();
            }

            MessageBox.Show(
                this,
                $"Uma saída foi desconectada ({string.Join(", ", disconnectedOutputLabels)}). " +
                (_outputWindows.Count == 0 ? "A reprodução foi pausada porque não restou saída pública." : "As demais saídas continuam."),
                "Tela desconectada",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        else if (causedByDisplayChange)
        {
            FileLogger.Info("Configuração de monitores alterada; janelas válidas foram reposicionadas.");
        }
    }

    private void ApplyStartupMonitorPreferences()
    {
        var settings = (Application.Current as App)?.Settings ?? AppSettings.Default;
        var connected = _monitors.Select(monitor => new ConnectedDisplay(monitor.Id, monitor.PersistenceKey, monitor.IsPrimary)).ToArray();
        var operatorMatch = DisplayPreferenceResolver.Match(settings.OperatorMonitorId, connected);
        if (operatorMatch.Kind == SavedDisplayMatchKind.Exact)
        {
            _operatorMonitor = _monitors.First(monitor => string.Equals(monitor.Id, operatorMatch.Display!.SessionId, StringComparison.OrdinalIgnoreCase));
            _operatorRequiresConfiguration = false;
            OperatorStatusText.Text = "O operador nunca aparece entre as saídas públicas.";
        }
        else
        {
            _operatorMonitor = _monitors.FirstOrDefault(monitor => monitor.IsPrimary) ?? _monitors.FirstOrDefault();
            _operatorRequiresConfiguration = true;
            OperatorStatusText.Text = settings.OperatorMonitorId is null
                ? "Escolha e salve a tela do operador antes de iniciar uma reprodução pública."
                : "A tela do operador salva está ausente ou ambígua. Escolha-a novamente; a preferência salva foi preservada.";
            StatusText.Text = "Configuração do operador necessária antes da reprodução pública.";
        }

        var resolvedOutputs = DisplayPreferenceResolver.ResolveStartupOutputs(
            settings.SelectedOutputIds,
            connected,
            _operatorMonitor?.PersistenceKey);
        foreach (var id in resolvedOutputs.Where(_reconnectAuthorization.MayRestoreSavedSelection))
        {
            _selectedOutputIdsThisSession.Add(id);
        }
    }

    private void HandleOperatorDisconnection()
    {
        CloseYouTubeWindow();
        _fullscreenPreviewWindow?.Close();
        _operatorRequiresConfiguration = true;
        _operatorRecoverySettingsAllowed = false;
        _operatorMonitor = _monitors.FirstOrDefault(monitor => monitor.IsPrimary) ?? _monitors.FirstOrDefault();
        OperatorStatusText.Text = "Operador provisório: escolha e salve novamente antes de continuar em telas públicas.";
        if (_operatorMonitor is not null)
        {
            _selectedOutputIdsThisSession.Remove(SelectionKey(_operatorMonitor));
            if (_outputWindows.Remove(_operatorMonitor.Id, out var provisionalOutput))
            {
                provisionalOutput.Close();
            }

            WindowPlacement.PlaceOperatorPanel(this, _operatorMonitor);
        }

        var transition = PlaybackRouting.UsesVideoOutputs(_coordinator.MediaKind)
            ? _coordinator.RequiredDeviceLost(_coordinator.Generation)
            : default;
        if (transition.PauseEngine)
        {
            _operatorRecoverySettingsAllowed = true;
            PauseForRequiredReconfiguration();
        }
    }

    private void RebuildOutputChoices()
    {
        _updatingMonitorChoices = true;
        try
        {
            _outputChoices.Clear();
            foreach (var monitor in _monitors.Where(monitor => !string.Equals(monitor.Id, _operatorMonitor?.Id, StringComparison.OrdinalIgnoreCase)))
            {
                var choice = new MonitorChoice
                {
                    Monitor = monitor,
                    IsSelected = _selectedOutputIdsThisSession.Contains(SelectionKey(monitor))
                };
                choice.PropertyChanged += OutputChoice_OnPropertyChanged;
                _outputChoices.Add(choice);
            }
        }
        finally
        {
            _updatingMonitorChoices = false;
        }

        RefreshPlaybackControls();
        RefreshStageDashboard();
    }

    private void OutputChoice_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_updatingMonitorChoices || e.PropertyName != nameof(MonitorChoice.IsSelected) || sender is not MonitorChoice choice)
        {
            return;
        }

        var key = SelectionKey(choice.Monitor);
        if (choice.IsSelected)
        {
            _selectedOutputIdsThisSession.Add(key);
            if (choice.Monitor.PersistenceKey is { } persistentId)
            {
                _explicitOutputIds.Add(persistentId);
                _reconnectAuthorization.ExplicitlyAuthorize(persistentId);
            }
        }
        else
        {
            _selectedOutputIdsThisSession.Remove(key);
            if (choice.Monitor.PersistenceKey is { } persistentId)
            {
                _explicitOutputIds.Remove(persistentId);
            }
        }

        PersistExplicitOutputs();
        RefreshPlaybackControls();
    }

    private static string SelectionKey(MonitorInfo monitor) => monitor.PersistenceKey ?? $"session:{monitor.Id}";

    private void ApplyMonitorNames()
    {
        var names = (Application.Current as App)?.Settings.MonitorNames ?? new Dictionary<string, string>();
        var operatorId = _operatorMonitor?.Id;
        _monitors = _monitors.Select(monitor => monitor with
        {
            CustomName = monitor.PersistenceKey is { } key && names.TryGetValue(key, out var name) ? name : null
        }).ToArray();
        _operatorMonitor = _monitors.FirstOrDefault(monitor => string.Equals(monitor.Id, operatorId, StringComparison.OrdinalIgnoreCase));
    }

    private void RefreshAudioDevices(bool force)
    {
        // Never initialize the native engine from a monitor-refresh timer.
        // Device enumeration is available after the first local media request
        // or when the user explicitly opens audio settings.
        if (_engineInstance is not { } nativeEngine)
        {
            return;
        }

        try
        {
            _audioDevices = nativeEngine.EnumerateAudioDevices();
        }
        catch (Exception exception)
        {
            FileLogger.Error("Falha ao enumerar saídas de áudio do LibVLC.", exception);
            AudioWarningText.Text = "Não foi possível atualizar as saídas de áudio. O dispositivo configurado foi preservado.";
            RefreshAudioPanel();
            return;
        }

        var signature = string.Join("\n", _audioDevices.Select(device => $"{device.Module}\0{device.DeviceId}\0{device.DisplayName}"));
        if (!force && _audioDeviceSignature is not null && string.Equals(signature, _audioDeviceSignature, StringComparison.Ordinal))
        {
            return;
        }

        _audioDeviceSignature = signature;
        var preference = (Application.Current as App)?.Settings.AudioOutput ?? AudioOutputPreference.Default;
        var availability = AudioPreferenceResolver.Resolve(preference, _audioDevices);
        var wasAvailable = _audioPreferenceAvailable;
        _audioPreferenceAvailable = availability switch
        {
            AudioPreferenceAvailability.WindowsDefault or AudioPreferenceAvailability.Available => !_audioReconfigurationRequired,
            AudioPreferenceAvailability.Unknown => _audioInventoryInitialized && wasAvailable && !_audioReconfigurationRequired,
            _ => false
        };
        _audioInventoryInitialized = true;

        AudioWarningText.Text = _audioReconfigurationRequired
            ? "A rota de áudio desta sessão foi perdida. Use Parar antes de iniciar novamente."
            : availability switch
            {
                AudioPreferenceAvailability.Missing => "A saída salva está indisponível. A reprodução fica bloqueada até ela voltar ou outra saída ser escolhida.",
                AudioPreferenceAvailability.Unknown => "O LibVLC não retornou inventário suficiente para confirmar a saída salva. Isso não prova ausência; atualize ou escolha outra saída antes de reproduzir.",
                AudioPreferenceAvailability.Available => "Saída explícita disponível. A seleção é aplicada pelo identificador aceito pelo LibVLC.",
                _ when _audioDevices.Count == 0 => "O LibVLC não enumerou dispositivos. Isso não prova ausência de áudio; o padrão do Windows continua disponível.",
                _ => "O dispositivo padrão será resolvido pelo Windows no início de cada reprodução."
            };

        RefreshAudioPanel();
        if (availability == AudioPreferenceAvailability.Missing && wasAvailable && _coordinator.IsActive)
        {
            _audioReconfigurationRequired = true;
            _audioPreferenceAvailable = false;
            RefreshAudioPanel();
            var transition = _coordinator.RequiredDeviceLost(_coordinator.Generation);
            if (transition.PauseEngine)
            {
                PauseForRequiredReconfiguration();
            }

            StatusText.Text = "Saída de áudio desconectada; reprodução pausada e retomada automática bloqueada.";
            MessageBox.Show(
                this,
                "A saída de áudio escolhida foi desconectada. A mídia foi pausada na posição atual. " +
                "Reconecte o dispositivo ou pare a reprodução e escolha outra saída; não haverá troca silenciosa nem retomada automática.",
                "Saída de áudio desconectada",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Engine_OnAudioDeviceChanged(long generation, string deviceId)
    {
        if (!_coordinator.AcceptsCallback(PlaybackCallbackKind.AudioDeviceChanged, generation))
        {
            return;
        }

        var preference = (Application.Current as App)?.Settings.AudioOutput ?? AudioOutputPreference.Default;
        if (!preference.IsExplicit || string.Equals(deviceId, preference.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var transition = _coordinator.RequiredDeviceLost(generation);
        if (!transition.Accepted)
        {
            return;
        }

        _audioPreferenceAvailable = false;
        _audioReconfigurationRequired = true;
        PauseForRequiredReconfiguration();
        AudioWarningText.Text = "O LibVLC informou que a saída explícita deixou de ser a saída ativa.";
        RefreshAudioPanel();
        StatusText.Text = "A saída de áudio mudou externamente; reprodução pausada.";
        MessageBox.Show(
            this,
            "A saída de áudio ativa mudou ou desapareceu. O GuiaPlay pausou a mídia e não fará fallback automático.",
            "Saída de áudio alterada",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private bool CanStartPlayback(MediaKind mediaKind, int publicOutputCount)
    {
        if (PlaybackRouting.UsesVideoOutputs(mediaKind) && publicOutputCount == 0)
        {
            return false;
        }

        if (!_audioPreferenceAvailable)
        {
            StatusText.Text = "Escolha uma saída de áudio disponível antes de reproduzir.";
            MessageBox.Show(this, "A saída de áudio salva está indisponível. Pare e escolha outra saída ou reconecte o dispositivo.",
                "Saída de áudio indisponível", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (PlaybackRouting.UsesVideoOutputs(mediaKind) && _operatorRequiresConfiguration)
        {
            StatusText.Text = "Escolha e salve a tela do operador antes da reprodução pública.";
            MessageBox.Show(this, "A tela do operador é provisória. Use Configurar telas e salve uma escolha antes de reproduzir em telas públicas.",
                "Configuração do operador necessária", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private void PauseForRequiredReconfiguration()
    {
        _engine.Pause();
        PlayButtonText.Text = "Continuar";
        SetConfigurationControlsEnabled(false);
        RefreshPlaybackControls();
    }

    private void SetConfigurationControlsEnabled(bool enabled)
    {
        HeaderSettingsButton.IsEnabled = SettingsAccessPolicy.CanOpenSettings(_controlsBusy);
    }

    private void PersistExplicitOutputs()
    {
        if (Application.Current is App app)
        {
            _ = app.UpdateSettings(current => current with
            {
                SelectedOutputIds = new HashSet<string>(_explicitOutputIds, StringComparer.OrdinalIgnoreCase)
            }, out var error);
            ShowSettingsSaveFailure(error);
        }
    }

    private void ScheduleSettingsSave()
    {
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    private void FlushDeferredSettings()
    {
        _settingsSaveTimer.Stop();
        if (Application.Current is App app)
        {
            _ = app.UpdateSettings(current => current with
            {
                Volume = (int)VolumeSlider.Value,
                Muted = MuteButton.Tag as bool? ?? false
            }, out var error);
            ShowSettingsSaveFailure(error);
        }
    }

    private void ShowSettingsSaveFailure(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return;
        }

        StatusText.Text = "Não foi possível salvar uma configuração; a sessão atual continua funcionando.";
    }

    private void UpdateMonitorWarning()
    {
        var unreliableCount = _monitors.Count(monitor => !monitor.IdentityReliable);
        var baseMessage = _monitors.Count <= 1
            ? "Somente o operador foi detectado. Vídeos exigem ao menos uma saída pública; áudio continua disponível."
            : "Marque uma ou mais saídas. Telas reconectadas ficam desmarcadas até nova seleção.";
        MonitorWarning.Text = unreliableCount == 0
            ? baseMessage
            : baseMessage + $" {unreliableCount} tela(s) têm identidade ambígua e não serão restauradas automaticamente.";
    }

    private nint WindowMessageHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmDisplayChange)
        {
            if (!_displayChangeDebouncer.TryAccept())
            {
                return nint.Zero;
            }

            CloseIdentifierWindows();
            _ = Dispatcher.BeginInvoke(() => RefreshConnectedDevices(true), DispatcherPriority.Send);
        }

        return nint.Zero;
    }

    private void SetControlsBusy(bool busy)
    {
        _controlsBusy = busy;
        OpenButton.IsEnabled = !busy && _licensingAllowsAccess;
        HeaderMediaButton.IsEnabled = !busy && _licensingAllowsAccess;
        HeaderPlaylistButton.IsEnabled = !busy && _licensingAllowsAccess;
        SetConfigurationControlsEnabled(!busy && !_coordinator.IsActive);
        RefreshSavedPlaylistActions();
        UpdateMediaGridActions();
        RefreshPlaybackControls();
    }

    private int SelectedOutputCount() => _outputChoices.Count(choice => choice.IsSelected);

    private void RefreshPlaybackControls()
    {
        RefreshStageDashboard();
        if (_controlsBusy || !_licensingAllowsAccess)
        {
            PlayButton.IsEnabled = false;
            PauseButton.IsEnabled = false;
            StopButton.IsEnabled = false;
            UpdateUnifiedPlayPauseVisual(isPlaying: false);
            RefreshPlaylistTransport();
            return;
        }

        if (_youtubeWindow is { } youtube)
        {
            var current = _youtubeSnapshot?.State ?? "CARREGANDO";
            PlayButton.IsEnabled = youtube.Ready && current is not "ERRO" and not "REPRODUZINDO";
            PauseButton.IsEnabled = youtube.Ready && current == "REPRODUZINDO";
            StopButton.IsEnabled = true;
            PlayButtonText.Text = current == "PAUSADO" ? "Continuar" : "Reproduzir";
            UpdateUnifiedPlayPauseVisual(current == "REPRODUZINDO");
            RefreshPlaylistTransport();
            return;
        }

        var status = _coordinator.Status;
        var mayStart = status is PlaybackStatus.Ready or PlaybackStatus.Stopped or PlaybackStatus.Ended or PlaybackStatus.Error or PlaybackStatus.Paused;
        PlayButton.IsEnabled = mayStart && _coordinator.CanPlay(SelectedOutputCount());
        PauseButton.IsEnabled = status == PlaybackStatus.Playing;
        StopButton.IsEnabled = _coordinator.MediaPath is not null;
        UpdateUnifiedPlayPauseVisual(status == PlaybackStatus.Playing);
        RefreshPlaylistTransport();
    }

    private void UpdateUnifiedPlayPauseVisual(bool isPlaying)
    {
        // PauseButton stays hidden for internal command/shortcut compatibility.
        // The hero is active when EITHER the old play or pause action can run.
        PlayButton.IsEnabled = PlayButton.IsEnabled || PauseButton.IsEnabled;
        MainPlayGlyph.Visibility = isPlaying ? Visibility.Collapsed : Visibility.Visible;
        MainPauseGlyph.Visibility = isPlaying ? Visibility.Visible : Visibility.Collapsed;
        PlayButton.ToolTip = isPlaying ? "Pausar reprodução (F6 / Espaço)" : "Reproduzir (F5 / Espaço)";
        System.Windows.Automation.AutomationProperties.SetName(PlayButton, isPlaying ? "Pausar" : "Reproduzir");
    }


    internal void ApplyLicensingState(LicensingSnapshot snapshot)
    {
        var previouslyAllowed = _licensingAllowsAccess;
        _licensingAllowsAccess = snapshot.HasAccess;
        LicensedWorkspace.IsEnabled = snapshot.HasAccess;
        OperationalControlPanel.IsEnabled = snapshot.HasAccess;
        LicenseBlockedOverlay.Visibility = snapshot.HasAccess ? Visibility.Collapsed : Visibility.Visible;
        LicenseBlockedTitleText.Text = snapshot.Status == "checking"
            ? "Verificando licença…"
            : snapshot.ErrorCode == "trial_consumed"
                ? "Período grátis já utilizado"
                : snapshot.ErrorCode is null
                    ? "Escolha como continuar"
                    : "Não foi possível validar";
        var errorSuffix = string.IsNullOrWhiteSpace(snapshot.ErrorCode) ||
                          snapshot.ErrorCode == "trial_consumed"
            ? string.Empty
            : $" (código: {snapshot.ErrorCode})";
        LicenseBlockedMessageText.Text = (snapshot.Message ??
            "Inicie a avaliação gratuita ou ative uma licença para usar o GuiaPlay.") + errorSuffix;
        LicenseStatusButton.Content = FormatLicenseSummary(snapshot);
        LicenseStatusDot.Fill = new SolidColorBrush(
            snapshot.HasAccess
                ? Color.FromRgb(0, 219, 130)
                : Color.FromRgb(145, 157, 174));

        if (!snapshot.HasAccess)
        {
            _fullscreenPreviewWindow?.Close();
            CloseYouTubeWindow();
        }

        if (!snapshot.HasAccess && previouslyAllowed && _coordinator.IsActive)
        {
            try
            {
                if (_coordinator.Status == PlaybackStatus.Playing)
                {
                    _engine.Pause();
                    _coordinator.Pause();
                }

                CloseOutputWindows();
                StatusText.Text = snapshot.Message ?? "A autorização do GuiaPlay deixou de ser válida.";
            }
            catch (Exception exception)
            {
                FileLogger.Error("Falha ao interromper reprodução após bloqueio de licença.", exception);
            }
        }
        else if (!snapshot.HasAccess)
        {
            StatusText.Text = snapshot.Message ?? "Ativação necessária para usar o GuiaPlay.";
        }

        OpenButton.IsEnabled = snapshot.HasAccess && !_controlsBusy;
        HeaderMediaButton.IsEnabled = snapshot.HasAccess && !_controlsBusy;
        HeaderPlaylistButton.IsEnabled = snapshot.HasAccess && !_controlsBusy;
        RefreshSavedPlaylistActions();
        UpdateMediaGridActions();
        RefreshAudioPanel();
        RefreshPlaybackControls();
    }

    private void LicensingManager_OnStateChanged(object? sender, EventArgs e)
    {
        if (sender is LicensingManager licensing)
        {
            Dispatch(() => ApplyLicensingState(licensing.Current));
        }
    }

    private static string FormatLicenseSummary(LicensingSnapshot snapshot)
    {
        if (snapshot.HasAccess && snapshot.AccessKind == LicensingAccessKind.License)
        {
            return snapshot.UsingOfflineAuthorization ? "Licença ativa • offline" : "Licença ativa";
        }

        if (snapshot.HasAccess && snapshot.AccessKind == LicensingAccessKind.Trial)
        {
            if (snapshot.ExpiresAt is { } expiresAt)
            {
                var remaining = expiresAt.ToUniversalTime() - DateTimeOffset.UtcNow;
                if (remaining > TimeSpan.Zero)
                {
                    var text = remaining.TotalDays >= 1
                        ? $"{(int)remaining.TotalDays}d {remaining.Hours}h"
                        : $"{Math.Max(0, (int)remaining.TotalHours)}h {remaining.Minutes}min";
                    return snapshot.UsingOfflineAuthorization
                        ? $"Avaliação • {text} • offline"
                        : $"Avaliação • {text}";
                }
            }

            return "Avaliação ativa";
        }

        if (snapshot.Status == "checking")
        {
            return "Verificando licença…";
        }

        return snapshot.ErrorCode == "trial_consumed" ? "Ativar GuiaPlay" : "Continuar";
    }

    private void CloseIdentifierWindows()
    {
        var cancellation = _identifierCancellation;
        _identifierCancellation = null;
        cancellation?.Cancel();
        foreach (var window in _identifierWindows)
        {
            window.Close();
        }

        _identifierWindows.Clear();
    }

    private void Dispatch(Action action)
    {
        if (!_isClosing)
        {
            _ = Dispatcher.BeginInvoke(action);
        }
    }

    private async void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        _fullscreenPreviewWindow?.Close();
        _fullscreenPreviewWindow = null;
        CloseYouTubeWindow();
        if (Application.Current is App app)
        {
            if (app.UpdateManager is { } updateManager)
            {
                updateManager.StateChanged -= UpdateManager_OnStateChanged;
            }

            if (app.Licensing is { } licensing)
            {
                licensing.StateChanged -= LicensingManager_OnStateChanged;
            }
        }
        FlushDeferredSettings();
        Hide();
        _monitorTimer.Stop();
        _settingsSaveTimer.Stop();
        _playlistAvailabilityCancellation?.Cancel();
        _playlistAvailabilityCancellation?.Dispose();
        _playlistAvailabilityCancellation = null;
        _folderImportCancellation?.Cancel();
        CloseIdentifierWindows();
        CloseOutputWindows();
        LogDiagnosticSnapshot("Encerramento da aplicação");
        if (_engineInstance is { } nativeEngine)
        {
            try
            {
                await nativeEngine.DisposeAsync();
            }
            catch (Exception exception)
            {
                FileLogger.Error("Falha ao encerrar o motor.", exception);
            }
        }

        _allowClose = true;
        Close();
    }

    private DiagnosticSnapshot CaptureDiagnostics()
    {
        var updateManager = (Application.Current as App)?.UpdateManager;
        var updateState = updateManager?.IsPreparing == true
            ? updateManager.PreparationProgress.Stage.ToString()
            : updateManager?.LastResult?.Status.ToString() ?? "Não verificado";
        return _diagnostics.Capture(
            _processMetrics.Sample(),
            _coordinator.Status,
            _coordinator.MediaPath,
            _coordinator.MediaKind,
            _outputWindows.Count,
            updateState);
    }

    private void LogDiagnosticSnapshot(string reason)
    {
        var snapshot = CaptureDiagnostics();
        var cpu = snapshot.Resources.CpuPercent is { } value
            ? $"{value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}%"
            : "coletando";
        FileLogger.Info(
            $"{reason}; estado={snapshot.PlaybackState}; geração={_coordinator.Generation}; tipo={snapshot.MediaType}; " +
            $"saídas={snapshot.OutputCount}; working-set={snapshot.Resources.WorkingSetBytes}; privada={snapshot.Resources.PrivateMemoryBytes}; " +
            $"gerenciada={snapshot.Resources.ManagedMemoryBytes}; cpu={cpu}; " +
            $"frames={snapshot.FramesReceived}/{snapshot.FramesRendered}/{snapshot.FramesReplaced}; " +
            $"cópia-média={snapshot.AverageFrameCopyMilliseconds:F3} ms; trocas={snapshot.MediaSwitches}.");
    }

}
