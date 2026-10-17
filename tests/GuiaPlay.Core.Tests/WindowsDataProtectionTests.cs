using System.Security.Cryptography;
using GuiaPlay.Core;
using Xunit;

namespace GuiaPlay.Core.Tests;

public sealed class WindowsDataProtectionTests
{
    [Fact]
    public void NativeFreeDpapiRoundTripsWithoutPersistingPlaintext()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var plaintext = System.Text.Encoding.UTF8.GetBytes(
            "GuiaPlay DPAPI round-trip sensitive token test; not a real license");
        var protectedBytes = WindowsDataProtection.Protect(plaintext);
        Assert.NotEmpty(protectedBytes);
        Assert.NotEqual(plaintext, protectedBytes);

        var restored = WindowsDataProtection.Unprotect(protectedBytes);
        Assert.Equal(plaintext, restored);
        CryptographicOperations.ZeroMemory(restored);
        CryptographicOperations.ZeroMemory(plaintext);
    }

    [Fact]
    public void LicenseStoreStillEncryptsRoundTripsAndKeepsFileOpaque()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var folder = Path.Combine(Path.GetTempPath(), "guiaplay-dpapi-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "licensing.dat");
        try
        {
            var store = new LicensingStateStore(path);
            var testState = PersistedLicensingState.Empty with
            {
                LicenseKey = "DPAPI-ONLY-TEST-KEY",
                TrialConsumed = true
            };

            Assert.True(store.Save(testState));
            Assert.True(File.Exists(path));
            var storedBytes = File.ReadAllBytes(path);
            Assert.DoesNotContain("DPAPI-ONLY-TEST-KEY",
                System.Text.Encoding.UTF8.GetString(storedBytes), StringComparison.Ordinal);
            var loaded = store.Load();
            Assert.Equal(testState.LicenseKey, loaded.LicenseKey);
            Assert.True(loaded.TrialConsumed);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
