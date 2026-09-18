using Filio.Models;
using Filio.Services;
using Xunit;

namespace Filio.Tests;

public class DocumentClassifierTests
{
    private static AppSettings NewSettingsWithDefaults() => AppSettings.CreateDefault();

    [Theory]
    [InlineData("invoice-Carmel-Consulting-2026.pdf", "Consulting")]
    [InlineData("חשבונית-חברת-כרמל-2026.pdf", "כרמל")]
    [InlineData("receipt.pdf", null)] // רק מילה גנרית - אין מה ללמוד ממנה
    [InlineData("2026-03-01.pdf", null)] // רק תאריך/מספרים - אין מה ללמוד ממנו
    public void SuggestClientKeywordFromFileName_PicksLongestNonGenericWord(string fileName, string? expected)
    {
        var suggested = DocumentClassifier.SuggestClientKeywordFromFileName(fileName);
        Assert.Equal(expected, suggested);
    }

    [Fact]
    public void Classify_DetectsInvoiceByContentKeyword()
    {
        var settings = NewSettingsWithDefaults();

        var result = DocumentClassifier.Classify(
            fileName: "document (3).pdf",
            content: "חשבונית מס מספר 12345 עבור שירותי ייעוץ",
            rules: settings.DocTypeRules,
            clients: settings.Clients,
            fileCreatedUtc: DateTime.UtcNow);

        Assert.Equal("Invoices", result.DocType);
    }

    [Fact]
    public void Classify_DetectsContractByFileNameWhenContentIsEmpty()
    {
        var settings = NewSettingsWithDefaults();

        var result = DocumentClassifier.Classify(
            fileName: "contract_client_2026.pdf",
            content: "",
            rules: settings.DocTypeRules,
            clients: settings.Clients,
            fileCreatedUtc: DateTime.UtcNow);

        Assert.Equal("Contracts", result.DocType);
    }

    [Fact]
    public void Classify_FallsBackToUnclassifiedWhenNoRuleMatches()
    {
        var settings = NewSettingsWithDefaults();

        var result = DocumentClassifier.Classify(
            fileName: "random-file-xyz.pdf",
            content: "טקסט כלשהו שלא קשור לשום סוג מסמך",
            rules: settings.DocTypeRules,
            clients: settings.Clients,
            fileCreatedUtc: DateTime.UtcNow);

        Assert.Equal("Unrecognized", result.DocType);
    }

    [Fact]
    public void Classify_ContentKeywordOutweighsFileNamePattern()
    {
        // שם הקובץ מרמז על "דוח" אבל התוכן מכיל יותר סימנים ברורים לחוזה - התוכן אמור לנצח
        var settings = NewSettingsWithDefaults();

        var result = DocumentClassifier.Classify(
            fileName: "report.pdf",
            content: "הסכם התקשרות זה נחתם בין הצדדים. הואיל וברצון הצדדים להתקשר בהסכם מחייב.",
            rules: settings.DocTypeRules,
            clients: settings.Clients,
            fileCreatedUtc: DateTime.UtcNow);

        Assert.Equal("Contracts", result.DocType);
    }

    [Fact]
    public void Classify_MatchesClientByAlias()
    {
        var settings = NewSettingsWithDefaults();
        settings.Clients.Add(new ClientProfile { Name = "אקמי בע\"מ", Keywords = "Acme, Acme Corp, אקמי" });

        var result = DocumentClassifier.Classify(
            fileName: "invoice.pdf",
            content: "חשבונית עבור Acme Corp בגין שירותי פיתוח",
            rules: settings.DocTypeRules,
            clients: settings.Clients,
            fileCreatedUtc: DateTime.UtcNow);

        Assert.Equal("אקמי בע\"מ", result.ClientName);
    }

    [Fact]
    public void Classify_FallsBackToUnknownClientWhenNoMatch()
    {
        var settings = NewSettingsWithDefaults();
        settings.Clients.Add(new ClientProfile { Name = "לקוח אחר", Keywords = "SomeOtherClient" });

        var result = DocumentClassifier.Classify(
            fileName: "invoice.pdf",
            content: "חשבונית ללא שום איזכור לקוח מוכר",
            rules: settings.DocTypeRules,
            clients: settings.Clients,
            fileCreatedUtc: DateTime.UtcNow);

        Assert.Equal("Unknown Client", result.ClientName);
    }

    [Theory]
    [InlineData("התאריך הוא 05/09/2026 בבדיקה", 2026, 9, 5)]
    [InlineData("Invoice date: 2026-09-05", 2026, 9, 5)]
    [InlineData("נחתם בתאריך 5.9.2026", 2026, 9, 5)]
    public void Classify_ExtractsDateFromContent(string content, int year, int month, int day)
    {
        var settings = NewSettingsWithDefaults();

        var result = DocumentClassifier.Classify(
            fileName: "file.pdf",
            content: content,
            rules: settings.DocTypeRules,
            clients: settings.Clients,
            fileCreatedUtc: DateTime.UtcNow);

        Assert.Equal(new DateTime(year, month, day), result.DocumentDate);
    }

    [Fact]
    public void Classify_FallsBackToFileCreationDateWhenNoDateFound()
    {
        var settings = NewSettingsWithDefaults();
        var created = new DateTime(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc);

        var result = DocumentClassifier.Classify(
            fileName: "file.pdf",
            content: "אין כאן שום תאריך",
            rules: settings.DocTypeRules,
            clients: settings.Clients,
            fileCreatedUtc: created);

        Assert.Equal(created.ToLocalTime().Date, result.DocumentDate.Date);
    }

    [Fact]
    public void Classify_IgnoresInvalidDateLikeMonth13()
    {
        var settings = NewSettingsWithDefaults();
        var created = new DateTime(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc);

        var result = DocumentClassifier.Classify(
            fileName: "file.pdf",
            content: "מספר תיק 31/13/2026 אינו תאריך תקין",
            rules: settings.DocTypeRules,
            clients: settings.Clients,
            fileCreatedUtc: created);

        // "31/13/2026" אינו תאריך חוקי (חודש 13) ולכן אמור ליפול חזרה לתאריך יצירת הקובץ
        Assert.Equal(created.ToLocalTime().Date, result.DocumentDate.Date);
    }

    [Theory]
    // שמות לקוחות הופכים לשם תיקייה, ולכן מנוקים מתווים אסורים בשם קובץ (כמו הגרשיים ב"בע\"מ") -
    // "בע\"מ" הופך ל-"בעמ" בכוונה.
    [InlineData("לקוח: חברת דוגמה בע\"מ", "חברת דוגמה בעמ")]
    [InlineData("לכבוד\nחברת טסט בע\"מ\nשלום רב", "חברת טסט בעמ")]
    public void Classify_GuessesClientNameFromHintPrefixWhenNoKnownClientMatches(string content, string expectedClient)
    {
        var settings = NewSettingsWithDefaults();

        var result = DocumentClassifier.Classify(
            fileName: "invoice.pdf",
            content: content,
            rules: settings.DocTypeRules,
            clients: settings.Clients,
            fileCreatedUtc: DateTime.UtcNow);

        Assert.Equal(expectedClient, result.ClientName);
    }

    [Theory]
    [InlineData("לקוח: א/ב\\ג*ד?", "לקוח א ב ג ד")]
    public void SanitizeForFolderName_StripsInvalidPathCharacters(string input, string _)
    {
        var sanitized = DocumentClassifier.SanitizeForFolderName(input);

        foreach (var invalidChar in System.IO.Path.GetInvalidFileNameChars())
            Assert.DoesNotContain(invalidChar, sanitized);
    }

    [Fact]
    public void SanitizeForFolderName_ReturnsFallbackForEmptyInput()
    {
        Assert.Equal("Unnamed", DocumentClassifier.SanitizeForFolderName("   "));
    }

    [Fact]
    public void Classify_PopulatesMatchExplanationWhenContentMatchesARule()
    {
        // ה"AI" מסביר את עצמו - זה מה שהופך אותו משקוף ל"קופסה שחורה" למשהו שהמשתמש
        // יכול להבין ולתקן (מוצג כ-tooltip ביומן הפעילות).
        var settings = NewSettingsWithDefaults();

        var result = DocumentClassifier.Classify(
            fileName: "document.pdf",
            content: "חשבונית מס מספר 12345",
            rules: settings.DocTypeRules,
            clients: settings.Clients,
            fileCreatedUtc: DateTime.UtcNow);

        Assert.False(string.IsNullOrWhiteSpace(result.MatchExplanation));
    }

    [Fact]
    public void Classify_PopulatesMatchExplanationEvenWhenNothingMatchesAtAll()
    {
        var settings = NewSettingsWithDefaults();

        var result = DocumentClassifier.Classify(
            fileName: "random-file-xyz.pdf",
            content: "טקסט כלשהו שלא קשור לשום סוג מסמך",
            rules: settings.DocTypeRules,
            clients: settings.Clients,
            fileCreatedUtc: DateTime.UtcNow);

        Assert.False(string.IsNullOrWhiteSpace(result.MatchExplanation));
    }
}
