using System.IO;
using System.Text.Json;
using FileFox.Models;

namespace FileFox.Services;

/// <summary>
/// אחראי על העברת/העתקת הקובץ למבנה התיקיות הסופי: שורש > לקוח > שנה > סוג מסמך > קובץ,
/// ועל שמירת יומן פעולות שמאפשר "בטל פעולה אחרונה".
/// </summary>
public class FileOrganizerService
{
    private const string DuplicatesFolderName = "_Possible Duplicates";
    private const string InstallersFolderName = "_Installers";

    // מגן על קריאה-שינוי-כתיבה של activity-log.json: כשכמה קבצים מטופלים כמעט
    // בו-זמנית (למשל שתי תיקיות מעקב מקבלות קובץ באותה שנייה), Organize רץ על thread-ים
    // נפרדים - בלי הנעילה הזו, שני threads יכולים לקרוא את אותו יומן ולכתוב חזרה בנפרד,
    // כשהאחרון שמסיים דורס את הרשומה של הראשון.
    private static readonly object LogLock = new();

    private readonly DuplicateDetectionService _duplicateService;

    public FileOrganizerService() : this(new DuplicateDetectionService())
    {
    }

    /// <summary>מאפשר להזריק DuplicateDetectionService עם נתיב אינדקס נפרד - בעיקר לבדיקות,
    /// כדי לא לזהם את אינדקס הכפילויות האמיתי של המשתמש ב-%AppData% בזמן ריצת בדיקות.</summary>
    public FileOrganizerService(DuplicateDetectionService duplicateService)
    {
        _duplicateService = duplicateService;
    }

    public FileLogEntry Organize(string sourcePath, ClassificationResult classification, AppSettings settings)
    {
        var entry = new FileLogEntry
        {
            OriginalPath = sourcePath,
            DetectedType = classification.DocType,
            DetectedClient = classification.ClientName,
            DetectedDate = classification.DocumentDate.ToString("yyyy-MM-dd"),
            MatchExplanation = classification.MatchExplanation
        };

        try
        {
            var clientFolder = DocumentClassifier.SanitizeForFolderName(classification.ClientName);
            var yearFolder = classification.DocumentDate.Year.ToString();
            var typeFolder = DocumentClassifier.SanitizeForFolderName(classification.DocType);

            // בדיקת תוכן-זהה (לא רק שם קובץ) לפני שקובעים לאן הקובץ הולך - מחקר על כלים
            // מתחרים הראה שמשתמשים רוצים לדעת על כפילויות, לא שיידרסו/יעלמו בשקט.
            var duplicateOf = settings.DetectDuplicates ? _duplicateService.FindExistingDuplicate(sourcePath) : null;

            // אותו סדר תיקיות (לקוח/שנה/סוג וכו') כמו קבצים רגילים, כדי שכפילויות לא ייצרו
            // מבנה שונה ומפתיע מתחת ל-"_Possible Duplicates" מזה שהמשתמש בחר לשאר הקבצים.
            var subPath = settings.FolderOrder switch
            {
                FolderStructureOrder.YearClientType => Path.Combine(yearFolder, clientFolder, typeFolder),
                FolderStructureOrder.TypeClientYear => Path.Combine(typeFolder, clientFolder, yearFolder),
                _ => Path.Combine(clientFolder, yearFolder, typeFolder)
            };

            string targetDir;
            if (duplicateOf != null)
            {
                targetDir = Path.Combine(settings.RootOutputFolder, DuplicatesFolderName, subPath);
                entry.IsDuplicate = true;
                entry.DuplicateOfPath = duplicateOf;
            }
            else
            {
                targetDir = Path.Combine(settings.RootOutputFolder, subPath);
            }

            Directory.CreateDirectory(targetDir);

            var targetPath = GetCollisionSafePath(Path.Combine(targetDir, Path.GetFileName(sourcePath)));

            if (settings.MoveInsteadOfCopy)
                File.Move(sourcePath, targetPath);
            else
                File.Copy(sourcePath, targetPath);

            entry.NewPath = targetPath;
            entry.Success = true;

            if (duplicateOf == null && settings.DetectDuplicates)
                _duplicateService.RememberFiledFile(targetPath);
        }
        catch (Exception ex)
        {
            entry.Success = false;
            entry.ErrorMessage = ex.Message;
        }

        AppendToLog(entry);
        return entry;
    }

    /// <summary>
    /// מיון קובצי התקנה (exe/msi): אין להם "לקוח" או "סוג מסמך" משמעותיים, אז הם מקבלים
    /// מבנה תיקיות פשוט משלהם - "_Installers\שנה" - במקום לעבור דרך מנוע הסיווג של מסמכים.
    /// עדיין נהנים מאותה הגנה מפני כפילויות ומאותו יומן פעולות עם אפשרות ביטול.
    /// </summary>
    public FileLogEntry OrganizeInstaller(string sourcePath, AppSettings settings)
    {
        var entry = new FileLogEntry
        {
            OriginalPath = sourcePath,
            DetectedType = "Installer",
            DetectedDate = DateTime.Now.ToString("yyyy-MM-dd"),
            MatchExplanation = LocalizationService.Get("ExplainInstaller")
        };

        try
        {
            var yearFolder = DateTime.Now.Year.ToString();
            var duplicateOf = settings.DetectDuplicates ? _duplicateService.FindExistingDuplicate(sourcePath) : null;

            string targetDir;
            if (duplicateOf != null)
            {
                targetDir = Path.Combine(settings.RootOutputFolder, DuplicatesFolderName, InstallersFolderName, yearFolder);
                entry.IsDuplicate = true;
                entry.DuplicateOfPath = duplicateOf;
            }
            else
            {
                targetDir = Path.Combine(settings.RootOutputFolder, InstallersFolderName, yearFolder);
            }

            Directory.CreateDirectory(targetDir);

            var targetPath = GetCollisionSafePath(Path.Combine(targetDir, Path.GetFileName(sourcePath)));

            if (settings.MoveInsteadOfCopy)
                File.Move(sourcePath, targetPath);
            else
                File.Copy(sourcePath, targetPath);

            entry.NewPath = targetPath;
            entry.Success = true;

            if (duplicateOf == null && settings.DetectDuplicates)
                _duplicateService.RememberFiledFile(targetPath);
        }
        catch (Exception ex)
        {
            entry.Success = false;
            entry.ErrorMessage = ex.Message;
        }

        AppendToLog(entry);
        return entry;
    }

    /// <summary>רושם ביומן רשומה שנוצרה מחוץ ל-FileOrganizerService - למשל קובץ שנחסם על
    /// ידי Windows Defender ולכן מעולם לא הגיע לשלב התיוק בכלל, אבל עדיין צריך להופיע
    /// ביומן הפעילות כדי שהמשתמש יידע שהוא קיים ושהוא לא נגע בו.</summary>
    public static void LogExternalEntry(FileLogEntry entry) => AppendToLog(entry);

    /// <summary>מחזיר את הקובץ למקומו המקורי, אם עוד קיים ביעד.</summary>
    public bool Undo(FileLogEntry entry)
    {
        if (entry.NewPath is null || !File.Exists(entry.NewPath))
            return false;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(entry.OriginalPath)!);
            var restorePath = GetCollisionSafePath(entry.OriginalPath);
            File.Move(entry.NewPath, restorePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetCollisionSafePath(string desiredPath)
    {
        if (!File.Exists(desiredPath))
            return desiredPath;

        var dir = Path.GetDirectoryName(desiredPath)!;
        var name = Path.GetFileNameWithoutExtension(desiredPath);
        var ext = Path.GetExtension(desiredPath);

        for (var i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(dir, $"{name}-{Guid.NewGuid():N}{ext}");
    }

    private static void AppendToLog(FileLogEntry entry)
    {
        try
        {
            lock (LogLock)
            {
                var existing = ReadLog();
                existing.Add(entry);

                // שומרים רק את 500 הרשומות האחרונות כדי שהקובץ לא יתנפח
                if (existing.Count > 500)
                    existing = existing.Skip(existing.Count - 500).ToList();

                File.WriteAllText(SettingsService.LogPath, JsonSerializer.Serialize(existing, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch
        {
            // כשל בכתיבת יומן לא אמור להפיל את תהליך התיוק עצמו
        }
    }

    public static List<FileLogEntry> ReadLog()
    {
        try
        {
            if (!File.Exists(SettingsService.LogPath))
                return new List<FileLogEntry>();

            var json = File.ReadAllText(SettingsService.LogPath);
            return JsonSerializer.Deserialize<List<FileLogEntry>>(json) ?? new List<FileLogEntry>();
        }
        catch
        {
            return new List<FileLogEntry>();
        }
    }
}
