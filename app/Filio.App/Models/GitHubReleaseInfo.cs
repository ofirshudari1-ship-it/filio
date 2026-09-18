using System.Text.Json.Serialization;

namespace Filio.Models;

/// <summary>
/// תת-קבוצה של השדות הרלוונטיים מתוך התגובה של GitHub REST API ל-
/// GET /repos/{owner}/{repo}/releases/latest (ללא אימות - נקודת קצה ציבורית).
/// </summary>
public class GitHubReleaseInfo
{
    /// <summary>שם התג שפורסם, לרוב עם קידומת "v" (למשל "v2.9.1") - מוסר לפני פענוח כ-Version.</summary>
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    /// <summary>עמוד ה-Release הציבורי ב-GitHub שאליו מפנים את המשתמש להורדה/קריאה, לא קובץ בינארי.</summary>
    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }
}
