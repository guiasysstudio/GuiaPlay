using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GuiaPlay.Core;
using Xunit;

namespace GuiaPlay.Core.Tests;

public sealed class EntitlementVerifierTests
{
    private const string DeviceId = "gpd_test-device-001";

    [Fact]
    public void AcceptsValidLicenseEntitlementInsideOfflineWindow()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signing = SigningConfiguration(key, "kid-test");
        var serverTime = new DateTimeOffset(2026, 10, 13, 20, 0, 0, TimeSpan.Zero);
        var token = CreateToken(
            key,
            signing.KeyId,
            "license",
            DeviceId,
            serverTime,
            serverTime.AddDays(30),
            serverTime.AddDays(15),
            licenseId: "lic_test");

        var result = EntitlementVerifier.Verify(
            token,
            signing,
            LicensingAccessKind.License,
            DeviceId,
            serverTime.AddHours(2),
            serverTime.AddHours(1));

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorCode);
        Assert.Equal(serverTime.AddDays(30), result.ExpiresAt);
        Assert.Equal(serverTime.AddDays(15), result.OfflineUntil);
    }

    [Fact]
    public void RejectsTokenBoundToAnotherDevice()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signing = SigningConfiguration(key, "kid-test");
        var serverTime = new DateTimeOffset(2026, 10, 13, 20, 0, 0, TimeSpan.Zero);
        var token = CreateToken(
            key,
            signing.KeyId,
            "trial",
            "gpd_other-device",
            serverTime,
            serverTime.AddDays(7),
            serverTime.AddHours(64));

        var result = EntitlementVerifier.Verify(
            token,
            signing,
            LicensingAccessKind.Trial,
            DeviceId,
            serverTime.AddHours(1));

        Assert.False(result.IsValid);
        Assert.Equal("entitlement_invalid", result.ErrorCode);
    }

    [Fact]
    public void RejectsExpiredOfflineWindow()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signing = SigningConfiguration(key, "kid-test");
        var serverTime = new DateTimeOffset(2026, 10, 13, 20, 0, 0, TimeSpan.Zero);
        var offlineUntil = serverTime.AddHours(64);
        var token = CreateToken(
            key,
            signing.KeyId,
            "trial",
            DeviceId,
            serverTime,
            serverTime.AddDays(7),
            offlineUntil);

        var result = EntitlementVerifier.Verify(
            token,
            signing,
            LicensingAccessKind.Trial,
            DeviceId,
            offlineUntil);

        Assert.False(result.IsValid);
        Assert.Equal("offline_window_expired", result.ErrorCode);
    }

    [Fact]
    public void RejectsClockRollbackWhileOffline()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signing = SigningConfiguration(key, "kid-test");
        var serverTime = new DateTimeOffset(2026, 10, 13, 20, 0, 0, TimeSpan.Zero);
        var token = CreateToken(
            key,
            signing.KeyId,
            "license",
            DeviceId,
            serverTime,
            serverTime.AddDays(30),
            serverTime.AddDays(15),
            licenseId: "lic_test");

        var result = EntitlementVerifier.Verify(
            token,
            signing,
            LicensingAccessKind.License,
            DeviceId,
            serverTime.AddHours(1),
            serverTime.AddHours(4));

        Assert.False(result.IsValid);
        Assert.Equal("clock_rollback", result.ErrorCode);
    }

    [Fact]
    public void FreshOnlineValidationCanReanchorWrongLocalClock()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signing = SigningConfiguration(key, "kid-test");
        var serverTime = new DateTimeOffset(2026, 10, 13, 20, 0, 0, TimeSpan.Zero);
        var token = CreateToken(
            key,
            signing.KeyId,
            "license",
            DeviceId,
            serverTime,
            serverTime.AddDays(30),
            serverTime.AddDays(15),
            licenseId: "lic_test");

        var result = EntitlementVerifier.Verify(
            token,
            signing,
            LicensingAccessKind.License,
            DeviceId,
            serverTime.AddHours(-8),
            null,
            enforceLocalTimeWindow: false);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void RejectsTamperedPayload()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signing = SigningConfiguration(key, "kid-test");
        var serverTime = new DateTimeOffset(2026, 10, 13, 20, 0, 0, TimeSpan.Zero);
        var token = CreateToken(
            key,
            signing.KeyId,
            "license",
            DeviceId,
            serverTime,
            serverTime.AddDays(30),
            serverTime.AddDays(15),
            licenseId: "lic_test");
        var parts = token.Split('.');
        var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(Base64UrlDecode(parts[1]))!;
        payload["projectId"] = "prj_tampered";
        parts[1] = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));

        var result = EntitlementVerifier.Verify(
            string.Join('.', parts),
            signing,
            LicensingAccessKind.License,
            DeviceId,
            serverTime.AddHours(1));

        Assert.False(result.IsValid);
        Assert.Equal("entitlement_invalid", result.ErrorCode);
    }

    private static LicensingSigningConfiguration SigningConfiguration(ECDsa key, string keyId)
    {
        var parameters = key.ExportParameters(includePrivateParameters: false);
        return new LicensingSigningConfiguration(
            "ES256",
            keyId,
            "EC",
            "P-256",
            Base64UrlEncode(parameters.Q.X!),
            Base64UrlEncode(parameters.Q.Y!));
    }

    private static string CreateToken(
        ECDsa key,
        string keyId,
        string type,
        string deviceId,
        DateTimeOffset serverTime,
        DateTimeOffset? expiresAt,
        DateTimeOffset offlineUntil,
        string? licenseId = null)
    {
        var header = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            alg = "ES256",
            typ = "GSL-ENT",
            kid = keyId
        }));
        var payload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            protocolVersion = LicensingProtocol.Version,
            type,
            projectId = LicensingProtocol.ProjectId,
            integrationCode = LicensingProtocol.IntegrationCode,
            deviceHash = EntitlementVerifier.Sha256Hex(deviceId),
            status = "active",
            licenseId,
            planName = type == "license" ? "Teste" : null,
            lifetime = false,
            issuedAt = type == "license" ? serverTime.ToString("O") : null,
            activatedAt = type == "license" ? serverTime.ToString("O") : null,
            startedAt = type == "trial" ? serverTime.ToString("O") : null,
            expiresAt = expiresAt?.ToString("O"),
            serverTime = serverTime.ToString("O"),
            offlineUntil = offlineUntil.ToString("O")
        }));
        var input = Encoding.ASCII.GetBytes($"{header}.{payload}");
        var signature = key.SignData(
            input,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{header}.{payload}.{Base64UrlEncode(signature)}";
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
        return Convert.FromBase64String(normalized);
    }
}
