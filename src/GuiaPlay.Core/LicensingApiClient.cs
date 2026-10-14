using System.Net.Http.Json;
using System.Text.Json;

namespace GuiaPlay.Core;

internal sealed record LicensingDeviceContext(
    string DeviceId,
    string DeviceName,
    string Platform,
    string AppVersion);

internal sealed class LicensingApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public LicensingApiClient(HttpClient? httpClient = null)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient
        {
            BaseAddress = LicensingProtocol.ApiBaseUri,
            Timeout = TimeSpan.FromSeconds(10)
        };

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = LicensingProtocol.ApiBaseUri;
        }

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"GuiaPlay/{ProductInfo.Version}");
        }
    }

    public async Task<ProjectLicensingConfiguration> GetProjectConfigAsync(CancellationToken cancellationToken)
    {
        var root = await PostAsync(
            "/api/v1/project/config",
            new { integrationCode = LicensingProtocol.IntegrationCode },
            cancellationToken);

        return LicensingJson.ParseProject(RequiredObject(root, "project"));
    }

    public async Task<LicensingServerView> StartTrialAsync(
        LicensingDeviceContext device,
        string requestId,
        CancellationToken cancellationToken)
    {
        var root = await PostAsync(
            "/api/v1/trial/start",
            new
            {
                integrationCode = LicensingProtocol.IntegrationCode,
                deviceId = device.DeviceId,
                deviceName = device.DeviceName,
                platform = device.Platform,
                appVersion = device.AppVersion,
                requestId
            },
            cancellationToken);

        return LicensingJson.ParseServerView(RequiredObject(root, "trial"), LicensingAccessKind.Trial);
    }

    public async Task<LicensingServerView> ValidateTrialAsync(
        LicensingDeviceContext device,
        string requestId,
        CancellationToken cancellationToken)
    {
        var root = await PostAsync(
            "/api/v1/trial/validate",
            new
            {
                integrationCode = LicensingProtocol.IntegrationCode,
                deviceId = device.DeviceId,
                appVersion = device.AppVersion,
                requestId
            },
            cancellationToken);

        return LicensingJson.ParseServerView(RequiredObject(root, "trial"), LicensingAccessKind.Trial);
    }

    public async Task<LicensingServerView> ActivateLicenseAsync(
        LicensingDeviceContext device,
        string licenseKey,
        string requestId,
        CancellationToken cancellationToken)
    {
        var root = await PostAsync(
            "/api/v1/license/activate",
            new
            {
                integrationCode = LicensingProtocol.IntegrationCode,
                licenseKey,
                deviceId = device.DeviceId,
                deviceName = device.DeviceName,
                platform = device.Platform,
                appVersion = device.AppVersion,
                requestId
            },
            cancellationToken);

        return LicensingJson.ParseServerView(RequiredObject(root, "license"), LicensingAccessKind.License);
    }

    public async Task<LicensingServerView> ValidateLicenseAsync(
        LicensingDeviceContext device,
        string licenseKey,
        string requestId,
        CancellationToken cancellationToken)
    {
        var root = await PostAsync(
            "/api/v1/license/validate",
            new
            {
                integrationCode = LicensingProtocol.IntegrationCode,
                licenseKey,
                deviceId = device.DeviceId,
                appVersion = device.AppVersion,
                requestId
            },
            cancellationToken);

        return LicensingJson.ParseServerView(RequiredObject(root, "license"), LicensingAccessKind.License);
    }

    public async Task DeactivateLicenseAsync(
        LicensingDeviceContext device,
        string licenseKey,
        string requestId,
        CancellationToken cancellationToken)
    {
        var root = await PostAsync(
            "/api/v1/license/deactivate",
            new
            {
                integrationCode = LicensingProtocol.IntegrationCode,
                licenseKey,
                deviceId = device.DeviceId,
                requestId
            },
            cancellationToken);

        var result = RequiredObject(root, "result");
        if (!result.TryGetProperty("deactivated", out var deactivated) ||
            deactivated.ValueKind != JsonValueKind.True)
        {
            throw new LicensingApiException(
                "invalid_response",
                "O servidor não confirmou a desativação da licença.");
        }
    }

    private async Task<JsonElement> PostAsync(
        string relativePath,
        object payload,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, relativePath)
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            };
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            JsonElement root;
            try
            {
                using var document = JsonDocument.Parse(body);
                root = document.RootElement.Clone();
            }
            catch (JsonException exception)
            {
                throw new LicensingApiException(
                    "invalid_response",
                    "O serviço de licenças retornou uma resposta inválida.",
                    (int)response.StatusCode,
                    exception);
            }

            if (!response.IsSuccessStatusCode ||
                !root.TryGetProperty("ok", out var ok) ||
                ok.ValueKind != JsonValueKind.True)
            {
                var errorCode = OptionalString(root, "error") ?? "invalid_response";
                var message = OptionalString(root, "message") ?? LicensingErrorMessages.For(errorCode);
                throw new LicensingApiException(errorCode, message, (int)response.StatusCode);
            }

            return root;
        }
        catch (LicensingApiException)
        {
            throw;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LicensingApiException(
                "network_error",
                "A conexão com o serviço de licenças expirou.",
                null,
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new LicensingApiException(
                "network_error",
                "Não foi possível acessar o serviço de licenças.",
                null,
                exception);
        }
    }

    private static JsonElement RequiredObject(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Object)
        {
            throw new LicensingApiException(
                "invalid_response",
                $"O serviço de licenças não retornou o objeto {propertyName}.");
        }

        return property;
    }

    private static string? OptionalString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }
}
