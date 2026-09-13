using System.Text.Json.Serialization;

namespace FileFox.Models;

/// <summary>
/// המבנה של קובץ ה-JSON שמפורסם בכתובת העדכונים (UpdateFeedUrl). לדוגמה:
/// { "version": "2.1.0", "installerUrl": "https://.../FileFoxSetup.exe", "notes": "..." }
/// </summary>
public class UpdateManifest
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("installerUrl")]
    public string InstallerUrl { get; set; } = string.Empty;

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;

    /// <summary>גיבוב SHA-256 (hex) של קובץ ההתקנה - אופציונלי, אבל אם קיים נבדק אחרי ההורדה
    /// לפני שההתקנה השקטה מופעלת. בלעדיו לא ניתן לאתר שרת/CDN שהוחלף בזדון בין הפרסום להורדה.</summary>
    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }
}
