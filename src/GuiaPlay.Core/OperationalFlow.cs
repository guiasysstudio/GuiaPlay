using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace GuiaPlay.Core;

public sealed record OperatorIndicatorState(string Text, bool IsWarning, bool IsInteractive)
{
    public static OperatorIndicatorState Create(
        string? currentLabel,
        bool hasSavedOperator,
        bool savedOperatorConnected,
        bool configurationRequired)
    {
        if (string.IsNullOrWhiteSpace(currentLabel))
        {
            return new OperatorIndicatorState("Nenhuma tela", true, false);
        }

        var suffix = configurationRequired
            ? hasSavedOperator && !savedOperatorConnected
                ? " • operador salvo desconectado"
                : " • provisório"
            : string.Empty;
        return new OperatorIndicatorState(currentLabel + suffix, configurationRequired, false);
    }
}

public enum PlaylistActivationAction
{
    None,
    LoadOnly,
    LoadAndPlay
}

public static class PlaylistActivationPolicy
{
    public static PlaylistActivationAction Resolve(MediaKind kind, bool isAvailable, int selectedVideoOutputs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(selectedVideoOutputs);
        if (!isAvailable || kind == MediaKind.Unknown)
        {
            return PlaylistActivationAction.None;
        }

        return kind == MediaKind.Audio || selectedVideoOutputs > 0
            ? PlaylistActivationAction.LoadAndPlay
            : PlaylistActivationAction.LoadOnly;
    }
}

public sealed record LaunchArgumentResult(string? MediaPath, string? Warning)
{
    public bool HasMedia => MediaPath is not null;
}

public static class LaunchArgumentParser
{
    public static LaunchArgumentResult Parse(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var candidate = arguments.FirstOrDefault(argument => !string.IsNullOrWhiteSpace(argument));
        if (candidate is null)
        {
            return new LaunchArgumentResult(null, null);
        }

        try
        {
            var fullPath = Path.GetFullPath(candidate);
            if (!File.Exists(fullPath))
            {
                return new LaunchArgumentResult(null, "O arquivo recebido pela linha de comando não foi encontrado.");
            }

            if (MediaTypeDetector.Detect(fullPath) == MediaKind.Unknown)
            {
                return new LaunchArgumentResult(null, "O formato recebido pela linha de comando não é suportado.");
            }

            return new LaunchArgumentResult(fullPath, null);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new LaunchArgumentResult(null, $"O caminho recebido pela linha de comando é inválido: {exception.Message}");
        }
    }
}

public enum SingleInstanceStartResult
{
    Primary,
    Forwarded
}

public sealed class SingleInstanceCoordinator(string mutexName, string pipeName) : IDisposable
{
    private readonly string _mutexName = mutexName;
    private readonly string _pipeName = pipeName;
    private readonly CancellationTokenSource _shutdown = new();
    private Mutex? _mutex;
    private Task? _listener;
    private Func<string?, Task>? _requestHandler;
    private bool _disposed;

    public bool IsListening => _listener is not null && !_listener.IsCompleted;

    public async Task<SingleInstanceStartResult> StartAsync(
        string? mediaPath,
        Func<string?, Task> requestHandler,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(requestHandler);
        _mutex = new Mutex(initiallyOwned: false, _mutexName, out var createdNew);
        if (!createdNew)
        {
            await ForwardAsync(mediaPath, cancellationToken).ConfigureAwait(false);
            _mutex.Dispose();
            _mutex = null;
            return SingleInstanceStartResult.Forwarded;
        }

        _requestHandler = requestHandler;
        _listener = Task.Run(ListenAsync);
        return SingleInstanceStartResult.Primary;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        _mutex?.Dispose();
        _shutdown.Dispose();
    }

    private async Task ForwardAsync(string? mediaPath, CancellationToken cancellationToken)
    {
        using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000, cancellationToken).ConfigureAwait(false);
        await using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: false) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(new SingleInstanceRequest(mediaPath))).ConfigureAwait(false);
    }

    private async Task ListenAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_shutdown.Token).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                var line = await reader.ReadLineAsync(_shutdown.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(line) &&
                    JsonSerializer.Deserialize<SingleInstanceRequest>(line) is { } request &&
                    _requestHandler is { } handler)
                {
                    _ = Task.Run(() => handler(request.MediaPath));
                }
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                return;
            }
            catch (IOException) when (!_shutdown.IsCancellationRequested)
            {
                // The next loop creates a fresh server after a transient client failure.
            }
        }
    }

    private sealed record SingleInstanceRequest(string? MediaPath);
}
