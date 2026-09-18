using System.IO;
using Filio.Services;
using Xunit;

namespace Filio.Tests;

/// <summary>
/// בודק את אינטגרציית Windows Defender עם קובץ הבדיקה התקני של תעשיית האנטי-וירוס
/// (EICAR) - מחרוזת לא-מזיקה שכל מנוע אנטי-וירוס בעולם, כולל Defender, מזהה בכוונה
/// כ"איום" למטרות בדיקה בדיוק כמו הזו. לא נוגעים בקוד זדוני אמיתי בשום שלב.
/// </summary>
public class WindowsDefenderScanServiceTests : IDisposable
{
    // המחרוזת הרשמית של https://www.eicar.org/ - מיועדת בכוונה לזיהוי על ידי אנטי-וירוס.
    private const string EicarTestString =
        "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";

    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "FilioDefenderTests_" + Guid.NewGuid().ToString("N"));

    public WindowsDefenderScanServiceTests() => Directory.CreateDirectory(_sandbox);

    public void Dispose()
    {
        if (Directory.Exists(_sandbox))
            Directory.Delete(_sandbox, recursive: true);
    }

    [Fact]
    public void ScanFile_NeverReportsTheEicarTestFileAsClean()
    {
        // ההגנה הקריטית שהבדיקה הזו מוודאת: גם אם משהו לא צפוי קורה בסביבת ההרצה (Defender
        // כבוי, MpCmdRun.exe לא נמצא), אסור בשום מצב שנחזיר "נקי" על קובץ שידוע כ"נגוע"
        // (גם אם מלאכותי) - Unavailable זה תוצאה בטוחה, Clean על EICAR זו לא.
        var eicarPath = Path.Combine(_sandbox, "eicar-test.com");
        File.WriteAllText(eicarPath, EicarTestString);

        var result = WindowsDefenderScanService.ScanFile(eicarPath);

        Assert.NotEqual(DefenderScanResult.Clean, result);
    }

    [Fact]
    public void ScanFile_ReturnsUnavailableForMissingFile()
    {
        var result = WindowsDefenderScanService.ScanFile(Path.Combine(_sandbox, "does-not-exist.exe"));

        Assert.Equal(DefenderScanResult.Unavailable, result);
    }

    [Fact]
    public void ScanFile_ReportsOrdinaryTextFileAsCleanOrUnavailable()
    {
        // קובץ טקסט תמים לא אמור להיחסם - אם Defender כן זמין, התוצאה חייבת להיות Clean
        // (לעולם לא ThreatFound על קובץ לא-מזיק), אחרת Unavailable אם לא ניתן לבדוק בכלל.
        var harmlessPath = Path.Combine(_sandbox, "notes.txt");
        File.WriteAllText(harmlessPath, "just an ordinary shopping list");

        var result = WindowsDefenderScanService.ScanFile(harmlessPath);

        Assert.NotEqual(DefenderScanResult.ThreatFound, result);
    }
}
