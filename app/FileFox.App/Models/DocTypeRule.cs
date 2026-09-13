using System.Text.Json.Serialization;

namespace FileFox.Models;

/// <summary>
/// חוק סיווג סוג מסמך. Keywords נבדקים מול תוכן הקובץ (PDF), FileNamePatterns נבדקים מול שם הקובץ.
/// שני השדות מקבלים רשימה מופרדת בפסיקים.
/// </summary>
public class DocTypeRule
{
    public string TypeName { get; set; } = string.Empty;

    public string Keywords { get; set; } = string.Empty;

    public string FileNamePatterns { get; set; } = string.Empty;

    /// <summary>סוג "קליטה" שאליו מגיע כל מסמך שלא תואם אף חוק אחר.</summary>
    public bool IsFallback { get; set; } = false;

    [JsonIgnore]
    public List<string> KeywordList => SplitCsv(Keywords);

    [JsonIgnore]
    public List<string> FileNamePatternList => SplitCsv(FileNamePatterns);

    private static List<string> SplitCsv(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
             .Where(s => s.Length > 0)
             .ToList();
}
