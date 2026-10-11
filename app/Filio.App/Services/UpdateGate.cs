using System.IO;
using System.Runtime.InteropServices;

namespace Filio.Services;

/// <summary>
/// "הרגע הבטוח" להתקנת עדכון אוטומטי (2.9.8). ההתקנה סוגרת את Filio ומפעילה אותו מחדש, ולכן לא
/// מתקינים באמצע עבודה של המשתמש: רק אחרי שלא נגע במקלדת/עכבר <see cref="MinIdleSeconds"/>,
/// ולעולם לא בזמן כיבוי/התנתקות של Windows (התקנה שנקטעת באמצע כתיבה לתיקיית ההתקנה = התקנה שבורה,
/// STANDARDS 11.1).
/// </summary>
public static class UpdateGate
{
    public const double MinIdleSeconds = 180;
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    public static readonly TimeSpan DeclineBackoff = TimeSpan.FromHours(24);

    public static bool IsSafeToInstall(double idleSeconds, bool sessionEnding, bool installInProgress,
        DateTime utcNow, DateTime blockedUntilUtc)
    {
        if (sessionEnding || installInProgress) return false;
        if (utcNow < blockedUntilUtc) return false;
        return idleSeconds >= MinIdleSeconds;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    /// <summary>שניות מאז הקלט האחרון של המשתמש. כשל בקריאה = 0 ("לא idle") - עדיף לא להתקין
    /// מאשר להתקין בטעות.</summary>
    public static double GetIdleSeconds()
    {
        try
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref info)) return 0;
            var now = unchecked((uint)Environment.TickCount);
            var idleMs = unchecked(now - info.dwTime);
            return idleMs / 1000.0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>סימון חד-פעמי ש-Setup.cs כותב (/RESTARTMINIMIZED) לפני שהוא מפעיל את Filio מחדש
    /// אחרי עדכון אוטומטי: מבקש שההפעלה הבאה תעלה למגש בלי חלון ראשי. אותו נתיב מילולי כמו ב-Setup.cs.</summary>
    public static string RestartMinimizedFlagPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Filio", "restart-minimized.flag");

    /// <summary>קורא ומוחק את הסימון. true אם היה קיים וטרי (עד 10 דקות - סימון ישן שנשאר בדיסק
    /// לא ישפיע על הפעלה רגילה אחרת).</summary>
    public static bool TryConsumeRestartMinimizedFlag()
    {
        try
        {
            var path = RestartMinimizedFlagPath;
            if (!File.Exists(path)) return false;
            var fresh = DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < TimeSpan.FromMinutes(10);
            File.Delete(path);
            return fresh;
        }
        catch
        {
            return false;
        }
    }

    private static string BackoffPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Filio", "update-backoff.txt");

    /// <summary>עד מתי (UTC) לא מנסים התקנה אוטומטית אחרי שהמשתמש דחה UAC. נשמר בדיסק כדי
    /// ש-Filio שמופעל מחדש (Setup מפעיל אותו אחרי דחייה) לא ינסה שוב מיד.</summary>
    public static DateTime LoadBackoffUntilUtc()
    {
        try
        {
            if (File.Exists(BackoffPath) &&
                long.TryParse(File.ReadAllText(BackoffPath).Trim(), out var ticks) &&
                ticks > 0 && ticks <= DateTime.MaxValue.Ticks)
                return new DateTime(ticks, DateTimeKind.Utc);
        }
        catch { /* best-effort */ }
        return DateTime.MinValue;
    }

    public static void SaveBackoffUntilUtc(DateTime untilUtc)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BackoffPath)!);
            File.WriteAllText(BackoffPath, untilUtc.Ticks.ToString());
        }
        catch { /* best-effort */ }
    }
}
