using System.Globalization;
using System.Text.Json;

namespace GuiaPlay.Core;

public static class LicensingProtocol
{
    public const string Version = "GSL-v1";
    public const string IntegrationCode = "GSLI-8TPY-5KA6-HK4J";
    public const string ProjectId = "prj_04d2013877c14fdc8d85";
    public const string LicensePrefix = "GPL";

    public static Uri ApiBaseUri { get; } = new("https://licencas.guiasys.online");
    public static Uri PurchaseUri { get; } = new("https://licencas.guiasys.online/");
}

public enum LicensingAccessKind
{
    None,
    Trial,
    License
}

public sealed record LicensingSigningConfiguration(
    string Algorithm,
    string KeyId,
    string Kty,
    string Curve,
    string X,
    string Y);

public sealed record LicensingTrialPolicy(
    bool Enabled,
    int Days,
    int ValidationHours,
    int OfflineHours);

public sealed record LicensingLicensePolicy(
    int ValidationHours,
    int OfflineDays);

public sealed record ProjectLicensingConfiguration(
    string ProtocolVersion,
    string ProjectId,
    string IntegrationCode,
    string Name,
    string Prefix,
    LicensingSigningConfiguration Signing,
    LicensingTrialPolicy Trial,
    LicensingLicensePolicy License,
    DateTimeOffset ServerTime);

public sealed record LicensingServerView(
    LicensingAccessKind AccessKind,
    string Status,
    string? LicenseId,
    string? CustomerName,
    string? CustomerEmail,
    string? PlanName,
    DateTimeOffset? IssuedAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? ExpiresAt,
    bool Lifetime,
    int MaxDevices,
    int DurationDays,
    int ValidationHours,
    int OfflineDays,
    int OfflineHours,
    int RemainingSeconds,
    DateTimeOffset ServerTime,
    DateTimeOffset OfflineUntil,
    string EntitlementToken);

public sealed record LicensingSnapshot(
    bool HasAccess,
    LicensingAccessKind AccessKind,
    string Status,
    string? ErrorCode,
    string? Message,
    string? LicenseKey,
    string? CustomerName,
    string? CustomerEmail,
    string? PlanName,
    DateTimeOffset? StartedAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? ExpiresAt,
    bool Lifetime,
    int? MaxDevices,
    int? TrialDurationDays,
    DateTimeOffset? LastOnlineValidationUtc,
    DateTimeOffset? OfflineUntil,
    bool UsingOfflineAuthorization)
{
    public static LicensingSnapshot Checking { get; } = new(
        false,
        LicensingAccessKind.None,
        "checking",
        null,
        "Verificando licença…",
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        false,
        null,
        null,
        null,
        null,
        false);

    public static LicensingSnapshot Unlicensed(string? errorCode = null, string? message = null) => new(
        false,
        LicensingAccessKind.None,
        "unlicensed",
        errorCode,
        message,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        false,
        null,
        null,
        null,
        null,
        false);
}

public sealed class LicensingApiException(
    string errorCode,
    string message,
    int? statusCode = null,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
    public int? StatusCode { get; } = statusCode;
}

public static class LicensingErrorMessages
{
    public static string For(string? errorCode) => errorCode switch
    {
        "invalid_request" => "Os dados enviados para o servidor de licenças são inválidos.",
        "project_not_found" => "O GuiaPlay não foi encontrado no serviço de licenças.",
        "project_inactive" => "O GuiaPlay está temporariamente desativado no serviço de licenças.",
        "trial_unavailable" => "A avaliação gratuita não está disponível para este produto.",
        "trial_not_started" => "A avaliação gratuita ainda não foi iniciada neste dispositivo.",
        "trial_expired" => "O período de avaliação deste dispositivo terminou.",
        "trial_converted" => "A avaliação deste dispositivo já foi convertida em uma licença paga.",
        "license_invalid" => "A chave informada é inválida.",
        "pending" => "A licença ainda não foi ativada.",
        "expired" => "A licença expirou.",
        "suspended" => "A licença está suspensa.",
        "revoked" => "A licença foi revogada.",
        "device_limit" => "O limite de dispositivos desta licença foi atingido.",
        "device_not_authorized" => "Este dispositivo não está autorizado para esta licença.",
        "device_bound_to_other_license" => "Este dispositivo já está vinculado a outra licença.",
        "origin_not_allowed" => "A origem deste aplicativo não está autorizada.",
        "rate_limited" => "Muitas solicitações foram feitas. Aguarde um pouco e tente novamente.",
        "network_error" => "Não foi possível acessar o serviço de licenças.",
        "clock_rollback" => "O relógio do computador aparenta ter sido retrocedido. Conecte-se à internet para validar a licença.",
        "offline_window_expired" => "O período permitido sem conexão terminou. Conecte-se à internet para validar a licença.",
        "entitlement_invalid" => "A autorização offline armazenada não pôde ser validada.",
        _ => "Não foi possível validar a licença do GuiaPlay."
    };
}

internal static class LicensingJson
{
    public static ProjectLicensingConfiguration ParseProject(JsonElement root)
    {
        var protocol = RequiredString(root, "protocolVersion");
        var projectId = RequiredString(root, "projectId");
        var integrationCode = RequiredString(root, "integrationCode");
        if (!string.Equals(protocol, LicensingProtocol.Version, StringComparison.Ordinal) ||
            !string.Equals(projectId, LicensingProtocol.ProjectId, StringComparison.Ordinal) ||
            !string.Equals(integrationCode, LicensingProtocol.IntegrationCode, StringComparison.Ordinal))
        {
            throw new InvalidDataException("A configuração recebida não pertence à integração GSL-v1 do GuiaPlay.");
        }

        var signing = root.GetProperty("signing");
        var publicJwk = signing.GetProperty("publicJwk");
        var signingConfiguration = new LicensingSigningConfiguration(
            RequiredString(signing, "algorithm"),
            RequiredString(signing, "keyId"),
            RequiredString(publicJwk, "kty"),
            RequiredString(publicJwk, "crv"),
            RequiredString(publicJwk, "x"),
            RequiredString(publicJwk, "y"));

        if (!string.Equals(signingConfiguration.Algorithm, "ES256", StringComparison.Ordinal) ||
            !string.Equals(signingConfiguration.Kty, "EC", StringComparison.Ordinal) ||
            !string.Equals(signingConfiguration.Curve, "P-256", StringComparison.Ordinal))
        {
            throw new InvalidDataException("O algoritmo de assinatura retornado pelo servidor não é compatível com o GuiaPlay.");
        }

        var trial = root.GetProperty("trial");
        var license = root.GetProperty("license");
        return new ProjectLicensingConfiguration(
            protocol,
            projectId,
            integrationCode,
            RequiredString(root, "name"),
            RequiredString(root, "prefix"),
            signingConfiguration,
            new LicensingTrialPolicy(
                ReadBoolean(trial, "enabled"),
                Math.Max(0, ReadInt(trial, "days")),
                Math.Max(1, ReadInt(trial, "validationHours")),
                Math.Max(0, ReadInt(trial, "offlineHours"))),
            new LicensingLicensePolicy(
                Math.Max(1, ReadInt(license, "validationHours")),
                Math.Max(0, ReadInt(license, "offlineDays"))),
            RequiredDate(root, "serverTime"));
    }

    public static LicensingServerView ParseServerView(JsonElement root, LicensingAccessKind accessKind)
    {
        var expectedType = accessKind == LicensingAccessKind.License ? "license" : "trial";
        if (!string.Equals(RequiredString(root, "protocolVersion"), LicensingProtocol.Version, StringComparison.Ordinal) ||
            !string.Equals(RequiredString(root, "projectId"), LicensingProtocol.ProjectId, StringComparison.Ordinal) ||
            !string.Equals(RequiredString(root, "integrationCode"), LicensingProtocol.IntegrationCode, StringComparison.Ordinal) ||
            !string.Equals(RequiredString(root, "type"), expectedType, StringComparison.Ordinal))
        {
            throw new InvalidDataException("A autorização recebida não pertence ao GuiaPlay.");
        }

        var entitlement = root.GetProperty("entitlement");
        var offlineUntil = RequiredDate(root, "offlineUntil");
        return new LicensingServerView(
            accessKind,
            RequiredString(root, "status"),
            OptionalString(root, "licenseId"),
            OptionalString(root, "customerName"),
            OptionalString(root, "customerEmail"),
            OptionalString(root, "planName"),
            OptionalDate(root, "issuedAt"),
            OptionalDate(root, "activatedAt"),
            OptionalDate(root, "startedAt"),
            OptionalDate(root, "expiresAt"),
            ReadBoolean(root, "lifetime"),
            Math.Max(0, ReadInt(root, "maxDevices")),
            Math.Max(0, ReadInt(root, "durationDays")),
            Math.Max(1, ReadInt(root, "validationHours")),
            Math.Max(0, ReadInt(root, "offlineDays")),
            Math.Max(0, ReadInt(root, "offlineHours")),
            Math.Max(0, ReadInt(root, "remainingSeconds")),
            RequiredDate(root, "serverTime"),
            offlineUntil,
            RequiredString(entitlement, "token"));
    }

    public static DateTimeOffset RequiredDate(JsonElement element, string propertyName)
    {
        var value = RequiredString(element, propertyName);
        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            throw new InvalidDataException($"Data inválida no campo {propertyName}.");
        }

        return parsed.ToUniversalTime();
    }

    public static DateTimeOffset? OptionalDate(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var value = property.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            throw new InvalidDataException($"Data inválida no campo {propertyName}.");
        }

        return parsed.ToUniversalTime();
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidDataException($"Campo obrigatório ausente: {propertyName}.");
        }

        return property.GetString()!;
    }

    private static string? OptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static int ReadInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : 0;

    private static bool ReadBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        property.GetBoolean();
}
