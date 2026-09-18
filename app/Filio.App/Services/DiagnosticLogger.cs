using System.IO;

namespace Filio.Services;

public enum LogLevel { Info, Warn, Error }

/// <summary>
/// לוג טכני לאבחון תקלות (לא יומן הפעילות שהמשתמש רואה בטאב "פעילות" — זה כלי דיבוג
/// למפתח/תמיכה). נכתב ל-%APPDATA%\Filio\logs\ עם קובץ יומי ורוטציה שמוחקת קבצים ישנים
/// מ-14 יום כדי שהתיקייה לא תתנפח לאורך זמן.
/// </summary>
public static class DiagnosticLogger
{
    private static readonly string LogsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Filio", "logs");

    private static readonly object WriteLock = new();
    private const int RetainDays = 14;

    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warn(string message) => Write(LogLevel.Warn, message);
    public static void Error(string message, Exception? ex = null) =>
        Write(LogLevel.Error, ex != null ? $"{message} — {ex.GetType().Name}: {ex.Message}" : message);

    private static void Write(LogLevel level, string message)
    {
        try
        {
            lock (WriteLock)
            {
                Directory.CreateDirectory(LogsDir);
                var fileName = $"filio-{DateTime.Now:yyyy-MM-dd}.log";
                var path = Path.Combine(LogsDir, fileName);
                var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(path, line);
            }
        }
        catch
        {
            // כשל כתיבת לוג לא אמור להפיל את האפליקציה עצמה
        }
    }

    /// <summary>מוחק קובצי לוג ישנים מ-14 יום. נקרא פעם אחת בהפעלת האפליקציה.</summary>
    public static void PruneOldLogs()
    {
        try
        {
            if (!Directory.Exists(LogsDir)) return;
            var cutoff = DateTime.Now.AddDays(-RetainDays);
            foreach (var file in Directory.GetFiles(LogsDir, "filio-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                    File.Delete(file);
            }
        }
        catch
        {
            // best-effort
        }
    }
}
