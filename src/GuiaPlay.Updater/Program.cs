using System.Diagnostics;
using System.Globalization;
using GuiaPlay.Core;

return await UpdaterProgram.RunAsync(args);

internal static class UpdaterProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        var values = ParseArguments(args);
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GuiaSys",
            "GuiaPlay",
            "updater.log");
        try
        {
            Log(logPath, "Updater iniciado.");
            var processId = int.Parse(Required(values, "process-id"), CultureInfo.InvariantCulture);
            var staging = Required(values, "staging");
            var target = Required(values, "target");
            var launch = Required(values, "launch");
            var backup = Required(values, "backup");
            var expectedVersion = Required(values, "version");
            if (!string.Equals(launch, "GuiaPlay.exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("O updater só pode reiniciar o executável principal do GuiaPlay.");
            }

            var managed = ManagedInstallationDetector.Detect(Path.Combine(target, "GuiaPlay.exe"));
            if (managed is null || !string.Equals(Path.GetFullPath(managed.RootDirectory), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Destino não é uma instalação gerenciada válida.");
            }

            if (!InstallMarkerStore.TryRead(
                    Path.Combine(staging, "install.json"),
                    out var packageVersion,
                    out var packageRuntimeIdentifier) ||
                !string.Equals(packageVersion, expectedVersion, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(packageRuntimeIdentifier, managed.RuntimeIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "A versão ou a arquitetura do pacote não corresponde à atualização selecionada.");
            }

            try
            {
                using var process = Process.GetProcessById(processId);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
            }
            catch (ArgumentException)
            {
                // The main process already ended.
            }

            var result = UpdateApplicator.Apply(staging, target, backup);
            if (!result.Succeeded)
            {
                throw new IOException($"Atualização falhou. Rollback: {result.RolledBack}. {result.Error}");
            }

            var launchPath = Path.GetFullPath(Path.Combine(target, launch));
            if (!string.Equals(Path.GetDirectoryName(launchPath), Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(launchPath))
            {
                throw new InvalidOperationException("O executável principal atualizado não foi encontrado no destino esperado.");
            }

            _ = Process.Start(new ProcessStartInfo(launchPath) { UseShellExecute = true, WorkingDirectory = target })
                ?? throw new InvalidOperationException("O Windows não iniciou o GuiaPlay atualizado.");
            Log(logPath, $"Atualização aplicada; reinício solicitado. Backup preservado em {backup}.");
            _ = UpdateWorkspaceRetention.TryDeleteRecognizedDirectory(
                Path.GetDirectoryName(Path.GetFullPath(staging))!,
                UpdateWorkspaceKind.Update);
            var cleanup = UpdateWorkspaceRetention.Cleanup(Path.GetTempPath(), DateTimeOffset.UtcNow);
            Log(logPath, $"Retenção concluída: {cleanup.DeletedDirectories} pasta(s) removida(s), {cleanup.PreservedDirectories} preservada(s).");
            return 0;
        }
        catch (Exception exception)
        {
            Log(logPath, $"ERRO: {exception}");
            return 1;
        }
    }

    private static Dictionary<string, string> ParseArguments(IEnumerable<string> args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var argument in args)
        {
            var separator = argument.IndexOf('=');
            if (!argument.StartsWith("--", StringComparison.Ordinal) || separator < 3) continue;
            result[argument[2..separator]] = argument[(separator + 1)..].Trim('"');
        }

        return result;
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Argumento --{key} ausente.");

    private static void Log(string path, string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Logging must never hide the updater's real result or turn recovery into a crash.
        }
    }

}
