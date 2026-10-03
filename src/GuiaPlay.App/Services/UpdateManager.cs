using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using GuiaPlay.Core;

namespace GuiaPlay.App.Services;

internal enum UpdatePreparationStage
{
    None,
    Downloading,
    VerifyingIntegrity,
    PreparingFiles,
    StartingUpdater
}

internal sealed record UpdatePreparationProgress(
    UpdatePreparationStage Stage,
    long BytesReceived = 0,
    long? TotalBytes = null,
    int? Percentage = null);

internal sealed class UpdateManager : IDisposable
{
    private static readonly HttpClient QueryHttpClient = CreateHttpClient(TimeSpan.FromSeconds(20));
    private static readonly HttpClient DownloadHttpClient = CreateHttpClient(Timeout.InfiniteTimeSpan);
    private readonly App _app;
    private readonly GitHubUpdateService _service;
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private readonly StartupUpdateCheckCoordinator _startupCheck = new();
    private readonly CancellationTokenSource _shutdown = new();
    private bool _disposed;

    public UpdateManager(App app)
    {
        _app = app;
        _service = new GitHubUpdateService(QueryHttpClient, ProductInfo.RepositoryOwner, ProductInfo.RepositoryName);
        LastResult = PersistedUpdateCache.Restore(
            app.Settings,
            ProductVersion.Parse(ProductInfo.Version),
            DateTimeOffset.UtcNow);
        if (LastResult is { } cached)
        {
            FileLogger.Info($"Update startup: cache restored {cached.Release?.Version.ToString() ?? cached.Status.ToString()}");
        }

        try
        {
            var cleanup = UpdateWorkspaceRetention.Cleanup(Path.GetTempPath(), DateTimeOffset.UtcNow);
            FileLogger.Info($"Updater: retenção temporária removeu {cleanup.DeletedDirectories} pasta(s); {cleanup.PreservedDirectories} preservada(s).");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            FileLogger.Error("Updater: não foi possível concluir a retenção temporária.", exception);
        }
    }

    public event EventHandler? StateChanged;

    public bool IsChecking { get; private set; }
    public bool IsPreparing { get; private set; }
    public UpdateCheckResult? LastResult { get; private set; }
    public UpdatePreparationProgress PreparationProgress { get; private set; } = new(UpdatePreparationStage.None);

    public async Task<UpdateCheckResult?> CheckOnStartupAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return LastResult;
        }

        if (!_app.Settings.CheckUpdatesAutomatically)
        {
            FileLogger.Info("Update startup: automatic disabled");
            return LastResult;
        }

        var task = _startupCheck.Start(
            automaticEnabled: true,
            async () =>
            {
                FileLogger.Info("Update startup: automatic enabled");
                FileLogger.Info("Update startup: querying GitHub");
                var result = await QueryGitHubAsync("startup", _shutdown.Token).ConfigureAwait(false);
                FileLogger.Info("Update startup: query completed");
                var detail = result.Release?.Version.ToString();
                FileLogger.Info(result.Status switch
                {
                    UpdateCheckStatus.UpdateAvailable => $"Update startup: result UpdateAvailable {detail}",
                    UpdateCheckStatus.UpToDate => "Update startup: result UpToDate",
                    UpdateCheckStatus.Failed => $"Update startup: failed {result.Error ?? "unknown reason"}",
                    _ => $"Update startup: result {result.Status}"
                });
                return result;
            });

        if (task is null)
        {
            return LastResult;
        }

        try
        {
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            return LastResult;
        }
    }

    public async Task<UpdateCheckResult?> CheckAsync(bool manual, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        cancellationToken = linkedCancellation.Token;
        var settings = _app.Settings;
        var now = DateTimeOffset.UtcNow;
        var currentVersion = ProductVersion.Parse(ProductInfo.Version);
        if (!UpdateSchedule.ShouldCheck(manual, settings.CheckUpdatesAutomatically, settings, currentVersion, now))
        {
            return LastResult;
        }

        await _checkGate.WaitAsync(cancellationToken);
        try
        {
            settings = _app.Settings;
            now = DateTimeOffset.UtcNow;
            if (!UpdateSchedule.ShouldCheck(manual, settings.CheckUpdatesAutomatically, settings, currentVersion, now))
            {
                return LastResult;
            }

            FileLogger.Info($"UpdateChecker: iniciando consulta {(manual ? "manual" : "automática")}.");
            return await QueryGitHubInsideGateAsync(currentVersion, now, manual ? "manual" : "automatic", cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _checkGate.Release();
        }
    }

    private async Task<UpdateCheckResult> QueryGitHubAsync(string context, CancellationToken cancellationToken)
    {
        await _checkGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await QueryGitHubInsideGateAsync(
                    ProductVersion.Parse(ProductInfo.Version),
                    DateTimeOffset.UtcNow,
                    context,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _checkGate.Release();
        }
    }

    private async Task<UpdateCheckResult> QueryGitHubInsideGateAsync(
        ProductVersion currentVersion,
        DateTimeOffset now,
        string context,
        CancellationToken cancellationToken)
    {
        IsChecking = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            var result = await _service.CheckAsync(currentVersion, ProductInfo.Channel, cancellationToken).ConfigureAwait(false);
            var visibleResult = UpdateResultRetention.SelectVisibleResult(LastResult, result);
            if (!ReferenceEquals(visibleResult, result))
            {
                FileLogger.Info($"UpdateChecker: falha em {context}; atualização conhecida mantida no cache.");
                LastResult = visibleResult;
                return result;
            }

            LastResult = visibleResult;
            if (result.Status != UpdateCheckStatus.Failed)
            {
                _ = _app.UpdateSettings(
                    current => PersistedUpdateCache.Record(current, currentVersion, result, now),
                    out _);
            }

            FileLogger.Info($"UpdateChecker: resultado {result.Status}; versão encontrada {result.Release?.Version.ToString() ?? "nenhuma"}.");
            return result;
        }
        finally
        {
            IsChecking = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task<(bool Launched, string Message)> PrepareAndLaunchInstallerAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        cancellationToken = linkedCancellation.Token;
        if (LastResult is { Status: UpdateCheckStatus.UpdateAvailable, Manifest: null })
        {
            await CheckAsync(manual: true, cancellationToken);
        }

        if (LastResult is not { Status: UpdateCheckStatus.UpdateAvailable, Release: { } release, Manifest: { } manifest })
        {
            return (false, "Nenhuma atualização pronta para instalar.");
        }

        var executable = Environment.ProcessPath;
        var installation = executable is null ? null : ManagedInstallationDetector.Detect(executable);
        if (installation is null)
        {
            return (false, "A instalação automática é destinada à versão instalada pelo GuiaPlay Setup.");
        }

        if (!string.Equals(installation.RuntimeIdentifier, UpdateRuntimeIdentifier.Current, StringComparison.OrdinalIgnoreCase))
        {
            return (false, "A arquitetura registrada da instalação não corresponde ao processo em execução.");
        }

        if (!manifest.TrySelectPackage(installation.RuntimeIdentifier, out var selectedPackage) || selectedPackage is null)
        {
            return (false, $"A release não contém pacote para {installation.RuntimeIdentifier}.");
        }

        var asset = release.Assets.FirstOrDefault(item =>
            string.Equals(item.Name, selectedPackage.AssetName, StringComparison.OrdinalIgnoreCase));
        if (asset is null)
        {
            return (false, "O pacote selecionado não está anexado à release.");
        }

        string? operationRoot = null;
        string? backupPath = null;
        string? updaterTempRoot = null;
        IsPreparing = true;
        try
        {
            var temporaryDirectory = Path.GetTempPath();
            operationRoot = UpdateWorkspaceRetention.CreateDirectory(
                temporaryDirectory,
                UpdateWorkspaceKind.Update,
                Guid.NewGuid().ToString("N"));
            backupPath = UpdateWorkspaceRetention.CreateDirectory(
                temporaryDirectory,
                UpdateWorkspaceKind.Backup,
                $"{ProductInfo.Version}-to-{manifest.Version}-{Guid.NewGuid():N}");
            var packagePath = Path.Combine(operationRoot, selectedPackage.AssetName);
            var stagingPath = Path.Combine(operationRoot, "staging");
            FileLogger.Info($"Updater: baixando {asset.Name}.");
            SetPreparationProgress(new UpdatePreparationProgress(UpdatePreparationStage.Downloading));
            var progress = new Progress<UpdateDownloadProgress>(value => SetPreparationProgress(
                new UpdatePreparationProgress(
                    UpdatePreparationStage.Downloading,
                    value.BytesReceived,
                    value.TotalBytes,
                    value.Percentage)));
            var downloader = new UpdatePackageDownloader(DownloadHttpClient);
            await downloader.DownloadAsync(asset.DownloadUrl, packagePath, progress, cancellationToken);
            SetPreparationProgress(new UpdatePreparationProgress(UpdatePreparationStage.VerifyingIntegrity));
            if (!await PackageIntegrity.VerifySha256Async(packagePath, selectedPackage.Sha256, cancellationToken))
            {
                throw new InvalidDataException("O SHA-256 do pacote baixado não confere com o manifesto.");
            }

            FileLogger.Info("Updater: SHA-256 validado; extraindo staging seguro.");
            SetPreparationProgress(new UpdatePreparationProgress(UpdatePreparationStage.PreparingFiles));
            await Task.Run(() => SafeZipExtractor.Extract(packagePath, stagingPath), cancellationToken);
            if (!InstallMarkerStore.TryRead(
                    Path.Combine(stagingPath, "install.json"),
                    out var stagedVersion,
                    out var stagedRuntimeIdentifier) ||
                !string.Equals(stagedVersion, manifest.Version, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(stagedRuntimeIdentifier, installation.RuntimeIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "O marcador do pacote não corresponde à versão e à arquitetura selecionadas.");
            }

            var installedUpdater = Path.Combine(installation.RootDirectory, "GuiaPlay.Updater.exe");
            if (!File.Exists(installedUpdater)) throw new FileNotFoundException("O executável do updater não está instalado.", installedUpdater);
            updaterTempRoot = UpdateWorkspaceRetention.CreateDirectory(
                temporaryDirectory,
                UpdateWorkspaceKind.Updater,
                Guid.NewGuid().ToString("N"));
            var updaterTemp = Path.Combine(updaterTempRoot, "GuiaPlay.Updater.exe");
            File.Copy(installedUpdater, updaterTemp);

            var start = new ProcessStartInfo(updaterTemp) { UseShellExecute = false, WorkingDirectory = updaterTempRoot };
            start.ArgumentList.Add($"--process-id={Environment.ProcessId}");
            start.ArgumentList.Add($"--staging={stagingPath}");
            start.ArgumentList.Add($"--target={installation.RootDirectory}");
            start.ArgumentList.Add("--launch=GuiaPlay.exe");
            start.ArgumentList.Add($"--backup={backupPath}");
            start.ArgumentList.Add($"--version={manifest.Version}");
            SetPreparationProgress(new UpdatePreparationProgress(UpdatePreparationStage.StartingUpdater));
            var launchResult = SafeProcessLauncher.TryStart(start);
            if (!launchResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"O processo do updater não pôde ser iniciado: {launchResult.Error}");
            }

            FileLogger.Info("Updater temporário iniciado; o GuiaPlay será encerrado.");
            return (true, "Atualização preparada. O GuiaPlay será reiniciado.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CleanupFailedPreparation(operationRoot, updaterTempRoot, backupPath);
            FileLogger.Info("Download da atualização cancelado pelo usuário.");
            return (false, "Download da atualização cancelado.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            CleanupFailedPreparation(operationRoot, updaterTempRoot, backupPath);
            FileLogger.Error("Falha ao preparar atualização.", exception);
            return (false, $"Não foi possível preparar a atualização: {exception.Message}");
        }
        finally
        {
            IsPreparing = false;
            PreparationProgress = new UpdatePreparationProgress(UpdatePreparationStage.None);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetPreparationProgress(UpdatePreparationProgress progress)
    {
        PreparationProgress = progress;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static HttpClient CreateHttpClient(TimeSpan timeout)
    {
        var client = new HttpClient { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GuiaPlay", ProductInfo.Version));
        return client;
    }

    private static void CleanupFailedPreparation(string? operationRoot, string? updaterRoot, string? backupRoot)
    {
        if (operationRoot is not null)
            _ = UpdateWorkspaceRetention.TryDeleteRecognizedDirectory(operationRoot, UpdateWorkspaceKind.Update);
        if (updaterRoot is not null)
            _ = UpdateWorkspaceRetention.TryDeleteRecognizedDirectory(updaterRoot, UpdateWorkspaceKind.Updater);
        if (backupRoot is not null)
            _ = UpdateWorkspaceRetention.TryDeleteRecognizedDirectory(backupRoot, UpdateWorkspaceKind.Backup);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        _shutdown.Dispose();
    }
}
