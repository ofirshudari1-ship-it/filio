using System.IO;
using Filio.Models;
using Filio.Services;
using Xunit;

namespace Filio.Tests;

public class SmartScanServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "FilioScanTests_" + Guid.NewGuid().ToString("N"));

    public SmartScanServiceTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private static List<DocTypeRule> DefaultRules() => AppSettings.CreateDefault().DocTypeRules.ToList();

    [Fact]
    public void Scan_ReturnsEmptyResultForMissingFolder()
    {
        var result = SmartScanService.Scan(Path.Combine(_folder, "does-not-exist"), DefaultRules());

        Assert.Equal(0, result.FilesScanned);
        Assert.Empty(result.SuggestedClients);
    }

    [Fact]
    public void Scan_ReturnsEmptyResultWhenNoPdfsPresent()
    {
        File.WriteAllText(Path.Combine(_folder, "notes.txt"), "not a pdf");

        var result = SmartScanService.Scan(_folder, DefaultRules());

        Assert.Equal(0, result.FilesScanned);
    }

    [Fact]
    public void Scan_CountsFilesAndClassifiesByFileNameWhenContentIsUnreadable()
    {
        // קבצים אלה אינם PDF תקין (רק טקסט רגיל עם סיומת .pdf) - PdfTextService אמור להחזיר
        // מחרוזת ריקה בשקט, כך שהסיווג נופל חזרה לזיהוי לפי שם הקובץ בלבד.
        File.WriteAllText(Path.Combine(_folder, "invoice_1.pdf"), "not a real pdf");
        File.WriteAllText(Path.Combine(_folder, "contract_2.pdf"), "not a real pdf");
        File.WriteAllText(Path.Combine(_folder, "random_3.pdf"), "not a real pdf");

        var result = SmartScanService.Scan(_folder, DefaultRules());

        Assert.Equal(3, result.FilesScanned);
        Assert.Equal(2, result.RecognizedCount); // invoice_1 + contract_2 matched by filename pattern
        Assert.Equal(1, result.UnrecognizedCount); // random_3 matched nothing
        Assert.True(result.DocTypeCounts.ContainsKey("Invoices"));
        Assert.True(result.DocTypeCounts.ContainsKey("Contracts"));
    }

    [Fact]
    public void Scan_CountsUnrecognizedFilesCorrectlyWhenNoFallbackRuleExists()
    {
        // בלי חוק "ברירת מחדל" מוגדר בכלל (המשתמש מחק אותו, או ייבא רשימת חוקים חלקית) -
        // DocumentClassifier מחזיר את המחרוזת המילולית "Unrecognized", וזה חייב עדיין
        // להיספר כ"לא מזוהה" ולא כ"מזוהה" בטעות.
        var rulesWithoutFallback = DefaultRules().Where(r => !r.IsFallback).ToList();
        File.WriteAllText(Path.Combine(_folder, "totally_unrelated_file.pdf"), "not a real pdf");

        var result = SmartScanService.Scan(_folder, rulesWithoutFallback);

        Assert.Equal(1, result.FilesScanned);
        Assert.Equal(0, result.RecognizedCount);
        Assert.Equal(1, result.UnrecognizedCount);
    }

    [Fact]
    public void Scan_DoesNotThrowOnUnrelatedNonPdfFiles()
    {
        File.WriteAllText(Path.Combine(_folder, "readme.txt"), "hello");
        File.WriteAllText(Path.Combine(_folder, "invoice_1.pdf"), "not a real pdf");

        var result = SmartScanService.Scan(_folder, DefaultRules());

        Assert.Equal(1, result.FilesScanned); // only the .pdf counts, .txt is ignored
    }
}
