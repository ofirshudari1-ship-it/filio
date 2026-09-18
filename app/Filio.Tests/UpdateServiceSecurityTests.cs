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
