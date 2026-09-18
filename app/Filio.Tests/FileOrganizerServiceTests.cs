using System.Collections.ObjectModel;
using System.IO;
using Filio.Models;
using Filio.Services;
using Xunit;

namespace Filio.Tests;

public class FileOrganizerServiceTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "FilioTests_" + Guid.NewGuid().ToString("N"));
    private readonly string _downloadsDir;
    private readonly string _outputDir;

    public FileOrganizerServiceTests()
    {
        _downloadsDir = Path.Combine(_sandbox, "downloads");
        _outputDir = Path.Combine(_sandbox, "output");
        Directory.CreateDirectory(_downloadsDir);
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_sandbox))
            Directory.Delete(_sandbox, recursive: true);
    }

    private AppSettings NewSettings(bool moveInsteadOfCopy = true) => new()
    {
        WatchFolders = new ObservableCollection<string> { _downloadsDir },
        RootOutputFolder = _outputDir,
        MoveInsteadOfCopy = moveInsteadOfCopy
    };

    private string CreateSourceFile(string name, string content = "dummy")
    {
        var path = Path.Combine(_downloadsDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>אינדקס כפילויות מבודד לכל בדיקה - כדי לא לגעת באינדקס האמיתי ב-%AppData%.</summary>
    private FileOrganizerService NewOrganizer() =>
        new(new DuplicateDetectionService(Path.Combine(_sandbox, "dup-index.json")));

    [Fact]
    public void Organize_MovesFileIntoClientYearTypeStructure()
    {
        var source = CreateSourceFile("invoice1.pdf");
        var settings = NewSettings();
        var classification = new ClassificationResult
        {
            DocType = "חשבוניות",
            ClientName = "לקוח בדיקה",
            DocumentDate = new DateTime(2026, 3, 10)
        };

        var entry = NewOrganizer().Organize(source, classification, settings);

        Assert.True(entry.Success);
        var expectedPath = Path.Combine(_outputDir, "לקוח בדיקה", "2026", "חשבוניות", "invoice1.pdf");
        Assert.Equal(expectedPath, entry.NewPath);
        Assert.True(File.Exists(expectedPath));
        Assert.False(File.Exists(source), "המקור אמור להיעלם כשההגדרה היא 'העברה'");
    }

    [Fact]
    public void Organize_CopiesInsteadOfMovingWhenConfigured()
    {
        var source = CreateSourceFile("invoice2.pdf");
        var settings = NewSettings(moveInsteadOfCopy: false);
        var classification = new ClassificationResult { DocType = "דוחות", ClientName = "לקוח אחר", DocumentDate = new DateTime(2026, 1, 1) };

        var entry = NewOrganizer().Organize(source, classification, settings);

        Assert.True(entry.Success);
        Assert.True(File.Exists(source), "המקור אמור להישאר קיים במצב 'העתקה'");
        Assert.True(File.Exists(entry.NewPath!));
    }

    [Fact]
    public void Organize_AvoidsOverwritingExistingFileWithSameName()
    {
        var settings = NewSettings();
        var classification = new ClassificationResult { DocType = "חשבוניות", ClientName = "לקוח", DocumentDate = new DateTime(2026, 5, 1) };
        var organizer = NewOrganizer();

        var first = CreateSourceFile("dup.pdf", "first");
        var firstEntry = organizer.Organize(first, classification, settings);

        var second = CreateSourceFile("dup.pdf", "second");
        var secondEntry = organizer.Organize(second, classification, settings);

        Assert.True(firstEntry.Success);
        Assert.True(secondEntry.Success);
        Assert.NotEqual(firstEntry.NewPath, secondEntry.NewPath);
        Assert.True(File.Exists(firstEntry.NewPath!));
        Assert.True(File.Exists(secondEntry.NewPath!));
        Assert.Equal("first", File.ReadAllText(firstEntry.NewPath!));
        Assert.Equal("second", File.ReadAllText(secondEntry.NewPath!));
    }

    [Fact]
    public void Organize_ReportsFailureInsteadOfThrowingWhenSourceMissing()
    {
        var settings = NewSettings();
        var classification = new ClassificationResult { DocType = "דוחות", ClientName = "לקוח", DocumentDate = DateTime.Today };

        var missingPath = Path.Combine(_downloadsDir, "does-not-exist.pdf");
        var entry = NewOrganizer().Organize(missingPath, classification, settings);

        Assert.False(entry.Success);
        Assert.NotNull(entry.ErrorMessage);
    }

    [Fact]
    public void Undo_RestoresFileToOriginalLocation()
    {
        var source = CreateSourceFile("restore-me.pdf", "content-to-restore");
        var settings = NewSettings();
        var classification = new ClassificationResult { DocType = "חוזים", ClientName = "לקוח", DocumentDate = new DateTime(2026, 2, 2) };
        var organizer = NewOrganizer();

        var entry = organizer.Organize(source, classification, settings);
        Assert.True(entry.Success);
        Assert.False(File.Exists(source));

        var undone = organizer.Undo(entry);

        Assert.True(undone);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(entry.NewPath!));
        Assert.Equal("content-to-restore", File.ReadAllText(source));
    }

    [Theory]
    [InlineData(FolderStructureOrder.ClientYearType, "לקוח", "2026", "חשבוניות")]
    [InlineData(FolderStructureOrder.YearClientType, "2026", "לקוח", "חשבוניות")]
    [InlineData(FolderStructureOrder.TypeClientYear, "חשבוניות", "לקוח", "2026")]
    public void Organize_RespectsConfiguredFolderOrder(FolderStructureOrder order, string seg1, string seg2, string seg3)
    {
        var settings = NewSettings();
        settings.FolderOrder = order;
        var classification = new ClassificationResult { DocType = "חשבוניות", ClientName = "לקוח", DocumentDate = new DateTime(2026, 6, 1) };

        var entry = NewOrganizer().Organize(CreateSourceFile("order_test.pdf"), classification, settings);

        var expectedPath = Path.Combine(_outputDir, seg1, seg2, seg3, "order_test.pdf");
        Assert.True(entry.Success);
        Assert.Equal(expectedPath, entry.NewPath);
    }

    [Fact]
    public void Organize_RoutesContentDuplicateToPossibleDuplicatesFolder()
    {
        var settings = NewSettings();
        var classification = new ClassificationResult { DocType = "Invoices", ClientName = "Acme", DocumentDate = new DateTime(2026, 4, 1) };
        var organizer = NewOrganizer();

        var original = CreateSourceFile("invoice_original.pdf", "identical content");
        var firstEntry = organizer.Organize(original, classification, settings);
        Assert.True(firstEntry.Success);
        Assert.False(firstEntry.IsDuplicate);

        // אותו תוכן בדיוק, שם קובץ שונה (למשל אותה חשבונית שהורדה פעמיים)
        var duplicate = CreateSourceFile("invoice_downloaded_again.pdf", "identical content");
        var secondEntry = organizer.Organize(duplicate, classification, settings);

        Assert.True(secondEntry.Success);
        Assert.True(secondEntry.IsDuplicate);
        Assert.Equal(firstEntry.NewPath, secondEntry.DuplicateOfPath);
        Assert.Contains("_Possible Duplicates", secondEntry.NewPath);
        Assert.True(File.Exists(secondEntry.NewPath!));
    }

    [Fact]
    public void Organize_DoesNotFlagDuplicatesWhenDetectionDisabled()
    {
        var settings = NewSettings();
        settings.DetectDuplicates = false;
        var classification = new ClassificationResult { DocType = "Invoices", ClientName = "Acme", DocumentDate = new DateTime(2026, 4, 1) };
        var organizer = NewOrganizer();

        organizer.Organize(CreateSourceFile("a.pdf", "same"), classification, settings);
        var second = organizer.Organize(CreateSourceFile("b.pdf", "same"), classification, settings);

        Assert.False(second.IsDuplicate);
    }

    [Fact]
    public void OrganizeInstaller_RoutesToDedicatedInstallersFolderByYear()
    {
        // קובצי התקנה לא עוברים דרך מנוע הסיווג של מסמכים - אין להם "לקוח" משמעותי.
        var source = CreateSourceFile("FilioSetup.exe");
        var settings = NewSettings();

        var entry = NewOrganizer().OrganizeInstaller(source, settings);

        Assert.True(entry.Success);
        var expectedDir = Path.Combine(_outputDir, "_Installers", DateTime.Now.Year.ToString());
        Assert.Equal(Path.Combine(expectedDir, "FilioSetup.exe"), entry.NewPath);
        Assert.True(File.Exists(entry.NewPath!));
        Assert.Equal("Installer", entry.DetectedType);
    }

    [Fact]
    public void OrganizeInstaller_RoutesDuplicateInstallerToPossibleDuplicatesInstallersFolder()
    {
        var settings = NewSettings();
        var organizer = NewOrganizer();

        var first = CreateSourceFile("setup_v1.exe", "identical installer bytes");
        var firstEntry = organizer.OrganizeInstaller(first, settings);
        Assert.True(firstEntry.Success);

        var second = CreateSourceFile("setup_v1_again.exe", "identical installer bytes");
        var secondEntry = organizer.OrganizeInstaller(second, settings);

        Assert.True(secondEntry.Success);
        Assert.True(secondEntry.IsDuplicate);
        Assert.Contains("_Possible Duplicates", secondEntry.NewPath);
        Assert.Contains("_Installers", secondEntry.NewPath);
    }

    [Fact]
    public void Reclassify_MovesAlreadyFiledEntryToCorrectClientAndKeepsOriginalPathInLog()
    {
        var settings = NewSettings();
        var organizer = NewOrganizer();
        var source = CreateSourceFile("misfiled.pdf");

        var wrongClassification = new ClassificationResult { DocType = "Invoices", ClientName = "Unknown Client", DocumentDate = new DateTime(2026, 3, 1) };
        var firstEntry = organizer.Organize(source, wrongClassification, settings);
        Assert.True(firstEntry.Success);
        Assert.Contains("Unknown Client", firstEntry.NewPath);

        var correctClassification = new ClassificationResult { DocType = "Invoices", ClientName = "Acme", DocumentDate = new DateTime(2026, 3, 1) };
        var fixedEntry = organizer.Reclassify(firstEntry, correctClassification, settings);

        Assert.True(fixedEntry.Success);
        Assert.Contains("Acme", fixedEntry.NewPath);
        Assert.False(File.Exists(firstEntry.NewPath), "הקובץ אמור לעבור מהמיקום הישן לחדש, לא להישאר גם שם");
        Assert.True(File.Exists(fixedEntry.NewPath));
        // יומן הפעילות עדיין מציג את הנתיב המקורי האמיתי (תיקיית ההורדות), לא את התחנה הביניים
        Assert.Equal(source, fixedEntry.OriginalPath);
    }

    [Fact]
    public void Reclassify_FilesAPendingReviewEntryThatWasNeverMoved()
    {
        var settings = NewSettings();
        var organizer = NewOrganizer();
        var source = CreateSourceFile("pending-review.pdf");

        // רשומת "ממתין לבדיקה" - הקובץ מעולם לא זז, NewPath ריק (בדיוק כמו FileWatcherService יוצר אותה)
        var reviewEntry = new FileLogEntry { OriginalPath = source, NewPath = null, NeedsReview = true, Success = false };

        var classification = new ClassificationResult { DocType = "Contracts", ClientName = "Acme", DocumentDate = new DateTime(2026, 3, 1) };
        var filedEntry = organizer.Reclassify(reviewEntry, classification, settings);

        Assert.True(filedEntry.Success);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(filedEntry.NewPath!));
        Assert.Contains("Acme", filedEntry.NewPath);
    }

    [Fact]
    public void Undo_ReturnsFalseWhenFileWasAlreadyMovedAway()
    {
        var entry = new FileLogEntry
        {
            OriginalPath = Path.Combine(_downloadsDir, "never-existed.pdf"),
            NewPath = Path.Combine(_outputDir, "also-never-existed.pdf"),
            Success = true
        };

        var undone = NewOrganizer().Undo(entry);

        Assert.False(undone);
    }
}
