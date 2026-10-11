using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Filio.Models;
using Filio.Services;
using Xunit;

namespace Filio.Tests;

/// <summary>
/// 2.9.8 - עדכון אוטומטי ברקע. בדיקות טהורות (בלי רשת, בלי נגיעה בהגדרות/AppData של המשתמש):
/// אימות ההורדה (גודל, MZ, sha256), רשימת המארחים המותרים, פענוח digest של GitHub, "הרגע הבטוח"
/// ומיגרציית ברירת המחדל של AutoInstallUpdates.
/// </summary>
public class AutoUpdateTests : IDisposable
{
    private const string Hex64 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "filio-autoupdate-tests-" + Guid.NewGuid().ToString("N"));

    public AutoUpdateTests() => Directory.CreateDirectory(_tmp);

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch { }
    }

    private string WriteFile(string name, byte[] bytes)
    {
        var path = Path.Combine(_tmp, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] FakeInstaller(int size = 4096)
    {
        var b = new byte[size];
        b[0] = (byte)'M'; b[1] = (byte)'Z';
        for (var i = 2; i < size; i++) b[i] = (byte)(i * 7);
        return b;
    }

    // ---------- digest ----------

    [Fact]
    public void ParseSha256Digest_AcceptsGitHubFormat_AndLowercases()
        => Assert.Equal(Hex64, UpdateService.ParseSha256Digest("sha256:" + Hex64.ToUpperInvariant()));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("md5:0123456789abcdef0123456789abcdef")]
    [InlineData("sha256:abcd")]
    [InlineData("sha256:zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void ParseSha256Digest_RejectsAnythingElse(string? input)
        => Assert.Null(UpdateService.ParseSha256Digest(input));

    [Fact]
    public void GitHubReleaseAsset_DeserializesDigestField()
    {
        const string json = "{\"name\":\"Filio-Setup-2.9.8.exe\",\"browser_download_url\":\"https://github.com/x/y.exe\",\"size\":10,\"digest\":\"sha256:" + Hex64 + "\"}";
        var asset = JsonSerializer.Deserialize<GitHubReleaseAsset>(json);
        Assert.NotNull(asset);
        Assert.Equal(Hex64, UpdateService.ParseSha256Digest(asset!.Digest));
    }

    // ---------- trusted host ----------

    [Theory]
    [InlineData("https://github.com/ofirshudari1-ship-it/filio/releases/download/v2.9.8/Filio-Setup-2.9.8.exe", true)]
    [InlineData("https://objects.githubusercontent.com/abc", true)]
    [InlineData("http://github.com/x.exe", false)]
    [InlineData("https://github.com.evil.example/x.exe", false)]
    [InlineData("https://notgithub.com/x.exe", false)]
    [InlineData("not a url", false)]
    [InlineData(null, false)]
    public void IsTrustedGitHubUrl_OnlyHttpsGitHubHosts(string? url, bool expected)
        => Assert.Equal(expected, UpdateService.IsTrustedGitHubUrl(url));

    [Fact]
    public async Task DownloadInstallerAsync_RefusesNonGitHubHost_WhenRequired()
    {
        var service = new UpdateService();
        await Assert.ThrowsAsync<InsecureUpdateUrlException>(() =>
            service.DownloadInstallerAsync("https://example.com/Filio-Setup.exe", requireGitHubHost: true));
    }

    // ---------- VerifyInstallerFile ----------

    [Fact]
    public void Verify_Ok_WithSizeMzAndSha()
    {
        var bytes = FakeInstaller();
        var path = WriteFile("ok.exe", bytes);
        var sha = Convert.ToHexString(SHA256.HashData(bytes));
        Assert.Null(UpdateService.VerifyInstallerFile(path, bytes.Length, sha));
        Assert.Null(UpdateService.VerifyInstallerFile(path, bytes.Length, sha.ToLowerInvariant()));
    }

    [Fact]
    public void Verify_Ok_WhenNeitherSizeNorShaPublished()
        => Assert.Null(UpdateService.VerifyInstallerFile(WriteFile("old.exe", FakeInstaller()), null, null));

    [Fact]
    public void Verify_RejectsSizeMismatch()
        => Assert.NotNull(UpdateService.VerifyInstallerFile(WriteFile("s.exe", FakeInstaller()), 4095, null));

    [Fact]
    public void Verify_RejectsWrongSha256()
        => Assert.Contains("sha256", UpdateService.VerifyInstallerFile(WriteFile("h.exe", FakeInstaller()), null, Hex64));

    [Fact]
    public void Verify_RejectsHtmlPageMasqueradingAsInstaller()
    {
        var html = System.Text.Encoding.UTF8.GetBytes("<html>" + new string('x', 3000) + "</html>");
        Assert.Contains("not a Windows executable", UpdateService.VerifyInstallerFile(WriteFile("page.exe", html), null, null));
    }

    [Fact]
    public void Verify_RejectsTinyMzStub()
        => Assert.NotNull(UpdateService.VerifyInstallerFile(WriteFile("tiny.exe", new byte[] { (byte)'M', (byte)'Z', 0, 0 }), null, null));

    [Fact]
    public void Verify_ReportsMissingFile()
        => Assert.Equal("file missing", UpdateService.VerifyInstallerFile(Path.Combine(_tmp, "nope.exe"), null, null));

    // ---------- safe moment ----------

    [Fact]
    public void Gate_SafeOnlyWhenIdleLongEnough()
    {
        var now = DateTime.UtcNow;
        Assert.True(UpdateGate.IsSafeToInstall(300, false, false, now, DateTime.MinValue));
        Assert.False(UpdateGate.IsSafeToInstall(10, false, false, now, DateTime.MinValue));
    }

    [Fact]
    public void Gate_NeverDuringSessionEndOrActiveInstall()
    {
        var now = DateTime.UtcNow;
        Assert.False(UpdateGate.IsSafeToInstall(99999, true, false, now, DateTime.MinValue));
        Assert.False(UpdateGate.IsSafeToInstall(99999, false, true, now, DateTime.MinValue));
    }

    [Fact]
    public void Gate_RespectsDeclineBackoff()
    {
        var now = DateTime.UtcNow;
        Assert.False(UpdateGate.IsSafeToInstall(99999, false, false, now, now + UpdateGate.DeclineBackoff));
        Assert.True(UpdateGate.IsSafeToInstall(99999, false, false, now, now.AddSeconds(-1)));
    }

    [Fact]
    public void Gate_Constants()
    {
        Assert.Equal(TimeSpan.FromHours(6), UpdateGate.CheckInterval);
        Assert.Equal(TimeSpan.FromHours(24), UpdateGate.DeclineBackoff);
        Assert.True(UpdateGate.GetIdleSeconds() >= 0);
    }

    // ---------- default + migration ----------

    [Fact]
    public void NewSettings_HaveAutoInstallOn()
    {
        var s = AppSettings.CreateDefault();
        Assert.True(s.AutoInstallUpdates);
        Assert.True(s.AutoCheckForUpdates);
    }

    [Fact]
    public void Migration_FlipsLegacyFalse_ForSettingsFileWithoutMarkerKey()
    {
        // what 2.9.7 wrote: explicit false (the old default), and no AutoInstallDefaultMigrated key
        const string legacyJson = "{\"AutoCheckForUpdates\":true,\"AutoInstallUpdates\":false}";
        var loaded = JsonSerializer.Deserialize<AppSettings>(legacyJson)!;
        Assert.False(loaded.AutoInstallUpdates);

        Assert.True(SettingsService.MigrateLegacyAutoInstallDefault(loaded, legacyJson));
        Assert.True(loaded.AutoInstallUpdates);
    }

    [Fact]
    public void Migration_NeverOverridesAnExplicitChoiceMadeAfterwards()
    {
        // 2.9.8+ file: marker key present, user explicitly switched the setting off
        const string newJson = "{\"AutoInstallUpdates\":false,\"AutoInstallDefaultMigrated\":true}";
        var loaded = JsonSerializer.Deserialize<AppSettings>(newJson)!;

        Assert.False(SettingsService.MigrateLegacyAutoInstallDefault(loaded, newJson));
        Assert.False(loaded.AutoInstallUpdates);
    }
}
