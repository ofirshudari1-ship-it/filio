using System.IO;
using FileFox.Models;
using FileFox.Services;
using Xunit;

namespace FileFox.Tests;

/// <summary>
/// בודק רק Export/Import - אלה היחידות שלוקחות נתיב מפורש כפרמטר, ולכן ניתנות לבדיקה
/// בלי לגעת ב-%AppData%\FileFox האמיתי (בניגוד ל-Load/Save שמשתמשים בנתיב קבוע).
/// </summary>
public class SettingsServiceExportImportTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "FileFoxSettingsIoTests_" + Guid.NewGuid().ToString("N"));

    public SettingsServiceExportImportTests() => Directory.CreateDirectory(_sandbox);

    public void Dispose()
    {
        if (Directory.Exists(_sandbox))
            Directory.Delete(_sandbox, recursive: true);
    }

    [Fact]
    public void ExportThenImport_RoundTripsAllValues()
    {
        var service = new SettingsService();
        var exportPath = Path.Combine(_sandbox, "export.json");

        var original = AppSettings.CreateDefault();
        original.RootOutputFolder = @"C:\Custom\Output";
        original.FolderOrder = FolderStructureOrder.TypeClientYear;
        original.Clients.Add(new ClientProfile { Name = "Acme", Keywords = "acme, ACME Ltd" });

        service.Export(original, exportPath);
        var imported = service.Import(exportPath);

        Assert.NotNull(imported);
        Assert.Equal(original.RootOutputFolder, imported!.RootOutputFolder);
        Assert.Equal(original.FolderOrder, imported.FolderOrder);
        Assert.Single(imported.Clients);
        Assert.Equal("Acme", imported.Clients[0].Name);
        Assert.Equal(original.DocTypeRules.Count, imported.DocTypeRules.Count);
    }

    [Fact]
    public void Export_ReturnsFalseInsteadOfThrowingWhenTargetDirectoryDoesNotExist()
    {
        // מדמה יעד לא-כתיב (תיקייה שלא קיימת, כמו כונן USB שהוצא) - במקום UnauthorizedAccessException/
        // DirectoryNotFoundException לא-מטופלת שהייתה מפילה את כל האפליקציה מלחיצה על "ייצוא הגדרות".
        var service = new SettingsService();
        var unreachablePath = Path.Combine(_sandbox, "does-not-exist-folder", "export.json");

        var succeeded = service.Export(AppSettings.CreateDefault(), unreachablePath);

        Assert.False(succeeded);
    }

    [Fact]
    public void Import_ReturnsNullForMissingFile()
    {
        var service = new SettingsService();
        var result = service.Import(Path.Combine(_sandbox, "does-not-exist.json"));

        Assert.Null(result);
    }

    [Fact]
    public void Import_ReturnsNullForCorruptJson()
    {
        var service = new SettingsService();
        var badFile = Path.Combine(_sandbox, "corrupt.json");
        File.WriteAllText(badFile, "{ this is not valid json ");

        var result = service.Import(badFile);

        Assert.Null(result);
    }

    [Fact]
    public void Import_RejectsFileLargerThanTheSizeCap()
    {
        // הגנה מפני קובץ "הגדרות" שנועד לגרום לצריכת זיכרון מוגזמת (בכוונה או בטעות) -
        // קובץ הגדרות תקין הוא כמה קילובייט לכל היותר.
        var service = new SettingsService();
        var oversizedFile = Path.Combine(_sandbox, "oversized.json");
        File.WriteAllText(oversizedFile, new string('x', 6 * 1024 * 1024));

        var result = service.Import(oversizedFile);

        Assert.Null(result);
    }

    [Fact]
    public void Import_RejectsSuspiciouslyLargeClientCollection()
    {
        var service = new SettingsService();
        var settings = AppSettings.CreateDefault();
        for (var i = 0; i < 2001; i++)
            settings.Clients.Add(new ClientProfile { Name = $"Client {i}" });

        var filePath = Path.Combine(_sandbox, "too-many-clients.json");
        service.Export(settings, filePath);

        var result = service.Import(filePath);

        Assert.Null(result);
    }
}
