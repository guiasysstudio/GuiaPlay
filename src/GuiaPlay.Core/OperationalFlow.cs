using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

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

    public static async Task<LaunchArgumentResult> ParseAsync(
        IEnumerable<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        var candidate = arguments.FirstOrDefault(argument => !string.IsNullOrWhiteSpace(argument));
        if (candidate is null)
        {
            return new LaunchArgumentResult(null, null);
        }

        try
        {
            var fullPath = Path.GetFullPath(candidate);
            var status = await MediaFileProbe.ProbeAsync(fullPath, timeout, cancellationToken).ConfigureAwait(false);
            return status switch
            {
                MediaProbeStatus.Available => new LaunchArgumentResult(fullPath, null),
                MediaProbeStatus.Missing => new LaunchArgumentResult(null, "O arquivo recebido pela linha de comando não foi encontrado."),
                MediaProbeStatus.Unsupported => new LaunchArgumentResult(null, "O formato recebido pela linha de comando não é suportado."),
                MediaProbeStatus.TimedOut => new LaunchArgumentResult(null, "O caminho recebido pela linha de comando não respondeu dentro do tempo limite."),
                _ => new LaunchArgumentResult(null, "O caminho recebido pela linha de comando é inválido.")
            };
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

public sealed record SingleInstanceEndpoint(string MutexName, string PipeName);

public sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly SingleInstanceEndpoint[] _endpoints;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Channel<string?> _requests = Channel.CreateBounded<string?>(new BoundedChannelOptions(64)
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
        FullMode = BoundedChannelFullMode.Wait
    });
    private readonly List<Mutex> _mutexes = [];
    private readonly List<Task> _listeners = [];
    private Task? _processor;
    private Func<string?, Task>? _requestHandler;
    private bool _started;
    private bool _disposed;

    public SingleInstanceCoordinator(string mutexName, string pipeName)
        : this(mutexName, pipeName, [])
    {
    }

    public SingleInstanceCoordinator(
        string mutexName,
        string pipeName,
        IEnumerable<SingleInstanceEndpoint> compatibilityEndpoints)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(compatibilityEndpoints);
        _endpoints = [new SingleInstanceEndpoint(mutexName, pipeName), .. compatibilityEndpoints];
        if (_endpoints.Any(endpoint => string.IsNullOrWhiteSpace(endpoint.MutexName) || string.IsNullOrWhiteSpace(endpoint.PipeName)) ||
            _endpoints.Select(endpoint => endpoint.MutexName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != _endpoints.Length ||
            _endpoints.Select(endpoint => endpoint.PipeName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != _endpoints.Length)
        {
            throw new ArgumentException("Os endpoints de instância única precisam ter nomes válidos e distintos.", nameof(compatibilityEndpoints));
        }
    }

    public bool IsListening => _listeners.Any(listener => !listener.IsCompleted);
    public event Action<Exception>? RequestFailed;

    public async Task<SingleInstanceStartResult> StartAsync(
        string? mediaPath,
        Func<string?, Task> requestHandler,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(requestHandler);
        if (_started)
        {
            throw new InvalidOperationException("O coordenador de instância única já foi iniciado.");
        }

        _started = true;
        try
        {
            foreach (var endpoint in _endpoints)
            {
                var mutex = new Mutex(initiallyOwned: false, endpoint.MutexName, out var createdNew);
                if (!createdNew)
                {
                    try
                    {
                        await ForwardAsync(endpoint.PipeName, mediaPath, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        mutex.Dispose();
                        DisposeMutexes();
                    }

                    return SingleInstanceStartResult.Forwarded;
                }

                _mutexes.Add(mutex);
            }
        }
        catch
        {
            DisposeMutexes();
            throw;
        }

        _requestHandler = requestHandler;
        _processor = Task.Run(ProcessRequestsAsync);
        foreach (var endpoint in _endpoints)
        {
            _listeners.Add(Task.Run(() => ListenAsync(endpoint.PipeName)));
        }

        return SingleInstanceStartResult.Primary;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _requests.Writer.TryComplete();
        _shutdown.Cancel();
        DisposeMutexes();
        var tasks = _listeners.Concat(new[] { _processor }.OfType<Task>()).ToArray();
        if (tasks.Length == 0)
        {
            _shutdown.Dispose();
            return;
        }

        _ = CompleteDisposalAsync(tasks);
    }

    private async Task CompleteDisposalAsync(Task[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Listener failures have already been surfaced through RequestFailed when actionable.
        }
        finally
        {
            _shutdown.Dispose();
        }
    }

    private void DisposeMutexes()
    {
        foreach (var mutex in _mutexes)
        {
            mutex.Dispose();
        }

        _mutexes.Clear();
    }

    private static async Task ForwardAsync(string pipeName, string? mediaPath, CancellationToken cancellationToken)
    {
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000, cancellationToken).ConfigureAwait(false);
        await using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: false) { AutoFlush = true };
        await writer.WriteLineAsync(
            JsonSerializer.Serialize(new SingleInstanceRequest(mediaPath)).AsMemory(),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ListenAsync(string pipeName)
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_shutdown.Token).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                var line = await reader.ReadLineAsync(_shutdown.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(line) &&
                    JsonSerializer.Deserialize<SingleInstanceRequest>(line) is { } request &&
                    _requestHandler is not null)
                {
                    await _requests.Writer.WriteAsync(request.MediaPath, _shutdown.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                return;
            }
            catch (ChannelClosedException) when (_shutdown.IsCancellationRequested)
            {
                return;
            }
            catch (IOException) when (!_shutdown.IsCancellationRequested)
            {
                // The next loop creates a fresh server after a transient client failure.
            }
            catch (JsonException exception) when (!_shutdown.IsCancellationRequested)
            {
                RequestFailed?.Invoke(exception);
            }
        }
    }

    private async Task ProcessRequestsAsync()
    {
        try
        {
            await foreach (var mediaPath in _requests.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
            {
                if (_requestHandler is not { } handler)
                {
                    continue;
                }

                try
                {
                    await handler(mediaPath).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    RequestFailed?.Invoke(exception);
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
    }

    private sealed record SingleInstanceRequest(string? MediaPath);
}
