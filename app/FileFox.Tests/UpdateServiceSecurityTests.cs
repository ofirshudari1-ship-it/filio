using FileFox.Services;
using Xunit;

namespace FileFox.Tests;

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
            service.DownloadInstallerAsync("http://example.com/FileFoxSetup.exe"));
    }
}
