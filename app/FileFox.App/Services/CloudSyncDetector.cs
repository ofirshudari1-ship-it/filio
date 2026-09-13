using System.IO;

namespace FileFox.Services;

public enum CloudSyncProvider { None, OneDrive, Dropbox, GoogleDrive }

/// <summary>
/// זיהוי תיקיות שמסונכרנות ע"י שירותי ענן (OneDrive/Dropbox/Google Drive) וזיהוי קבצי
/// "placeholder" ב-OneDrive Files On-Demand שעדיין לא ירדו בפועל למחשב.
///
/// זהו פער אמיתי ותיעודי שכלים מתחרים (Hazel, DropIt, File Juggler) לא מטפלים בו: העברת
/// קובץ בזמן שהוא עדיין מסתנכרן/מתעכל ע"י לקוח הענן עלולה ליצור "עותקי קונפליקט" כפולים
/// או לאלץ הורדה מיותרת. FileFox ממתין לקובץ להתייצב במקום פשוט לנסות ולהזיז אותו.
/// </summary>
public static class CloudSyncDetector
{
    public static CloudSyncProvider DetectProvider(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return CloudSyncProvider.None;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return CloudSyncProvider.None;
        }

        // בודקים כל מקטע נתיב בנפרד (לא Contains גולמי על הנתיב כולו) כדי שתיקייה מקומית
        // רגילה כמו "OneDriveBackup" לא תיחשב בטעות כתיקיית OneDrive אמיתית - היא לא שווה
        // ל-"onedrive" ולא מתחילה ב-"onedrive " (ריווח, כמו בחשבונות עסקיים "OneDrive - Acme").
        foreach (var rawSegment in fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            var segment = rawSegment.ToLowerInvariant();

            if (segment == "onedrive" || segment.StartsWith("onedrive -") || segment.StartsWith("onedrive_"))
                return CloudSyncProvider.OneDrive;

            if (segment == "dropbox")
                return CloudSyncProvider.Dropbox;

            if (segment is "google drive" or "googledrive" or "my drive")
                return CloudSyncProvider.GoogleDrive;
        }

        return CloudSyncProvider.None;
    }

    public static bool IsInsideCloudSyncFolder(string path) => DetectProvider(path) != CloudSyncProvider.None;

    /// <summary>
    /// true אם הקובץ הוא placeholder של OneDrive Files On-Demand שעדיין לא ירד בפועל -
    /// לנגוע בו (להעביר/לפתוח לקריאה) עלול לכפות הורדה מלאה במקום רק לחכות שהיא תסתיים ממילא.
    /// </summary>
    // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS / FILE_ATTRIBUTE_RECALL_ON_OPEN - הדגלים שווינדוס
    // שם על קובץ ש"נמצא בענן" (OneDrive Files On-Demand) ולא הורד עדיין בפועל. לא קיימים
    // כשם ב-System.IO.FileAttributes של .NET, לכן משתמשים בערך הגולמי של ה-flag.
    private const int FileAttributeRecallOnDataAccess = 0x00400000;
    private const int FileAttributeRecallOnOpen = 0x00040000;

    public static bool IsCloudPlaceholder(string filePath)
    {
        try
        {
            var attributes = File.GetAttributes(filePath);
            var rawAttributes = (int)attributes;

            return attributes.HasFlag(FileAttributes.Offline)
                   || (rawAttributes & FileAttributeRecallOnDataAccess) != 0
                   || (rawAttributes & FileAttributeRecallOnOpen) != 0;
        }
        catch
        {
            return false;
        }
    }
}
