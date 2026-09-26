using System.Net.Http.Headers;
using System.Text.Json;

namespace GuiaPlay.Core;

public sealed class GitHubUpdateService(HttpClient httpClient, string owner, string repository)
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly Uri _releasesUri = new($"https://api.github.com/repos/{owner}/{repository}/releases?per_page=50");

    public async Task<UpdateCheckResult> CheckAsync(
        ProductVersion currentVersion,
        UpdateChannel channel,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _releasesUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheckResult.Failure($"GitHub respondeu com HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var releases = await JsonSerializer.DeserializeAsync<GitHubRelease[]>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                           ?? [];
            var latest = ReleaseSelector.SelectLatest(releases, channel);
            if (latest is null)
            {
                return new UpdateCheckResult(UpdateCheckStatus.NoPublishedVersion);
            }

            if (latest.Version <= currentVersion)
            {
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate, latest);
            }

            var manifestAsset = latest.Assets.FirstOrDefault(asset =>
                string.Equals(asset.Name, "update-manifest.json", StringComparison.OrdinalIgnoreCase));
            if (manifestAsset is null)
            {
                return UpdateCheckResult.Failure("A release não contém update-manifest.json.");
            }

            using var manifestResponse = await _httpClient.GetAsync(manifestAsset.DownloadUrl, cancellationToken).ConfigureAwait(false);
            if (!manifestResponse.IsSuccessStatusCode)
            {
                return UpdateCheckResult.Failure($"Não foi possível baixar o manifesto (HTTP {(int)manifestResponse.StatusCode}).");
            }

            var json = await manifestResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!UpdateManifest.TryParse(json, out var manifest, out var error) || manifest is null ||
                !string.Equals(manifest.Version, latest.Version.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return UpdateCheckResult.Failure($"Manifesto de atualização inválido: {error ?? "versão divergente"}");
            }

            if (!latest.Assets.Any(asset => string.Equals(asset.Name, manifest.Package.AssetName, StringComparison.OrdinalIgnoreCase)))
            {
                return UpdateCheckResult.Failure("O pacote indicado pelo manifesto não está anexado à release.");
            }

            return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, latest, manifest);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return UpdateCheckResult.Failure("A consulta excedeu o tempo limite.");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or NotSupportedException)
        {
            return UpdateCheckResult.Failure($"Não foi possível consultar as atualizações: {exception.Message}");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
