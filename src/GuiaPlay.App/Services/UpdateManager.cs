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

internal sealed class UpdateManager
{
    private static readonly HttpClient QueryHttpClient = CreateHttpClient(TimeSpan.FromSeconds(20));
    private static readonly HttpClient DownloadHttpClient = CreateHttpClient(Timeout.InfiniteTimeSpan);
    private readonly App _app;
    private readonly GitHubUpdateService _service;
    private readonly SemaphoreSlim _checkGate = new(1, 1);

    public UpdateManager(App app)
    {
        _app = app;
        _service = new GitHubUpdateService(QueryHttpClient, ProductInfo.RepositoryOwner, ProductInfo.RepositoryName);
        LastResult = PersistedUpdateCache.Restore(
            app.Settings,
            ProductVersion.Parse(ProductInfo.Version),
            DateTimeOffset.UtcNow);
    }

    public event EventHandler? StateChanged;

    public bool IsChecking { get; private set; }
    public bool IsPreparing { get; private set; }
    public UpdateCheckResult? LastResult { get; private set; }
    public UpdatePreparationProgress PreparationProgress { get; private set; } = new(UpdatePreparationStage.None);

    public async Task<UpdateCheckResult?> CheckAsync(bool manual, CancellationToken cancellationToken = default)
    {
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

            IsChecking = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
            FileLogger.Info($"UpdateChecker: iniciando consulta {(manual ? "manual" : "automática")}.");
            LastResult = await _service.CheckAsync(currentVersion, ProductInfo.Channel, cancellationToken);
            _ = _app.UpdateSettings(
                current => PersistedUpdateCache.Record(current, currentVersion, LastResult, now),
                out _);
            FileLogger.Info($"UpdateChecker: resultado {LastResult.Status}; versão encontrada {LastResult.Release?.Version.ToString() ?? "nenhuma"}.");
            return LastResult;
        }
        finally
        {
            IsChecking = false;
            _checkGate.Release();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task<(bool Launched, string Message)> PrepareAndLaunchInstallerAsync(CancellationToken cancellationToken = default)
    {
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

        var asset = release.Assets.First(item => string.Equals(item.Name, manifest.Package.AssetName, StringComparison.OrdinalIgnoreCase));
        var operationRoot = Path.Combine(Path.GetTempPath(), "GuiaPlayUpdate", Guid.NewGuid().ToString("N"));
        var packagePath = Path.Combine(operationRoot, manifest.Package.AssetName);
        var stagingPath = Path.Combine(operationRoot, "staging");
        var backupPath = Path.Combine(Path.GetTempPath(), "GuiaPlayBackup", $"{ProductInfo.Version}-to-{manifest.Version}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(operationRoot);
        IsPreparing = true;
        try
        {
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
            if (!await PackageIntegrity.VerifySha256Async(packagePath, manifest.Package.Sha256, cancellationToken))
            {
                throw new InvalidDataException("O SHA-256 do pacote baixado não confere com o manifesto.");
            }

            FileLogger.Info("Updater: SHA-256 validado; extraindo staging seguro.");
            SetPreparationProgress(new UpdatePreparationProgress(UpdatePreparationStage.PreparingFiles));
            await Task.Run(() => SafeZipExtractor.Extract(packagePath, stagingPath), cancellationToken);

            var installedUpdater = Path.Combine(installation.RootDirectory, "GuiaPlay.Updater.exe");
            if (!File.Exists(installedUpdater)) throw new FileNotFoundException("O executável do updater não está instalado.", installedUpdater);
            var updaterTempRoot = Path.Combine(Path.GetTempPath(), "GuiaPlayUpdater", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(updaterTempRoot);
            var updaterTemp = Path.Combine(updaterTempRoot, "GuiaPlay.Updater.exe");
            File.Copy(installedUpdater, updaterTemp);

            var start = new ProcessStartInfo(updaterTemp) { UseShellExecute = false, WorkingDirectory = updaterTempRoot };
            start.ArgumentList.Add($"--process-id={Environment.ProcessId}");
            start.ArgumentList.Add($"--staging={stagingPath}");
            start.ArgumentList.Add($"--target={installation.RootDirectory}");
            start.ArgumentList.Add("--launch=GuiaPlay.exe");
            start.ArgumentList.Add($"--backup={backupPath}");
            SetPreparationProgress(new UpdatePreparationProgress(UpdatePreparationStage.StartingUpdater));
            _ = Process.Start(start) ?? throw new InvalidOperationException("O processo do updater não pôde ser iniciado.");
            FileLogger.Info("Updater temporário iniciado; o GuiaPlay será encerrado.");
            return (true, "Atualização preparada. O GuiaPlay será reiniciado.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryDeleteDirectory(operationRoot);
            FileLogger.Info("Download da atualização cancelado pelo usuário.");
            return (false, "Download da atualização cancelado.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            TryDeleteDirectory(operationRoot);
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

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
