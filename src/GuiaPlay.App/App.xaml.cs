using System.IO;
using System.Windows;
using GuiaPlay.App.Services;
using GuiaPlay.Core;

namespace GuiaPlay.App;

public partial class App : Application
{
    private AppSettingsStore? _settingsStore;
    private SingleInstanceCoordinator? _singleInstance;

    public AppSettings Settings { get; private set; } = AppSettings.Default;
    public PlaylistStore? PlaylistStore { get; private set; }
    public PlaylistDocument Playlist { get; set; } = PlaylistDocument.Empty;
    internal UpdateManager? UpdateManager { get; private set; }
    internal WindowsIntegrationService? WindowsIntegration { get; private set; }
    public AppearancePreference Appearance => Settings.Appearance;
    public string? StartupSettingsWarning { get; private set; }
    public string? StartupPlaylistWarning { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args is [var command] &&
            string.Equals(command, WindowsIntegrationService.RemoveCommandLineSwitch, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                CreateWindowsIntegrationService().RemoveAll();
                Shutdown(0);
            }
            catch
            {
                Shutdown(1);
            }

            return;
        }

        var launchArgument = LaunchArgumentParser.Parse(e.Args);
        _singleInstance = new SingleInstanceCoordinator(
            @"Local\GuiaPlay.M04.SingleInstance",
            "GuiaPlay.M04.Commands");
        SingleInstanceStartResult instanceResult;
        try
        {
            var forwardedArgument = launchArgument.MediaPath ?? e.Args.FirstOrDefault(argument => !string.IsNullOrWhiteSpace(argument));
            instanceResult = _singleInstance.StartAsync(forwardedArgument, HandleForwardedRequestAsync)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                $"Não foi possível comunicar com a instância aberta do GuiaPlay.\n\n{exception.Message}",
                "GuiaPlay — instância única",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        if (instanceResult == SingleInstanceStartResult.Forwarded)
        {
            Shutdown();
            return;
        }

        FileLogger.Initialize();
        var settingsPathOverride = Environment.GetEnvironmentVariable("GUIAPLAY_SETTINGS_PATH");
        var settingsPath = string.IsNullOrWhiteSpace(settingsPathOverride)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GuiaSys",
                "GuiaPlay",
                "settings.json")
            : Path.GetFullPath(settingsPathOverride);
        _settingsStore = new AppSettingsStore(settingsPath);
        var loadResult = _settingsStore.Load();
        Settings = loadResult.Settings;
        StartupSettingsWarning = loadResult.Warning;
        if (loadResult.Warning is not null)
        {
            FileLogger.Info(loadResult.Warning);
        }

        var playlistPathOverride = Environment.GetEnvironmentVariable("GUIAPLAY_PLAYLIST_PATH");
        var playlistPath = string.IsNullOrWhiteSpace(playlistPathOverride)
            ? Path.Combine(Path.GetDirectoryName(settingsPath)!, "playlist.json")
            : Path.GetFullPath(playlistPathOverride);
        PlaylistStore = new PlaylistStore(playlistPath);
        var playlistLoadResult = PlaylistStore.Load();
        Playlist = playlistLoadResult.Playlist;
        StartupPlaylistWarning = playlistLoadResult.Warning;
        if (playlistLoadResult.Warning is not null)
        {
            FileLogger.Info(playlistLoadResult.Warning);
        }

        UpdateManager = new UpdateManager(this);
        WindowsIntegration = CreateWindowsIntegrationService();

        ApplyAppearance(Settings.Appearance, persist: false);
        DispatcherUnhandledException += (_, args) =>
        {
            FileLogger.Error("Falha não tratada na interface.", args.Exception);
            MessageBox.Show(
                $"O GuiaPlay encontrou um erro e continuará aberto quando possível.\n\n{args.Exception.Message}",
                "GuiaPlay — erro",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };
        var window = new MainWindow();
        MainWindow = window;
        window.Loaded += async (_, _) =>
        {
            if (launchArgument.Warning is { } warning)
            {
                window.ShowExternalRequestStatus(warning);
            }
            else if (launchArgument.MediaPath is { } mediaPath)
            {
                await window.HandleExternalMediaAsync(mediaPath);
            }
        };
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    public void ApplyAppearance(AppearancePreference preference, bool persist = true)
    {
        Settings = Settings with { Appearance = preference };
        ThemeMode = preference switch
        {
            AppearancePreference.Light => ThemeMode.Light,
            AppearancePreference.Dark => ThemeMode.Dark,
            _ => ThemeMode.System
        };

        if (!persist)
        {
            return;
        }

        _ = SaveSettings(out _);
        FileLogger.Info($"Aparência alterada para {preference}.");
    }

    public bool UpdateSettings(Func<AppSettings, AppSettings> update, out string? error)
    {
        ArgumentNullException.ThrowIfNull(update);
        Settings = update(Settings);
        return SaveSettings(out error);
    }

    public bool SaveSettings(out string? error)
    {
        if (_settingsStore is null)
        {
            error = "O armazenamento de configurações ainda não foi inicializado.";
            return false;
        }

        var result = _settingsStore.Save(Settings);
        error = result.Error;
        if (!result.Succeeded)
        {
            FileLogger.Error($"Não foi possível salvar as configurações: {result.Error}");
        }

        return result.Succeeded;
    }

    private static WindowsIntegrationService CreateWindowsIntegrationService()
    {
        var executablePath = Path.Combine(AppContext.BaseDirectory, "GuiaPlay.exe");
        return new WindowsIntegrationService(
            new WindowsRegistryStore(),
            executablePath,
            ShellAssociationNotifier.NotifyChanged);
    }

    private Task HandleForwardedRequestAsync(string? argument)
    {
        return Dispatcher.InvokeAsync(async () =>
        {
            if (MainWindow is not MainWindow window)
            {
                return;
            }

            if (argument is null)
            {
                window.BringToFront();
                return;
            }

            var parsed = LaunchArgumentParser.Parse([argument]);
            if (parsed.Warning is { } warning)
            {
                window.ShowExternalRequestStatus(warning);
                return;
            }

            await window.HandleExternalMediaAsync(parsed.MediaPath!);
        }).Task.Unwrap();
    }
}
