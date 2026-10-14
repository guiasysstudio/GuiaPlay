using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GuiaPlay.Core;

public sealed record EntitlementVerificationResult(
    bool IsValid,
    string? ErrorCode,
    string? Status,
    DateTimeOffset? ServerTime,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? OfflineUntil);

public static class EntitlementVerifier
{
    private static readonly TimeSpan ClockRollbackTolerance = TimeSpan.FromMinutes(5);

    public static EntitlementVerificationResult Verify(
        string token,
        LicensingSigningConfiguration signing,
        LicensingAccessKind expectedAccessKind,
        string deviceId,
        DateTimeOffset nowUtc,
        DateTimeOffset? lastObservedUtc = null,
        bool enforceLocalTimeWindow = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentNullException.ThrowIfNull(signing);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
            {
                return Invalid("entitlement_invalid");
            }

            using var headerDocument = JsonDocument.Parse(Base64UrlDecode(parts[0]));
            using var payloadDocument = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            var header = headerDocument.RootElement;
            var payload = payloadDocument.RootElement;

            if (!StringPropertyEquals(header, "alg", "ES256") ||
                !StringPropertyEquals(header, "typ", "GSL-ENT") ||
                !StringPropertyEquals(header, "kid", signing.KeyId) ||
                !string.Equals(signing.Algorithm, "ES256", StringComparison.Ordinal) ||
                !string.Equals(signing.Kty, "EC", StringComparison.Ordinal) ||
                !string.Equals(signing.Curve, "P-256", StringComparison.Ordinal))
            {
                return Invalid("entitlement_invalid");
            }

            if (!VerifySignature(parts, signing))
            {
                return Invalid("entitlement_invalid");
            }

            var expectedType = expectedAccessKind == LicensingAccessKind.License ? "license" : "trial";
            var expectedDeviceHash = Sha256Hex(deviceId);
            if (!StringPropertyEquals(payload, "protocolVersion", LicensingProtocol.Version) ||
                !StringPropertyEquals(payload, "type", expectedType) ||
                !StringPropertyEquals(payload, "projectId", LicensingProtocol.ProjectId) ||
                !StringPropertyEquals(payload, "integrationCode", LicensingProtocol.IntegrationCode) ||
                !StringPropertyEquals(payload, "deviceHash", expectedDeviceHash) ||
                !StringPropertyEquals(payload, "status", "active"))
            {
                return Invalid("entitlement_invalid");
            }

            if (expectedAccessKind == LicensingAccessKind.License &&
                (!payload.TryGetProperty("licenseId", out var licenseId) ||
                 licenseId.ValueKind != JsonValueKind.String ||
                 string.IsNullOrWhiteSpace(licenseId.GetString())))
            {
                return Invalid("entitlement_invalid");
            }

            var serverTime = ReadRequiredDate(payload, "serverTime");
            var expiresAt = ReadOptionalDate(payload, "expiresAt");
            var offlineUntil = ReadRequiredDate(payload, "offlineUntil");

            if (expiresAt is { } expiry && expiry < serverTime)
            {
                return Invalid("entitlement_invalid", serverTime, expiresAt, offlineUntil);
            }

            if (offlineUntil < serverTime)
            {
                return Invalid("entitlement_invalid", serverTime, expiresAt, offlineUntil);
            }

            if (expiresAt is { } finalExpiry && offlineUntil > finalExpiry)
            {
                return Invalid("entitlement_invalid", serverTime, expiresAt, offlineUntil);
            }

            if (enforceLocalTimeWindow)
            {
                nowUtc = nowUtc.ToUniversalTime();
                if (nowUtc + ClockRollbackTolerance < serverTime ||
                    lastObservedUtc is { } observed && nowUtc + ClockRollbackTolerance < observed.ToUniversalTime())
                {
                    return Invalid("clock_rollback", serverTime, expiresAt, offlineUntil);
                }

                if (expiresAt is { } expires && nowUtc >= expires)
                {
                    return Invalid(
                        expectedAccessKind == LicensingAccessKind.Trial ? "trial_expired" : "expired",
                        serverTime,
                        expiresAt,
                        offlineUntil);
                }

                if (nowUtc >= offlineUntil)
                {
                    return Invalid("offline_window_expired", serverTime, expiresAt, offlineUntil);
                }
            }

            return new EntitlementVerificationResult(
                true,
                null,
                "active",
                serverTime,
                expiresAt,
                offlineUntil);
        }
        catch (Exception exception) when (
            exception is JsonException or
            FormatException or
            CryptographicException or
            InvalidDataException)
        {
            return Invalid("entitlement_invalid");
        }
    }

    public static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool VerifySignature(string[] parts, LicensingSigningConfiguration signing)
    {
        var x = Base64UrlDecode(signing.X);
        var y = Base64UrlDecode(signing.Y);
        if (x.Length != 32 || y.Length != 32)
        {
            return false;
        }

        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = x,
                Y = y
            }
        });

        var input = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        var signature = Base64UrlDecode(parts[2]);
        return signature.Length == 64 &&
               ecdsa.VerifyData(
                   input,
                   signature,
                   HashAlgorithmName.SHA256,
                   DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    private static bool StringPropertyEquals(JsonElement element, string propertyName, string expected) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String &&
        string.Equals(property.GetString(), expected, StringComparison.Ordinal);

    private static DateTimeOffset ReadRequiredDate(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(
                property.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            throw new InvalidDataException($"Data inválida no entitlement: {propertyName}.");
        }

        return parsed.ToUniversalTime();
    }

    private static DateTimeOffset? ReadOptionalDate(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(
                property.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            throw new InvalidDataException($"Data inválida no entitlement: {propertyName}.");
        }

        return parsed.ToUniversalTime();
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
        return Convert.FromBase64String(normalized);
    }

    private static EntitlementVerificationResult Invalid(
        string errorCode,
        DateTimeOffset? serverTime = null,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? offlineUntil = null) =>
        new(false, errorCode, null, serverTime, expiresAt, offlineUntil);
}
