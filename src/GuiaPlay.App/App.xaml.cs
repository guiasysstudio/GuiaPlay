using System.IO;
using System.Windows;
using System.Windows.Media;
using GuiaPlay.App.Services;
using GuiaPlay.Core;
using Microsoft.Win32;

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
    public AccentColorPreference AccentColor => Settings.AccentColor;
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

        // Keep the original argument only for the local single-instance hand-off. The
        // primary process validates it with LaunchArgumentParser before it reaches the
        // playback workflow, so an Explorer verb can never launch an arbitrary file.
        var launchArgument = e.Args.FirstOrDefault(argument => !string.IsNullOrWhiteSpace(argument));
        _singleInstance = new SingleInstanceCoordinator(
            @"Local\GuiaPlay.SingleInstance",
            "GuiaPlay.Commands",
            [new SingleInstanceEndpoint(@"Local\GuiaPlay.M04.SingleInstance", "GuiaPlay.M04.Commands")]);
        SingleInstanceStartResult instanceResult;
        try
        {
            instanceResult = _singleInstance.StartAsync(launchArgument, HandleForwardedRequestAsync)
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

        var launchArguments = e.Args.ToArray();

        FileLogger.Initialize();
        _singleInstance.RequestFailed += exception =>
            FileLogger.Error("Falha ao processar uma solicitação da instância única.", exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            FileLogger.Error("Falha não tratada no processo.", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
            FileLogger.Error("Falha não observada em tarefa assíncrona.", args.Exception);
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

        ApplyAppearance(Settings.Appearance, Settings.AccentColor, persist: false);
        SystemEvents.UserPreferenceChanged += SystemEvents_OnUserPreferenceChanged;
        DispatcherUnhandledException += (_, args) =>
        {
            FileLogger.Error("Falha não tratada na interface.", args.Exception);
            if (args.Exception is not IOException and not UnauthorizedAccessException)
            {
                MessageBox.Show(
                    $"O GuiaPlay encontrou uma falha crítica e será encerrado de forma controlada.\n\n{args.Exception.Message}",
                    "GuiaPlay — falha crítica",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                args.Handled = true;
                Shutdown(1);
                return;
            }

            MessageBox.Show(
                $"O GuiaPlay encontrou uma falha de acesso a arquivo, mas a sessão pode continuar.\n\n{args.Exception.Message}",
                "GuiaPlay — erro",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };
        var window = new MainWindow();
        MainWindow = window;
        window.Loaded += async (_, _) =>
        {
            var launchRequest = await LaunchArgumentParser.ParseAsync(
                launchArguments,
                TimeSpan.FromSeconds(4));
            if (launchRequest.Warning is { } warning)
            {
                FileLogger.Info(warning);
                window.ShowExternalRequestStatus(warning);
            }
            else if (launchRequest.MediaPath is { } mediaPath)
            {
                await window.HandleExternalMediaAsync(mediaPath);
            }
        };
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= SystemEvents_OnUserPreferenceChanged;
        UpdateManager?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    public void ApplyAppearance(
        AppearancePreference preference,
        AccentColorPreference accentColor,
        bool persist = true)
    {
        if (persist && !UpdateSettings(
                current => current with { Appearance = preference, AccentColor = accentColor },
                out _))
        {
            return;
        }

        ThemeMode = preference switch
        {
            AppearancePreference.Light => ThemeMode.Light,
            AppearancePreference.Dark => ThemeMode.Dark,
            _ => ThemeMode.System
        };
        ApplyAccentPalette(preference, accentColor);

        if (persist)
        {
            FileLogger.Info($"Aparência alterada para {preference}; destaque {accentColor}.");
        }
    }

    internal static bool WindowsUsesDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private void ApplyAccentPalette(AppearancePreference preference, AccentColorPreference accentColor)
    {
        var mode = AppearancePaletteResolver.ResolveMode(
            preference,
            WindowsUsesDarkMode(),
            SystemParameters.HighContrast);
        var palette = AppearancePaletteResolver.Resolve(accentColor, mode);
        if (palette.UsesSystemColors)
        {
            Resources["GuiaPlayAccentBrush"] = SystemColors.HighlightBrush;
            Resources["GuiaPlayAccentForegroundBrush"] = SystemColors.HighlightTextBrush;
            Resources["GuiaPlayAccentSubtleBrush"] = SystemColors.ControlBrush;
            Resources["GuiaPlayAccentBorderBrush"] = SystemColors.HighlightBrush;
            Resources["GuiaPlayWindowBackgroundBrush"] = SystemColors.WindowBrush;
            Resources["GuiaPlaySurfacePrimaryBrush"] = SystemColors.WindowBrush;
            Resources["GuiaPlaySurfaceSecondaryBrush"] = SystemColors.ControlBrush;
            Resources["GuiaPlaySurfaceElevatedBrush"] = SystemColors.WindowBrush;
            Resources["GuiaPlaySidebarBrush"] = SystemColors.ControlBrush;
            Resources["GuiaPlayControlSurfaceBrush"] = SystemColors.ControlBrush;
            Resources["GuiaPlayControlHoverBrush"] = SystemColors.HighlightBrush;
            Resources["GuiaPlaySelectionBrush"] = SystemColors.HighlightBrush;
            Resources["GuiaPlaySelectionForegroundBrush"] = SystemColors.HighlightTextBrush;
            Resources["GuiaPlayDividerBrush"] = SystemColors.WindowTextBrush;
            Resources["GuiaPlayTextPrimaryBrush"] = SystemColors.WindowTextBrush;
            Resources["GuiaPlayTextSecondaryBrush"] = SystemColors.GrayTextBrush;
            return;
        }

        Resources["GuiaPlayAccentBrush"] = Brush(palette.AccentHex);
        Resources["GuiaPlayAccentForegroundBrush"] = Brush(palette.AccentForegroundHex);
        Resources["GuiaPlayAccentSubtleBrush"] = Brush(palette.AccentSubtleHex);
        Resources["GuiaPlayAccentBorderBrush"] = Brush(palette.AccentBorderHex);
        Resources["GuiaPlayWindowBackgroundBrush"] = Brush(palette.WindowBackgroundHex);
        Resources["GuiaPlaySurfacePrimaryBrush"] = Brush(palette.SurfacePrimaryHex);
        Resources["GuiaPlaySurfaceSecondaryBrush"] = Brush(palette.SurfaceSecondaryHex);
        Resources["GuiaPlaySurfaceElevatedBrush"] = Brush(palette.SurfaceElevatedHex);
        Resources["GuiaPlaySidebarBrush"] = Brush(palette.SidebarBackgroundHex);
        Resources["GuiaPlayControlSurfaceBrush"] = Brush(palette.ControlBackgroundHex);
        Resources["GuiaPlayControlHoverBrush"] = Brush(palette.ControlHoverHex);
        Resources["GuiaPlaySelectionBrush"] = Brush(palette.SelectionBackgroundHex);
        Resources["GuiaPlaySelectionForegroundBrush"] = Brush(palette.SelectionForegroundHex);
        Resources["GuiaPlayDividerBrush"] = Brush(palette.DividerHex);
        Resources["GuiaPlayTextPrimaryBrush"] = Brush(palette.TextPrimaryHex);
        Resources["GuiaPlayTextSecondaryBrush"] = Brush(palette.TextSecondaryHex);
    }

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    private void SystemEvents_OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) =>
        Dispatcher.BeginInvoke(() => ApplyAppearance(Settings.Appearance, Settings.AccentColor, persist: false));

    public bool UpdateSettings(Func<AppSettings, AppSettings> update, out string? error)
    {
        ArgumentNullException.ThrowIfNull(update);
        var candidate = update(Settings);
        if (!TrySaveSettings(candidate, out error))
        {
            return false;
        }

        Settings = candidate;
        return true;
    }

    public bool SaveSettings(out string? error) => TrySaveSettings(Settings, out error);

    private bool TrySaveSettings(AppSettings candidate, out string? error)
    {
        if (_settingsStore is null)
        {
            error = "O armazenamento de configurações ainda não foi inicializado.";
            return false;
        }

        var result = _settingsStore.Save(candidate);
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

    private async Task HandleForwardedRequestAsync(string? argument)
    {
        var request = argument is null
            ? null
            : await LaunchArgumentParser.ParseAsync([argument], TimeSpan.FromSeconds(4)).ConfigureAwait(false);
        await Dispatcher.InvokeAsync(async () =>
        {
            if (MainWindow is not MainWindow window)
            {
                return;
            }

            if (request is null)
            {
                window.BringToFront();
                return;
            }

            if (request.Warning is { } warning)
            {
                FileLogger.Info(warning);
                window.ShowExternalRequestStatus(warning);
                return;
            }

            await window.HandleExternalMediaAsync(request.MediaPath!);
        }).Task.Unwrap();
    }
}
