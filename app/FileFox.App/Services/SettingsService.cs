using System.IO;
using System.Text.Json;
using FileFox.Models;

namespace FileFox.Services;

public class SettingsService
{
    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileFox");

    private static readonly string SettingsPath = Path.Combine(AppDataDir, "settings.json");

    public static string LogPath => Path.Combine(AppDataDir, "activity-log.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppSettings Load()
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);

            if (!File.Exists(SettingsPath))
            {
                var defaults = AppSettings.CreateDefault();
                Save(defaults);
                return defaults;
            }

            var json = File.ReadAllText(SettingsPath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return loaded ?? AppSettings.CreateDefault();
        }
        catch
        {
            // קובץ הגדרות פגום/לא קריא - חוזרים לברירת מחדל במקום לקרוס
            return AppSettings.CreateDefault();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(AppDataDir);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }

    // קובץ ייבוא, בניגוד לקובץ ההגדרות הפנימי, יכול להגיע ממקור חיצוני (מייל, הורדה, USB) -
    // מגבלות הגודל/כמות למטה הן לא בגלל שציפינו לקובץ תקין גדול כך, אלא כדי שקובץ שנועד
    // לגרום לצריכת זיכרון מוגזמת (בכוונה או בטעות) יידחה בשקט במקום להאט/לתקוע את התוכנה.
    private const long MaxImportFileSizeBytes = 5 * 1024 * 1024;
    private const int MaxImportedCollectionItems = 2000;

    /// <summary>שומר עותק של ההגדרות לקובץ שהמשתמש בוחר - לגיבוי או להעברה למחשב אחר.
    /// מחזיר false (במקום לזרוק) אם הכתיבה נכשלת - למשל יעד לקריאה-בלבד או כונן מלא - כדי
    /// שלחיצה על "ייצוא הגדרות" תציג הודעת שגיאה ברורה במקום להפיל את כל האפליקציה.</summary>
    public bool Export(AppSettings settings, string filePath)
    {
        try
        {
            File.WriteAllText(filePath, JsonSerializer.Serialize(settings, JsonOptions));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>טוען הגדרות מקובץ גיבוי. מחזיר null אם הקובץ פגום/לא תקין/חורג ממגבלות סבירות,
    /// במקום לזרוק חריגה או לקבל נתונים חשודים כמו שהם.</summary>
    public AppSettings? Import(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists || fileInfo.Length > MaxImportFileSizeBytes)
                return null;

            var json = File.ReadAllText(filePath);
            var imported = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (imported == null)
                return null;

            if (imported.WatchFolders.Count > MaxImportedCollectionItems ||
                imported.Clients.Count > MaxImportedCollectionItems ||
                imported.DocTypeRules.Count > MaxImportedCollectionItems)
                return null;

            return imported;
        }
        catch
        {
            return null;
        }
    }
}
