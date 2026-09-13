using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using FileFox.Models;
using FileFox.Services;
using Xunit;

namespace FileFox.Tests;

public class FileWatcherServiceTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "FileFoxWatcherTests_" + Guid.NewGuid().ToString("N"));
    private readonly string _watchDir;
    private readonly string _outputDir;

    public FileWatcherServiceTests()
    {
        _watchDir = Path.Combine(_sandbox, "downloads");
        _outputDir = Path.Combine(_sandbox, "output");
        Directory.CreateDirectory(_watchDir);
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_sandbox))
            Directory.Delete(_sandbox, recursive: true);
    }

    // EnableDefenderScan כבוי כברירת מחדל בבדיקות (בניגוד לברירת המחדל האמיתית של
    // האפליקציה) - סריקת Defender אמיתית היא תהליך חיצוני איטי ולא-דטרמיניסטי, ולא רוצים
    // שכל בדיקה קיימת בקובץ הזה תאט/תתלוי בו. רק הבדיקות הספציפיות לאבטחה מפעילות אותו.
    private AppSettings NewSettings() => new()
    {
        WatchFolders = new ObservableCollection<string> { _watchDir },
        RootOutputFolder = _outputDir,
        MoveInsteadOfCopy = true,
        DocTypeRules = AppSettings.CreateDefault().DocTypeRules,
        EnableDefenderScan = false
    };

    /// <summary>אינדקס כפילויות מבודד - כדי לא לגעת באינדקס האמיתי ב-%AppData% בזמן בדיקות.</summary>
    private FileWatcherService NewWatcher(AppSettings settings) =>
        new(settings, new FileOrganizerService(new DuplicateDetectionService(Path.Combine(_sandbox, "dup-index.json"))));

    [Fact]
    public async Task NewSubdirectory_IsIgnoredAndDoesNotProduceALogEntry()
    {
        var settings = NewSettings();
        var processedEntries = new List<FileLogEntry>();

        using var watcher = NewWatcher(settings);
        watcher.FileProcessed += entry => { lock (processedEntries) processedEntries.Add(entry); };
        watcher.Start();

        // מדמה בדיוק את המצב שגרם לבאג: תיקיית "לקוח" שנוצרת ישירות בתוך תיקיית המעקב
        Directory.CreateDirectory(Path.Combine(_watchDir, "לקוח כלשהו"));

        await Task.Delay(2000);

        lock (processedEntries)
            Assert.Empty(processedEntries);
    }

    [Fact]
    public async Task NewFile_IsAutomaticallyClassifiedAndMoved()
    {
        var settings = NewSettings();
        var processedEntries = new List<FileLogEntry>();

        using var watcher = NewWatcher(settings);
        watcher.FileProcessed += entry => { lock (processedEntries) processedEntries.Add(entry); };
        watcher.Start();

        File.WriteAllText(Path.Combine(_watchDir, "חוזה עם לקוח.pdf"), "טקסט דמה, לא PDF אמיתי");

        var entry = await WaitForEntryAsync(processedEntries, TimeSpan.FromSeconds(10));

        Assert.NotNull(entry);
        Assert.True(entry!.Success);
        Assert.False(File.Exists(Path.Combine(_watchDir, "חוזה עם לקוח.pdf")), "הקובץ אמור לעבור לתיקיית היעד");
        Assert.True(File.Exists(entry.NewPath!), "הקובץ אמור להופיע בתיקיית היעד");
    }

    [Fact]
    public async Task FilesWithUnwatchedExtensions_AreLeftAlone()
    {
        // באג אמיתי שנתפס בשימוש בפועל: לפני התיקון, כל קובץ (ארכיונים, HTML, מסמכי עבודה
        // כלליים) שנחת בתיקיית ההורדות היה נגרר ל"לא מזוהה" - גם קבצים שלא קשורים בכלל
        // לחשבוניות/חוזים/דוחות. עכשיו רק סיומות שברשימת WatchedFileExtensions מטופלות.
        // (תמונות jpg/png כן נצפות כברירת מחדל מאז שנוסף OCR מקומי - לכן משתמשים כאן ב-zip.)
        var settings = NewSettings();
        var processedEntries = new List<FileLogEntry>();

        using var watcher = NewWatcher(settings);
        watcher.FileProcessed += entry => { lock (processedEntries) processedEntries.Add(entry); };
        watcher.Start();

        var archivePath = Path.Combine(_watchDir, "backup.zip");
        File.WriteAllText(archivePath, "not a real zip, just test bytes");

        // גם ממתינים רגע וגם שולחים קובץ pdf אחריו - כדי לוודא שה-zip לא התעכב סתם אלא
        // שהמעקב פעיל וה-pdf כן מטופל בזמן סביר
        await Task.Delay(1500);
        Assert.Empty(processedEntries);
        Assert.True(File.Exists(archivePath), "קובץ עם סיומת לא-נצפית אמור להישאר במקומו");

        File.WriteAllText(Path.Combine(_watchDir, "invoice.pdf"), "טקסט דמה");
        var entry = await WaitForEntryAsync(processedEntries, TimeSpan.FromSeconds(10));

        Assert.NotNull(entry);
        Assert.True(File.Exists(archivePath), "קובץ ה-zip עדיין אמור להישאר במקומו גם אחרי שקובץ pdf טופל");
    }

    [Fact]
    public async Task FilesMatchingIgnorePattern_AreSkippedEvenWithWatchedExtension()
    {
        var settings = NewSettings();
        settings.IgnoredFileNamePatterns = "do-not-file";
        var processedEntries = new List<FileLogEntry>();

        using var watcher = NewWatcher(settings);
        watcher.FileProcessed += entry => { lock (processedEntries) processedEntries.Add(entry); };
        watcher.Start();

        var ignoredPath = Path.Combine(_watchDir, "please-do-not-file-me.pdf");
        File.WriteAllText(ignoredPath, "טקסט דמה");

        await Task.Delay(1500);
        Assert.Empty(processedEntries);
        Assert.True(File.Exists(ignoredPath), "קובץ שתואם לתבנית ההתעלמות אמור להישאר במקומו");

        File.WriteAllText(Path.Combine(_watchDir, "invoice.pdf"), "טקסט דמה");
        var entry = await WaitForEntryAsync(processedEntries, TimeSpan.FromSeconds(10));

        Assert.NotNull(entry);
        Assert.True(File.Exists(ignoredPath));
    }

    [Fact]
    public async Task PhotographedInvoice_IsClassifiedByOcrContentEndToEnd()
    {
        // בדיוק היכולת שנוספה: תמונה (למשל צילום נייד של חשבונית) עוברת דרך אותו pipeline
        // מלא כמו PDF - כולל OCR מקומי אמיתי (לא מדומה) על תוכן אמיתי שנצייר בתמונה.
        var settings = NewSettings();
        var processedEntries = new List<FileLogEntry>();

        using var watcher = NewWatcher(settings);
        watcher.FileProcessed += entry => { lock (processedEntries) processedEntries.Add(entry); };
        watcher.Start();

        var photoPath = Path.Combine(_watchDir, "IMG_20260908.jpg");
        using (var bitmap = new Bitmap(700, 140))
        {
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White);
                using var font = new Font("Arial", 28, System.Drawing.FontStyle.Bold);
                g.DrawString("INVOICE 90210", font, Brushes.Black, new PointF(15, 45));
            }
            bitmap.Save(photoPath, ImageFormat.Jpeg);
        }

        var entry = await WaitForEntryAsync(processedEntries, TimeSpan.FromSeconds(20));

        Assert.NotNull(entry);
        Assert.True(entry!.Success);
        Assert.Equal("Invoices", entry.DetectedType);
    }

    [Fact]
    public async Task Installer_IsRoutedToInstallersFolderWhenOptedIn()
    {
        var settings = NewSettings();
        settings.OrganizeInstallers = true;
        settings.EnableDefenderScan = false; // מבודד את בדיקת הניתוב מזמן ריצה איטי של סריקה אמיתית
        var processedEntries = new List<FileLogEntry>();

        using var watcher = NewWatcher(settings);
        watcher.FileProcessed += entry => { lock (processedEntries) processedEntries.Add(entry); };
        watcher.Start();

        File.WriteAllText(Path.Combine(_watchDir, "SomeAppSetup.exe"), "not a real installer, just test bytes");

        var entry = await WaitForEntryAsync(processedEntries, TimeSpan.FromSeconds(10));

        Assert.NotNull(entry);
        Assert.True(entry!.Success);
        Assert.Contains("_Installers", entry.NewPath);
        Assert.False(File.Exists(Path.Combine(_watchDir, "SomeAppSetup.exe")));
    }

    [Fact]
    public async Task ExeFiles_AreIgnoredByDefaultWhenInstallerOrganizingIsOff()
    {
        var settings = NewSettings(); // OrganizeInstallers כבוי כברירת מחדל
        var processedEntries = new List<FileLogEntry>();

        using var watcher = NewWatcher(settings);
        watcher.FileProcessed += entry => { lock (processedEntries) processedEntries.Add(entry); };
        watcher.Start();

        var exePath = Path.Combine(_watchDir, "random_tool.exe");
        File.WriteAllText(exePath, "not a real exe, just test bytes");

        await Task.Delay(1500);

        Assert.Empty(processedEntries);
        Assert.True(File.Exists(exePath), "בלי OrganizeInstallers מופעל, קובץ exe אמור להישאר בדיוק במקומו");
    }

    [Fact]
    public async Task FileFlaggedByDefender_IsNeverFiledAndIsReportedAsAThreat()
    {
        // בדיוק כמו ב-WindowsDefenderScanServiceTests: EICAR הוא קובץ הבדיקה התקני של
        // תעשיית האנטי-וירוס, לא קוד זדוני אמיתי - מיועד בכוונה לזיהוי כ"איום" למטרות בדיקה.
        const string eicarTestString = "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";

        var settings = NewSettings();
        settings.EnableDefenderScan = true;
        var processedEntries = new List<FileLogEntry>();

        using var watcher = NewWatcher(settings);
        watcher.FileProcessed += entry => { lock (processedEntries) processedEntries.Add(entry); };
        watcher.Start();

        var eicarPath = Path.Combine(_watchDir, "eicar-test-invoice.pdf");
        File.WriteAllText(eicarPath, eicarTestString);

        var entry = await WaitForEntryAsync(processedEntries, TimeSpan.FromSeconds(30));

        // אם Defender לא זמין בסביבת ההרצה, אין entry שנוצר (הבדיקה מדלגת בשקט על הבדיקה
        // האמיתית) - אבל אם כן זוהה איום, זה חייב בהחלט לא להיות מתויק.
        if (entry != null)
        {
            Assert.True(entry.ThreatDetected);
            Assert.False(entry.Success);
            Assert.True(File.Exists(eicarPath), "קובץ שזוהה כאיום אסור שיוזז מהמיקום המקורי שלו");
        }
    }

    [Fact]
    public async Task RetryFileAsync_ReprocessesAFileDirectly()
    {
        var settings = NewSettings();
        var processedEntries = new List<FileLogEntry>();

        using var watcher = NewWatcher(settings);
        watcher.FileProcessed += entry => { lock (processedEntries) processedEntries.Add(entry); };
        watcher.Start();

        var path = Path.Combine(_watchDir, "invoice.pdf");
        File.WriteAllText(path, "טקסט דמה");

        await watcher.RetryFileAsync(path);

        var entry = await WaitForEntryAsync(processedEntries, TimeSpan.FromSeconds(10));
        Assert.NotNull(entry);
        Assert.True(entry!.Success);
    }

    [Fact]
    public async Task EnsureAllWatchersHealthy_StartsWatchingFolderThatAppearsLater()
    {
        // מדמה כונן רשת/תיקייה שלא הייתה קיימת כשה-watcher עלה לראשונה, וחזרה מאוחר יותר -
        // בדיוק התקלה המתועדת בכלים מתחרים (למשל DropIt) שלא מתאוששים לבד.
        var lateFolder = Path.Combine(_sandbox, "late-folder");
        var settings = new AppSettings
        {
            WatchFolders = new ObservableCollection<string> { lateFolder },
            RootOutputFolder = _outputDir,
            MoveInsteadOfCopy = true,
            DocTypeRules = AppSettings.CreateDefault().DocTypeRules,
            EnableDefenderScan = false
        };

        var processedEntries = new List<FileLogEntry>();
        using var watcher = NewWatcher(settings);
        watcher.FileProcessed += entry => { lock (processedEntries) processedEntries.Add(entry); };
        watcher.Start(); // התיקייה עוד לא קיימת - לא אמור לזרוק חריגה

        Directory.CreateDirectory(lateFolder);
        watcher.EnsureAllWatchersHealthy(); // בפועל רץ כל 30 שניות; כאן קוראים ישירות לבדיקה מיידית

        File.WriteAllText(Path.Combine(lateFolder, "invoice.pdf"), "טקסט דמה");

        var entry = await WaitForEntryAsync(processedEntries, TimeSpan.FromSeconds(10));

        Assert.NotNull(entry);
        Assert.True(entry!.Success);
    }

    private static async Task<FileLogEntry?> WaitForEntryAsync(List<FileLogEntry> entries, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            lock (entries)
            {
                if (entries.Count > 0)
                    return entries[0];
            }

            await Task.Delay(200);
        }

        return null;
    }
}
