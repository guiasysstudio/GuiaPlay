using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GuiaPlay.App.Models;
using GuiaPlay.App.Services;
using GuiaPlay.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace GuiaPlay.App.Windows;

internal sealed record YouTubePlaybackSnapshot(
    string State,
    bool Ready,
    long ElapsedMilliseconds,
    long DurationMilliseconds,
    int? ErrorCode,
    string? Message);

/// <summary>
/// One visible official YouTube IFrame player, hosted on one public monitor.
/// The operator only receives state via a verified WebView2 origin; no frame
/// capture, media extraction, duplicate hidden player, or LibVLC forwarding.
/// </summary>
internal sealed class YouTubeOutputWindow : Window
{
    private const string Host = "guiaplay-assets.guiasys.online";
    private const string HostOrigin = "https://" + Host;
    private readonly WebView2 _browser;
    private bool _closed;
    private bool _ready;
    private string _state = "AGUARDANDO";
    private long _elapsed;
    private long _duration;
    private int? _error;
    private readonly DispatcherTimer _startupTimer = new() { Interval = TimeSpan.FromSeconds(25) };

    public YouTubeOutputWindow(MonitorInfo? monitor)
    {
        Monitor = monitor;
        _startupTimer.Tick += (_, _) =>
        {
            _startupTimer.Stop();
            if (!_closed && !_ready)
            {
                ReportFailure("O YouTube não respondeu em 25 segundos. Verifique conexão, " +
                    "WebView2 e acesso à API do YouTube.");
            }
        };
        Title = monitor is null
            ? "GuiaPlay — YouTube (player oficial)"
            : "GuiaPlay — YouTube (saída pública)";
        WindowStyle = monitor is null ? WindowStyle.SingleBorderWindow : WindowStyle.None;
        ResizeMode = monitor is null ? ResizeMode.CanResize : ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = true;
        Topmost = monitor is not null;
        if (monitor is null)
        {
            Width = 1000;
            Height = 650;
            MinWidth = 640;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        Background = Brushes.Black;

        _browser = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            DefaultBackgroundColor = System.Drawing.Color.Black
        };
        Content = _browser;
        if (monitor is not null)
        {
            SourceInitialized += (_, _) => WindowPlacement.FillMonitor(this, monitor);
        }
        Closed += (_, _) =>
        {
            _closed = true;
            _startupTimer.Stop();
            if (_browser.CoreWebView2 is { } core)
            {
                core.WebMessageReceived -= Browser_WebMessageReceived;
            }

            _ready = false;
            _browser.Dispose();
        };
    }

    public MonitorInfo? Monitor { get; private set; }
    public bool Ready => _ready;
    public event EventHandler<YouTubePlaybackSnapshot>? PlaybackStateChanged;

    public async Task InitializeAsync(YouTubeVideoReference video)
    {
        ArgumentNullException.ThrowIfNull(video);
        if (_closed)
        {
            throw new InvalidOperationException("A janela pública foi encerrada.");
        }

        var localFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "YouTube");
        if (!File.Exists(Path.Combine(localFolder, "player.html")))
        {
            throw new FileNotFoundException("O recurso do player oficial não foi instalado.");
        }

        var userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GuiaSys", "GuiaPlay", "WebView2-YouTube");
        Directory.CreateDirectory(userData);
        // The NuGet SDK does not install the WebView2 Runtime on Windows.
        var runtimeVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
        FileLogger.Info($"YouTube WebView2 Runtime: {runtimeVersion}; vídeo: {video.VideoId}.");
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
        if (_closed)
        {
            return;
        }

        await _browser.EnsureCoreWebView2Async(environment);
        if (_closed || _browser.CoreWebView2 is not { } core)
        {
            return;
        }

        core.Settings.IsWebMessageEnabled = true;
        core.Settings.IsScriptEnabled = true;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = true; // do not mask native player actions
        core.Settings.IsStatusBarEnabled = false;
        core.SetVirtualHostNameToFolderMapping(
            Host, localFolder, CoreWebView2HostResourceAccessKind.DenyCors);

        core.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            // User-initiated YouTube/Google links are opened in the system
            // browser; never create another unsupervised player inside the app.
            if (args.IsUserInitiated &&
                Uri.TryCreate(args.Uri, UriKind.Absolute, out var destination) &&
                destination.Scheme == Uri.UriSchemeHttps &&
                destination.Host is "www.youtube.com" or "youtube.com" or "accounts.google.com")
            {
                try
                {
                    Process.Start(new ProcessStartInfo(destination.AbsoluteUri)
                    {
                        UseShellExecute = true
                    });
                }
                catch (Exception)
                {
                    // The browser link is optional; playback remains in place.
                }
            }
        };
        core.NavigationStarting += (_, args) =>
        {
            if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var candidate) ||
                !string.Equals(candidate.Host, Host, StringComparison.OrdinalIgnoreCase) ||
                candidate.Scheme != Uri.UriSchemeHttps ||
                !candidate.AbsolutePath.Equals("/player.html", StringComparison.Ordinal))
            {
                args.Cancel = true;
            }
        };
        core.AddWebResourceRequestedFilter(
            "https://www.youtube.com/embed/*", CoreWebView2WebResourceContext.Document);
        core.WebResourceRequested += (_, args) =>
        {
            // Provide the actual application-owned embedding origin for WebView2,
            // whose referer may otherwise be absent (YouTube IFrame error 153).
            if (args.Request.Uri.StartsWith("https://www.youtube.com/embed/", StringComparison.OrdinalIgnoreCase))
            {
                args.Request.Headers.SetHeader("Referer", HostOrigin + "/");
            }
        };
        core.NavigationCompleted += (_, args) =>
        {
            if (!_closed && !args.IsSuccess)
            {
                ReportFailure("Página do YouTube não carregou (" +
                    args.WebErrorStatus + "). Verifique conexão e bloqueios de rede.");
            }
        };
        core.ProcessFailed += (_, args) =>
        {
            if (!_closed)
            {
                ReportFailure("Processo WebView2 falhou (" +
                    args.ProcessFailedKind + "). Abra novamente o vídeo.");
            }
        };
        core.WebMessageReceived += Browser_WebMessageReceived;
        _startupTimer.Start();
        core.Navigate(HostOrigin + "/player.html?videoId=" +
            Uri.EscapeDataString(video.VideoId));
    }

    public void Command(string action, double? value = null)
    {
        if (_closed || !_ready || _browser.CoreWebView2 is not { } core)
        {
            return;
        }

        // Only constant command verbs are accepted; never script-evaluate user URLs.
        if (action is not ("play" or "pause" or "stop" or "seek" or
                          "volume" or "mute" or "unmute" or "status"))
        {
            return;
        }

        core.PostWebMessageAsJson(JsonSerializer.Serialize(new { action, seconds = value, value }));
    }

    public void Reposition(MonitorInfo monitor)
    {
        if (Monitor is null)
        {
            return; // Operator player remains a freely movable window.
        }

        Monitor = monitor;
        WindowPlacement.FillMonitor(this, monitor);
    }

    private void ReportFailure(string message)
    {
        _startupTimer.Stop();
        _state = "ERRO";
        _error = null;
        FileLogger.Error("YouTube: " + message);
        PlaybackStateChanged?.Invoke(this,
            new YouTubePlaybackSnapshot(_state, _ready, _elapsed, _duration, null, message));
    }

    private void Browser_WebMessageReceived(
        object? sender, CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        if (_closed || !Uri.TryCreate(eventArgs.Source, UriKind.Absolute, out var source) ||
            source.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(source.Host, Host, StringComparison.OrdinalIgnoreCase) ||
            !source.AbsolutePath.Equals("/player.html", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            using var json = JsonDocument.Parse(eventArgs.WebMessageAsJson);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var typeValue) ||
                typeValue.ValueKind != JsonValueKind.String)
            {
                return;
            }

            var type = typeValue.GetString();
            string? message = null;
            switch (type)
            {
                case "ready":
                    _ready = true;
                    _startupTimer.Stop();
                    _state = "PRONTO";
                    break;
                case "position":
                    if (root.TryGetProperty("seconds", out var seconds) &&
                        seconds.ValueKind == JsonValueKind.Number &&
                        seconds.TryGetDouble(out var current) &&
                        double.IsFinite(current))
                    {
                        _elapsed = (long)Math.Clamp(current * 1000, 0, long.MaxValue / 2d);
                    }

                    if (root.TryGetProperty("durationSeconds", out var duration) &&
                        duration.ValueKind == JsonValueKind.Number &&
                        duration.TryGetDouble(out var total) && double.IsFinite(total))
                    {
                        _duration = (long)Math.Clamp(total * 1000, 0, long.MaxValue / 2d);
                    }

                    break;
                case "state":
                    if (root.TryGetProperty("state", out var state) &&
                        state.TryGetInt32(out var code))
                    {
                        _state = code switch
                        {
                            1 => "REPRODUZINDO",
                            2 => "PAUSADO",
                            0 => "CONCLUÍDO",
                            3 => "CARREGANDO",
                            5 => "PRONTO",
                            _ => "AGUARDANDO"
                        };
                    }

                    break;
                case "autoplayBlocked":
                    _state = "INTERAÇÃO NECESSÁRIA";
                    message = "O YouTube bloqueou o início por comando; clique em Reproduzir diretamente no player público.";
                    break;
                case "error":
                    _startupTimer.Stop();
                    _state = "ERRO";
                    if (root.TryGetProperty("code", out var error) &&
                        error.ValueKind == JsonValueKind.Number && error.TryGetInt32(out var err))
                    {
                        _error = err;
                    }

                    message = _error switch
                    {
                        2 => "Link ou identificador de vídeo inválido.",
                        100 => "Vídeo privado, removido ou indisponível.",
                        101 or 150 => "O proprietário não permite incorporar este vídeo.",
                        153 => "YouTube não recebeu identificação válida do aplicativo (Referer).",
                        5 => "O player HTML5 não conseguiu reproduzir este conteúdo.",
                        _ => "Falha na reprodução do YouTube. Verifique rede e permissões do vídeo."
                    };
                    break;
                default:
                    return;
            }

            if (type == "error")
            {
                FileLogger.Error($"YouTube IFrame erro {_error?.ToString() ?? "desconhecido"}: {message}");
            }

            PlaybackStateChanged?.Invoke(this, new YouTubePlaybackSnapshot(
                _state, _ready, _elapsed, _duration, _error, message));
        }
        catch (JsonException)
        {
            // Ignore any malformed messages rather than compromising native state.
        }
    }
}
