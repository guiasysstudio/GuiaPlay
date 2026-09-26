using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using GuiaPlay.Core;

namespace GuiaPlay.App.Services;

internal sealed class UpdateManager
{
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private readonly App _app;
    private readonly GitHubUpdateService _service;
    private readonly SemaphoreSlim _checkGate = new(1, 1);

    public UpdateManager(App app)
    {
        _app = app;
        _service = new GitHubUpdateService(HttpClient, ProductInfo.RepositoryOwner, ProductInfo.RepositoryName);
    }

    public event EventHandler? StateChanged;

    public bool IsChecking { get; private set; }
    public UpdateCheckResult? LastResult { get; private set; }

    public async Task<UpdateCheckResult?> CheckAsync(bool manual, CancellationToken cancellationToken = default)
    {
        var settings = _app.Settings;
        var now = DateTimeOffset.UtcNow;
        if (!UpdateSchedule.ShouldCheck(manual, settings.CheckUpdatesAutomatically, settings.LastUpdateCheckUtc, now))
        {
            return LastResult;
        }

        await _checkGate.WaitAsync(cancellationToken);
        try
        {
            IsChecking = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
            FileLogger.Info($"UpdateChecker: iniciando consulta {(manual ? "manual" : "automática")}.");
            var currentVersion = ProductVersion.Parse(ProductInfo.Version);
            LastResult = await _service.CheckAsync(currentVersion, ProductInfo.Channel, cancellationToken);
            _ = _app.UpdateSettings(current => current with { LastUpdateCheckUtc = now }, out _);
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
        try
        {
            FileLogger.Info($"Updater: baixando {asset.Name}.");
            var downloader = new UpdatePackageDownloader(HttpClient);
            await downloader.DownloadAndVerifyAsync(asset.DownloadUrl, packagePath, manifest.Package.Sha256, cancellationToken);
            FileLogger.Info("Updater: SHA-256 validado; extraindo staging seguro.");
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
            _ = Process.Start(start) ?? throw new InvalidOperationException("O processo do updater não pôde ser iniciado.");
            FileLogger.Info("Updater temporário iniciado; o GuiaPlay será encerrado.");
            return (true, "Atualização preparada. O GuiaPlay será reiniciado.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            TryDeleteDirectory(operationRoot);
            FileLogger.Error("Falha ao preparar atualização.", exception);
            return (false, $"Não foi possível preparar a atualização: {exception.Message}");
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
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
