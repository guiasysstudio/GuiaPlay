using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
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
    private const int WmDisplayChange = 0x007E;
    private const string PlaylistDragFormat = "GuiaPlay.PlaylistNode";
    private readonly PlaybackCoordinator _coordinator = new();
    private readonly LibVlcPlaybackEngine _engine;
    private readonly ObservableCollection<MonitorChoice> _outputChoices = [];
    private readonly ObservableCollection<PlaylistGroupNode> _playlistGroups = [];
    private readonly PlaylistCatalog _playlistCatalog;
    private readonly Dictionary<string, OutputWindow> _outputWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IdentifierWindow> _identifierWindows = [];
    private readonly DispatcherTimer _monitorTimer;
    private readonly DispatcherTimer _settingsSaveTimer;
    private readonly ReconnectAuthorization _reconnectAuthorization = new();
    private readonly HashSet<string> _selectedOutputIdsThisSession = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _explicitOutputIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action _renderFrameCallback;
    private IReadOnlyList<MonitorInfo> _monitors = [];
    private WriteableBitmap? _bitmap;
    private long _frameGeneration;
    private int _renderPending;
    private bool _isDraggingProgress;
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
    private bool _operatorRequiresConfiguration;
    private bool _audioPreferenceAvailable = true;
    private bool _audioReconfigurationRequired;
    private bool _audioInventoryInitialized;
    private string? _audioDeviceSignature;
    private IReadOnlyList<AudioDeviceIdentity> _audioDevices = [];
    private MonitorInfo? _operatorMonitor;
    private CancellationTokenSource? _identifierCancellation;
    private Point _playlistDragStart;
    private object? _playlistDragSource;
    private bool _autoInstallInProgress;

    public MainWindow()
    {
        InitializeComponent();
        Title = $"GuiaPlay {ProductInfo.Version}";
        _renderFrameCallback = RenderLatestFrame;
        OutputsList.ItemsSource = _outputChoices;
        PlaylistTree.ItemsSource = _playlistGroups;
        var app = Application.Current as App;
        var settings = app?.Settings ?? AppSettings.Default;
        _playlistCatalog = new PlaylistCatalog(app?.Playlist ?? PlaylistDocument.Empty);
        RebuildPlaylistTree();
        foreach (var id in settings.SelectedOutputIds)
        {
            _explicitOutputIds.Add(id);
        }

        VolumeSlider.Value = settings.Volume;
        MuteButton.Tag = settings.Muted;
        UpdateMuteVisual(settings.Muted);
        VolumeText.Text = settings.Muted ? "mudo" : $"{settings.Volume}%";
        _engine = new LibVlcPlaybackEngine();
        _engine.FrameReady += Engine_OnFrameReady;
        _engine.Playing += generation => Dispatch(() => Engine_OnPlaying(generation));
        _engine.Paused += generation => Dispatch(() => Engine_OnPaused(generation));
        _engine.EndReached += generation => Dispatch(() => Engine_OnEndReached(generation));
        _engine.Error += (generation, message) => Dispatch(() => Engine_OnError(generation, message));
        _engine.TimeChanged += (generation, time) => Dispatch(() => UpdateTime(generation, time));
        _engine.LengthChanged += (generation, length) => Dispatch(() => UpdateLength(generation, length));
        _engine.AudioDeviceChanged += (generation, deviceId) => Dispatch(() => Engine_OnAudioDeviceChanged(generation, deviceId));

        _monitorTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => RefreshConnectedDevices(false), Dispatcher);
        _settingsSaveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(650), DispatcherPriority.Background, (_, _) => FlushDeferredSettings(), Dispatcher)
        {
            IsEnabled = false
        };
        _settingsInitialized = true;
        if (app?.UpdateManager is { } updateManager)
        {
            updateManager.StateChanged += UpdateManager_OnStateChanged;
        }
        Loaded += (_, _) =>
        {
            FitToWorkArea();
            RefreshConnectedDevices(false);
            if (_operatorMonitor is not null)
            {
                WindowPlacement.PlaceOperatorPanel(this, _operatorMonitor);
            }

            _monitorTimer.Start();
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
            _ = CheckForUpdatesOnStartupAsync();
        };
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessageHook);
        Closing += MainWindow_OnClosing;
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

    private async Task<bool> LoadMediaAsync(string path, bool playImmediately)
    {
        var kind = MediaTypeDetector.Detect(path);
        if (kind == MediaKind.Unknown || !File.Exists(path))
        {
            StatusText.Text = "Arquivo indisponível ou formato não reconhecido.";
            return false;
        }

        var replacementConfirmed = true;
        if (_coordinator.IsActive)
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
            return false;
        }

        _audioReconfigurationRequired = false;
        RefreshAudioDevices(force: true);
        try
        {
            SetControlsBusy(true);
            CloseOutputWindows();
            StatusText.Text = "Carregando mídia…";
            var audioOutput = (Application.Current as App)?.Settings.AudioOutput ?? AudioOutputPreference.Default;
            await _engine.LoadAsync(path, kind, _coordinator.Generation, audioOutput);
            _engine.Volume = (int)VolumeSlider.Value;
            _engine.Muted = MuteButton.Tag as bool? ?? false;
            _bitmap = null;
            PreviewImage.Source = null;
            PreviewPlaceholder.Text = kind == MediaKind.Audio ? "Áudio carregado" : "Prévia de vídeo";
            PreviewPlaceholder.Visibility = Visibility.Visible;
            FileNameText.Text = Path.GetFileName(path);
            FileNameText.ToolTip = Path.GetFileName(path);
            ProgressSlider.Value = 0;
            ProgressSlider.Maximum = 1;
            UpdateTimeLabels(0, 0);
            StatusText.Text = kind == MediaKind.Audio
                ? "Áudio carregado; pronto para reproduzir."
                : _outputChoices.Any(choice => choice.IsSelected)
                    ? "Vídeo carregado; pronto para reproduzir."
                    : "Vídeo carregado; selecione ao menos uma saída.";
            RefreshPlaybackControls();
        }
        catch (Exception exception)
        {
            _coordinator.PlaybackFailed(_coordinator.Generation);
            FileLogger.Error($"Falha ao carregar '{path}'.", exception);
            MessageBox.Show(this, exception.Message, "Não foi possível abrir a mídia", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Falha ao carregar; você pode escolher outro arquivo.";
            return false;
        }
        finally
        {
            SetControlsBusy(false);
        }

        if (playImmediately && _coordinator.CanPlay(SelectedOutputCount()))
        {
            await StartOrResumePlaybackAsync();
        }

        return true;
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

    private void AddPlaylistItemButton_OnClick(object sender, RoutedEventArgs e)
    {
        var group = SelectedPlaylistGroup();
        if (group is null)
        {
            StatusText.Text = "Selecione primeiro o grupo que receberá a mídia.";
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Adicionar referências à playlist",
            Filter = "Mídia compatível|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.webm;*.m4v;*.mpg;*.mpeg;*.ts;*.m2ts;*.3gp;*.ogv;*.mp3;*.wav;*.ogg;*.flac;*.aac;*.m4a;*.wma;*.opus;*.aiff;*.alac",
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        foreach (var path in dialog.FileNames)
        {
            _playlistCatalog.AddItem(group.Id, path);
        }

        SavePlaylistAndRefresh($"{dialog.FileNames.Length} referência(s) adicionada(s); os arquivos não foram copiados.");
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

        var action = PlaylistActivationPolicy.Resolve(item.Item.Kind, item.IsAvailable, SelectedOutputCount());
        if (action == PlaylistActivationAction.None)
        {
            StatusText.Text = $"Arquivo não encontrado: {item.Name}";
            return;
        }

        await LoadMediaAsync(item.Item.OriginalPath, playImmediately: action == PlaylistActivationAction.LoadAndPlay);
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

    private void PlaylistTree_OnDrop(object sender, DragEventArgs e)
    {
        var target = FindPlaylistNode(e.OriginalSource as DependencyObject);
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            ImportDroppedFiles(paths, target);
            e.Handled = true;
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

    private void ImportDroppedFiles(IEnumerable<string> paths, object? target)
    {
        var group = ResolveDropGroup(target);
        if (group is null)
        {
            var created = _playlistCatalog.CreateGroup("Mídias");
            group = new PlaylistGroupNode(created);
        }

        var result = _playlistCatalog.AddItems(group.Id, paths);
        if (result.Added.Count == 0)
        {
            StatusText.Text = result.Rejected.Count == 0
                ? "Nenhum arquivo foi recebido."
                : "Nenhum formato de mídia reconhecido foi adicionado.";
            return;
        }

        var message = $"{result.Added.Count} referência(s) adicionada(s) a {group.Name}.";
        if (result.Rejected.Count > 0)
        {
            message += $" {result.Rejected.Count} arquivo(s) ignorado(s).";
        }

        SavePlaylistAndRefresh(message);
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
        _playlistGroups.Clear();
        foreach (var group in _playlistCatalog.Snapshot.Groups)
        {
            _playlistGroups.Add(new PlaylistGroupNode(group));
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
        StatusText.Text = successMessage;
    }

    private async void PlayButton_OnClick(object sender, RoutedEventArgs e)
        => await StartOrResumePlaybackAsync();

    private async Task StartOrResumePlaybackAsync()
    {
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
        if (_coordinator.MediaPath is null)
        {
            return;
        }

        CloseOutputWindows();
        _coordinator.Stop();
        SetControlsBusy(true);
        var stoppedSafely = false;
        try
        {
            await _engine.StopAsync();
            _audioReconfigurationRequired = false;
            RefreshAudioDevices(force: true);
            ProgressSlider.Value = 0;
            UpdateTimeLabels(0, Math.Max(0, _engine.Length));
            StatusText.Text = "Parado; o arquivo continua carregado";
            LogPlaybackMetrics($"Parada da geração {_coordinator.Generation}");
            PlayButtonText.Text = "Reproduzir";
            SetConfigurationControlsEnabled(true);
            RefreshPlaybackControls();
            stoppedSafely = true;
        }
        catch (Exception exception)
        {
            FileLogger.Error("Falha ao parar o motor.", exception);
            StatusText.Text = "A saída foi fechada, mas o motor informou uma falha ao parar";
        }
        finally
        {
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

    private void MainWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _identifierWindows.Count > 0)
        {
            CloseIdentifierWindows();
            e.Handled = true;
        }
    }

    private async void ConfigureScreensButton_OnClick(object sender, RoutedEventArgs e) =>
        await OpenSettingsAsync(openUpdates: false);

    private async void UpdateAvailableButton_OnClick(object sender, RoutedEventArgs e) =>
        await OpenSettingsAsync(openUpdates: true);

    private async Task OpenSettingsAsync(bool openUpdates)
    {
        if (_coordinator.IsActive && !openUpdates)
        {
            StatusText.Text = "Pare a reprodução antes de alterar a configuração das telas.";
            return;
        }

        if (_monitors.Count == 0)
        {
            StatusText.Text = "Nenhuma tela ativa foi encontrada para configurar.";
            return;
        }

        var app = Application.Current as App;
        if (app is not { UpdateManager: { } updateManager })
        {
            StatusText.Text = "O serviço de atualizações ainda não está disponível.";
            return;
        }
        var currentSettings = app.Settings;
        var names = currentSettings.MonitorNames;
        RefreshAudioDevices(force: true);
        var dialog = new ScreenConfigurationWindow(
            _monitors,
            _operatorMonitor?.Id,
            currentSettings,
            _audioDevices,
            updateManager,
            openUpdates,
            _coordinator.IsActive)
        { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } result)
        {
            StatusText.Text = "Configuração de telas cancelada; nada foi alterado.";
            return;
        }

        if (_coordinator.IsActive)
        {
            _ = app.UpdateSettings(current => current with
            {
                CheckUpdatesAutomatically = result.CheckUpdatesAutomatically,
                InstallUpdatesAutomatically = result.InstallUpdatesAutomatically
            }, out var activeSaveError);
            ShowSettingsSaveFailure(activeSaveError);
            StatusText.Text = "Preferências de atualização salvas; reprodução preservada.";
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

        _operatorMonitor = result.OperatorMonitor;
        _operatorRequiresConfiguration = false;
        if (_operatorMonitor.PersistenceKey is { } operatorKey)
        {
            _selectedOutputIdsThisSession.Remove(operatorKey);
            _explicitOutputIds.Remove(operatorKey);
        }

        if (app is not null)
        {
            _ = app.UpdateSettings(current => current with
            {
                OperatorMonitorId = _operatorMonitor.PersistenceKey,
                MonitorNames = mergedNames,
                SelectedOutputIds = new HashSet<string>(_explicitOutputIds, StringComparer.OrdinalIgnoreCase),
                Appearance = result.Appearance,
                AudioOutput = result.AudioOutput,
                CheckUpdatesAutomatically = result.CheckUpdatesAutomatically,
                InstallUpdatesAutomatically = result.InstallUpdatesAutomatically
            }, out var error);
            ShowSettingsSaveFailure(error);
            app.ApplyAppearance(result.Appearance, persist: false);
        }

        _audioPreferenceAvailable = true;
        _audioReconfigurationRequired = false;
        if (_coordinator.MediaPath is not null)
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

        ApplyMonitorNames();
        RebuildOutputChoices();
        UpdateOperatorIndicator();
        OperatorStatusText.Text = _operatorMonitor.PersistenceKey is null
            ? "Operador escolhido para esta sessão; a identidade desta tela é ambígua e não será restaurada automaticamente."
            : "O operador nunca aparece entre as saídas públicas.";
        WindowPlacement.PlaceOperatorPanel(this, _operatorMonitor);
        StatusText.Text = "Configurações salvas e aplicadas.";
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
        await manager.CheckAsync(manual: false);
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
        if (_autoInstallInProgress || _coordinator.IsActive || Application.Current is not App { UpdateManager: { } manager } app ||
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
        var muted = !(MuteButton.Tag as bool? ?? false);
        MuteButton.Tag = muted;
        UpdateMuteVisual(muted);
        _engine.Muted = muted;
        VolumeText.Text = muted ? "mudo" : $"{(int)VolumeSlider.Value}%";
        ScheduleSettingsSave();
    }

    private void UpdateMuteVisual(bool muted)
    {
        SpeakerIcon.Visibility = muted ? Visibility.Collapsed : Visibility.Visible;
        SpeakerMuteIcon.Visibility = muted ? Visibility.Visible : Visibility.Collapsed;
        MuteButtonText.Text = muted ? "Ativar som" : "Mudo";
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
        var value = (int)e.NewValue;
        if (VolumeText is not null)
        {
            VolumeText.Text = MuteButton?.Tag as bool? == true ? "mudo" : $"{value}%";
        }

        if (_engine is not null)
        {
            _engine.Volume = value;
        }

        if (_settingsInitialized)
        {
            ScheduleSettingsSave();
        }
    }

    private void ProgressSlider_OnDragStarted(object sender, MouseButtonEventArgs e) => _isDraggingProgress = true;

    private void ProgressSlider_OnDragCompleted(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingProgress)
        {
            return;
        }

        _isDraggingProgress = false;
        _engine.Seek((long)ProgressSlider.Value);
        UpdateTimeLabels((long)ProgressSlider.Value, _engine.Length);
    }

    private void Engine_OnFrameReady(long generation)
    {
        Interlocked.Increment(ref _frameCallbacks);
        _frameGeneration = generation;
        if (Interlocked.Exchange(ref _renderPending, 1) == 0)
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Render, _renderFrameCallback);
        }
    }

    private void RenderLatestFrame()
    {
        Interlocked.Exchange(ref _renderPending, 0);
        var generation = _frameGeneration;
        if (generation != _coordinator.Generation || !_engine.TryAcquireLatestFrame(generation, out var frame))
        {
            return;
        }

        using (frame)
        {
            if (_bitmap is null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height)
            {
                _bitmap = new WriteableBitmap((int)frame.Width, (int)frame.Height, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null);
                PreviewImage.Source = _bitmap;
                foreach (var window in _outputWindows.Values)
                {
                    window.SetSource(_bitmap);
                }

                PreviewPlaceholder.Visibility = Visibility.Collapsed;
            }

            var copyStart = Stopwatch.GetTimestamp();
            _bitmap.WritePixels(
                new Int32Rect(0, 0, (int)frame.Width, (int)frame.Height),
                frame.Pointer,
                checked((int)(frame.Pitch * frame.Height)),
                (int)frame.Pitch);
            Interlocked.Add(ref _copyTicks, Stopwatch.GetTimestamp() - copyStart);
            Interlocked.Increment(ref _framesRendered);
            if (_outputsAwaitingFirstFrame)
            {
                foreach (var window in _outputWindows.Values)
                {
                    window.SetSource(_bitmap);
                }

                _outputsAwaitingFirstFrame = false;
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
        if (generation != _coordinator.Generation)
        {
            return;
        }

        StatusText.Text = "Pausado";
    }

    private void Engine_OnEndReached(long generation)
    {
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
        _ = TryAutomaticInstallationAsync();
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
        MessageBox.Show(this, message, "Erro de reprodução", MessageBoxButton.OK, MessageBoxImage.Error);
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
        if (generation != _coordinator.Generation)
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
    }

    private void UpdateLength(long generation, long length)
    {
        if (generation != _coordinator.Generation)
        {
            return;
        }

        ProgressSlider.Maximum = Math.Max(1, length);
        UpdateTimeLabels(_engine.Time, length);
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
        var names = (Application.Current as App)?.Settings.MonitorNames ?? new Dictionary<string, string>();
        current = current.Select(monitor => monitor with
        {
            CustomName = monitor.PersistenceKey is { } key && names.TryGetValue(key, out var name) ? name : null
        }).ToArray();
        var currentIds = current.Select(monitor => monitor.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var disconnectedMonitors = previousMonitors.Where(monitor => FindCurrentMonitor(monitor, current) is null).ToArray();
        foreach (var monitor in disconnectedMonitors)
        {
            _selectedOutputIdsThisSession.Remove(SelectionKey(monitor));
            if (monitor.PersistenceKey is { } persistentId)
            {
                _reconnectAuthorization.MarkDisconnected(persistentId);
            }
        }

        var currentOperatorMatch = _operatorMonitor is null ? null : FindCurrentMonitor(_operatorMonitor, current);
        var operatorWasLost = _operatorMonitor is not null && currentOperatorMatch is null;
        var hadPublicOutputs = _outputWindows.Count > 0;
        var disconnectedOutputIds = _outputWindows
            .Where(pair => FindCurrentMonitor(pair.Value.Monitor, current) is null)
            .Select(pair => pair.Key)
            .ToArray();
        var disconnectedOutputLabels = disconnectedOutputIds
            .Select(id => _outputWindows[id].Monitor.ShortLabel)
            .ToArray();

        foreach (var identifier in _identifierWindows.Where(window => !currentIds.Contains(window.MonitorId)).ToArray())
        {
            identifier.Close();
            _identifierWindows.Remove(identifier);
        }

        foreach (var id in disconnectedOutputIds)
        {
            _outputWindows[id].Close();
            _outputWindows.Remove(id);
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

        foreach (var pair in _outputWindows.ToArray())
        {
            var monitor = FindCurrentMonitor(pair.Value.Monitor, current);
            if (monitor is null)
            {
                continue;
            }

            if (!string.Equals(pair.Key, monitor.Id, StringComparison.OrdinalIgnoreCase))
            {
                _outputWindows.Remove(pair.Key);
                _outputWindows[monitor.Id] = pair.Value;
            }

            pair.Value.Reposition(monitor);
            pair.Value.Show();
        }

        UpdateMonitorWarning();
        UpdateActiveOutputs();

        if (operatorWasLost)
        {
            FileLogger.Info("A tela do operador foi desconectada; reprodução pública bloqueada até nova configuração.");
            MessageBox.Show(
                this,
                "A tela do operador foi desconectada. A reprodução foi pausada e o painel foi movido para uma tela disponível. " +
                "Abra Configurar telas e escolha novamente o operador; a reprodução não será retomada automaticamente.",
                "Tela do operador desconectada",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        else if (disconnectedOutputIds.Length > 0)
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
        _operatorRequiresConfiguration = true;
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

    private static MonitorInfo? FindCurrentMonitor(MonitorInfo previous, IReadOnlyList<MonitorInfo> current)
    {
        if (previous.PersistenceKey is { } persistentId)
        {
            var matches = current.Where(monitor =>
                string.Equals(monitor.PersistenceKey, persistentId, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        return current.FirstOrDefault(monitor => string.Equals(monitor.Id, previous.Id, StringComparison.OrdinalIgnoreCase));
    }

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
        try
        {
            _audioDevices = _engine.EnumerateAudioDevices();
        }
        catch (Exception exception)
        {
            FileLogger.Error("Falha ao enumerar saídas de áudio do LibVLC.", exception);
            AudioWarningText.Text = "Não foi possível atualizar as saídas de áudio. O padrão do Windows continua disponível.";
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

        if (availability == AudioPreferenceAvailability.Missing && wasAvailable && _coordinator.IsActive)
        {
            _audioReconfigurationRequired = true;
            _audioPreferenceAvailable = false;
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
        if (generation != _coordinator.Generation)
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
        ConfigureScreensButton.IsEnabled = enabled;
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
            CloseIdentifierWindows();
            foreach (var window in _outputWindows.Values)
            {
                window.Hide();
            }

            _ = Dispatcher.BeginInvoke(() => RefreshConnectedDevices(true), DispatcherPriority.Send);
        }

        return nint.Zero;
    }

    private void SetControlsBusy(bool busy)
    {
        _controlsBusy = busy;
        OpenButton.IsEnabled = !busy;
        SetConfigurationControlsEnabled(!busy && !_coordinator.IsActive);
        RefreshPlaybackControls();
    }

    private int SelectedOutputCount() => _outputChoices.Count(choice => choice.IsSelected);

    private void RefreshPlaybackControls()
    {
        if (_controlsBusy)
        {
            PlayButton.IsEnabled = false;
            PauseButton.IsEnabled = false;
            StopButton.IsEnabled = false;
            return;
        }

        var status = _coordinator.Status;
        var mayStart = status is PlaybackStatus.Ready or PlaybackStatus.Stopped or PlaybackStatus.Ended or PlaybackStatus.Error or PlaybackStatus.Paused;
        PlayButton.IsEnabled = mayStart && _coordinator.CanPlay(SelectedOutputCount());
        PauseButton.IsEnabled = status == PlaybackStatus.Playing;
        StopButton.IsEnabled = _coordinator.MediaPath is not null;
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
        if (Application.Current is App { UpdateManager: { } updateManager })
        {
            updateManager.StateChanged -= UpdateManager_OnStateChanged;
        }
        FlushDeferredSettings();
        Hide();
        _monitorTimer.Stop();
        _settingsSaveTimer.Stop();
        CloseIdentifierWindows();
        CloseOutputWindows();
        try
        {
            await _engine.DisposeAsync();
        }
        catch (Exception exception)
        {
            FileLogger.Error("Falha ao encerrar o motor.", exception);
        }

        _allowClose = true;
        Close();
    }

}
