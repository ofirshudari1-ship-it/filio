using System.IO;
using System.Text.Json;
using Filio.Models;

namespace Filio.Services;

public class SettingsService
{
    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Filio");

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
            if (loaded == null)
                return AppSettings.CreateDefault();

            // מיגרציה חד-פעמית ל-2.9.8: עד 2.9.7 העדכון האוטומטי היה כבוי כברירת מחדל, אז
            // "AutoInstallUpdates": false בקובץ הקיים הוא ברירת המחדל הישנה ולא בחירה מפורשת.
            // הקובץ הישן לא מכיל את מפתח AutoInstallDefaultMigrated בכלל.
            if (MigrateLegacyAutoInstallDefault(loaded, json))
            {
                try { Save(loaded); } catch { /* ייכתב בשמירה הבאה */ }
            }
            return loaded;
        }
        catch
        {
            // קובץ הגדרות פגום/לא קריא - חוזרים לברירת מחדל במקום לקרוס
            return AppSettings.CreateDefault();
        }
    }

    /// <summary>מיגרציית 2.9.8 (טהורה, נבדקת ביחידה): קובץ הגדרות שנכתב ע"י גרסה ישנה לא מכיל את
    /// המפתח AutoInstallDefaultMigrated - אז AutoInstallUpdates=false שבו הוא ברירת המחדל הישנה ולא
    /// בחירה מפורשת, והופכים אותו ל-true. קובץ שכבר עבר מיגרציה (או נכתב ע"י 2.9.8+) לא נוגעים בו.
    /// מחזיר true אם בוצע שינוי.</summary>
    public static bool MigrateLegacyAutoInstallDefault(AppSettings loaded, string json)
    {
        if (json.Contains("\"AutoInstallDefaultMigrated\"", StringComparison.Ordinal))
            return false;
        loaded.AutoInstallUpdates = true;
        loaded.AutoInstallDefaultMigrated = true;
        return true;
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
