using System.IO;
using FileFox.Services;
using Xunit;

namespace FileFox.Tests;

public class CloudSyncDetectorTests
{
    [Theory]
    [InlineData(@"C:\Users\test\OneDrive\Documents", CloudSyncProvider.OneDrive)]
    [InlineData(@"C:\Users\test\OneDrive - Acme Corp\Invoices", CloudSyncProvider.OneDrive)]
    [InlineData(@"C:\Users\test\Dropbox\Clients", CloudSyncProvider.Dropbox)]
    [InlineData(@"C:\Users\test\Google Drive\Reports", CloudSyncProvider.GoogleDrive)]
    [InlineData(@"C:\Users\test\Downloads", CloudSyncProvider.None)]
    [InlineData(@"C:\Users\test\Documents\FileFox", CloudSyncProvider.None)]
    // תיקיות מקומיות רגילות ששמן רק *מתחיל* בשם ספק ענן - לא אמורות להיחשב תיקיית סנכרון
    [InlineData(@"C:\OneDriveBackup\Invoices", CloudSyncProvider.None)]
    [InlineData(@"C:\DropboxOldExports\file.pdf", CloudSyncProvider.None)]
    public void DetectProvider_RecognizesKnownCloudFolderPatterns(string path, CloudSyncProvider expected)
    {
        Assert.Equal(expected, CloudSyncDetector.DetectProvider(path));
    }

    [Fact]
    public void IsInsideCloudSyncFolder_TrueForOneDrivePath()
    {
        Assert.True(CloudSyncDetector.IsInsideCloudSyncFolder(@"C:\Users\test\OneDrive\Invoices"));
    }

    [Fact]
    public void IsInsideCloudSyncFolder_FalseForOrdinaryPath()
    {
        Assert.False(CloudSyncDetector.IsInsideCloudSyncFolder(@"C:\Users\test\Downloads"));
    }

    [Fact]
    public void IsCloudPlaceholder_FalseForOrdinaryLocalFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "FileFoxCloudTest_" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "just a normal local file");

        try
        {
            Assert.False(CloudSyncDetector.IsCloudPlaceholder(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IsCloudPlaceholder_FalseForMissingFile()
    {
        Assert.False(CloudSyncDetector.IsCloudPlaceholder(Path.Combine(Path.GetTempPath(), "does-not-exist.pdf")));
    }
}
