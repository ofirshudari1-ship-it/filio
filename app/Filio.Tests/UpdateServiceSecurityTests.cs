using System.IO;
using Filio.Models;
using Filio.Services;
using Xunit;

namespace Filio.Tests;

/// <summary>
/// עדכון שקט מוריד ומריץ קובץ הפעלה בלי שהמשתמש רואה כל שלב - הבדיקות האלה מכוונות
/// לעוגני האבטחה שמונעים ניצול של הזרימה הזו: HTTPS חובה, ואימות sha256 כשמוצהר.
/// לא נוגעות ברשת בפועל - רק בבדיקות הסינון שרצות לפני כל קריאת HTTP.
/// </summary>
public class UpdateServiceSecurityTests
{
    [Theory]
    [InlineData("http://example.com/feed.json")]
    [InlineData("ftp://example.com/feed.json")]
    [InlineData("not-a-url")]
    public async Task CheckForUpdateAsync_RejectsNonHttpsFeedUrl(string insecureUrl)
    {
        var service = new UpdateService();

        await Assert.ThrowsAsync<InsecureUpdateUrlException>(() => service.CheckForUpdateAsync(insecureUrl));
    }

    [Fact]
    public async Task DownloadInstallerAsync_RejectsNonHttpsInstallerUrl()
    {
        var service = new UpdateService();

        await Assert.ThrowsAsync<InsecureUpdateUrlException>(() =>
            service.DownloadInstallerAsync("http://example.com/FilioSetup.exe"));
    }

    // expectedSizeBytes is checked BEFORE EnsureHttps would even be reached in a real download
    // (HTTPS is validated first, same as today) - but here we confirm the HTTPS gate still
    // fires first regardless of whether a size was also supplied, so the new parameter can't
    // accidentally bypass the existing security check.
    [Fact]
    public async Task DownloadInstallerAsync_RejectsNonHttpsInstallerUrl_EvenWithExpectedSize()
    {
        var service = new UpdateService();

        await Assert.ThrowsAsync<InsecureUpdateUrlException>(() =>
            service.DownloadInstallerAsync("http://example.com/FilioSetup.exe", expectedSizeBytes: 12345));
    }
}

/// <summary>
/// בדיקות ל-UpdateService.SelectInstallerAsset - הבחירה הטהורה (בלי רשת) שמאתרת את
/// Filio-Setup-X.Y.Z.exe בין ה-assets המצורפים ל-GitHub Release, בשביל זרימת ה"עדכן עכשיו"
/// השקטה. אם לא נמצא asset מתאים, הזרימה חוזרת לקישור-לעמוד-הרלוונטי הישן.
/// </summary>
public class UpdateServiceInstallerAssetSelectionTests
{
    private static GitHubReleaseAsset Asset(string name, string url = "https://example.com/x", long size = 100) =>
        new() { Name = name, BrowserDownloadUrl = url, Size = size };

    [Fact]
    public void SelectInstallerAsset_ReturnsNull_WhenAssetsIsNull()
    {
        Assert.Null(UpdateService.SelectInstallerAsset(null));
    }

    [Fact]
    public void SelectInstallerAsset_ReturnsNull_WhenNoAssetsAtAll()
    {
        Assert.Null(UpdateService.SelectInstallerAsset(Array.Empty<GitHubReleaseAsset>()));
    }

    [Fact]
    public void SelectInstallerAsset_ReturnsNull_WhenNoAssetMatchesInstallerNaming()
    {
        var assets = new[] { Asset("source-code.zip"), Asset("README.txt"), Asset("checksums.sha256") };

        Assert.Null(UpdateService.SelectInstallerAsset(assets));
    }

    [Theory]
    [InlineData("Filio-Setup-2.9.4.exe")]
    [InlineData("filio-setup-2.9.4.exe")]
    [InlineData("FILIO-SETUP-2.9.4.EXE")]
    public void SelectInstallerAsset_FindsInstallerRegardlessOfCase(string assetName)
    {
        var expected = Asset(assetName, "https://example.com/Filio-Setup-2.9.4.exe", 78_000_000);
        var assets = new[] { Asset("source-code.zip"), expected };

        var result = UpdateService.SelectInstallerAsset(assets);

        Assert.NotNull(result);
        Assert.Equal(expected.BrowserDownloadUrl, result!.BrowserDownloadUrl);
        Assert.Equal(78_000_000, result.Size);
    }

    [Fact]
    public void SelectInstallerAsset_IgnoresAssetWithEmptyDownloadUrl()
    {
        var assets = new[] { Asset("Filio-Setup-2.9.4.exe", url: "") };

        Assert.Null(UpdateService.SelectInstallerAsset(assets));
    }
}

/// <summary>
/// בדיקות ל-UpdateService.TryConsumeUpdateFailedMarker - קובץ הסימון שכותב Setup.cs
/// (build\installer, פרויקט נפרד) כשהתקנה שקטה נכשלת, ו-Filio קורא בהפעלה הבאה. הבדיקה
/// כותבת/מוחקת ישירות מתחת לנתיב האמיתי (אין נקודת הזרקה לנתיב חלופי) ומנקה אחריה.
/// </summary>
public class UpdateServiceUpdateFailedMarkerTests
{
    [Fact]
    public void TryConsumeUpdateFailedMarker_ReturnsNull_WhenNoMarkerFileExists()
    {
        var path = UpdateService.GetUpdateFailedMarkerPath();
        if (File.Exists(path)) File.Delete(path);

        Assert.Null(UpdateService.TryConsumeUpdateFailedMarker());
    }

    [Fact]
    public void TryConsumeUpdateFailedMarker_ReadsAndDeletesTheFile()
    {
        var path = UpdateService.GetUpdateFailedMarkerPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "2026-09-21 silent install failed: disk full");

        var result = UpdateService.TryConsumeUpdateFailedMarker();

        Assert.Equal("2026-09-21 silent install failed: disk full", result);
        Assert.False(File.Exists(path), "the marker must be consumed (deleted) so the same failure isn't reported on every future launch");
    }
}

/// <summary>
/// בדיקות ל-UpdateService.TryParseTagVersion - הלוגיקה הטהורה שמנתחת tag_name של
/// GitHub Release (למשל "v2.9.1") לגרסה בת-השוואה, בלי לגעת ברשת.
/// </summary>
public class UpdateServiceGitHubTagParsingTests
{
    [Theory]
    [InlineData("v2.9.1", "2.9.1")]
    [InlineData("V2.9.1", "2.9.1")]
    [InlineData("2.9.1", "2.9.1")]
    [InlineData("v2.10.0", "2.10.0")]
    public void TryParseTagVersion_ParsesValidTags(string tag, string expected)
    {
        var success = UpdateService.TryParseTagVersion(tag, out var version);

        Assert.True(success);
        Assert.Equal(Version.Parse(expected), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    [InlineData("not-a-version")]
    [InlineData("vnext")]
    public void TryParseTagVersion_RejectsInvalidTags(string? tag)
    {
        var success = UpdateService.TryParseTagVersion(tag, out _);

        Assert.False(success);
    }
}
