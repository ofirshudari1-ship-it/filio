using Microsoft.Win32;

namespace FileFox.Services;

/// <summary>מפעיל/מכבה הפעלה אוטומטית עם Windows דרך מפתח ה-Run של המשתמש הנוכחי (לא דורש הרשאות מנהל).</summary>
public static class StartupService
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FileFox";

    /// <summary>
    /// כותב/מוחק את ערך ההפעלה האוטומטית. עטוף ב-try/catch בכוונה: במחשב עם מדיניות קבוצתית
    /// (Group Policy ארגוני) שנועלת את מפתח ה-Run, או תוכנת אבטחה שחוסמת כתיבה אליו,
    /// Registry.SetValue/DeleteValue יכולה לזרוק UnauthorizedAccessException/SecurityException.
    /// זה נקרא עכשיו גם מ-OnStartup בכל הפעלה (לא רק בלחיצת "שמירה") - בלי ה-try/catch, מחשב
    /// כזה היה יכול לגרום ל-FileFox לקרוס בכל הפעלה, לא רק כשל שקט בהגדרה אחת.
    /// </summary>
    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null) return;

            if (enabled)
            {
                // Environment.ProcessPath הוא הדרך היחידה האמינה לקבל את נתיב ה-exe גם בפרסום single-file
                // (Assembly.Location מחזיר תמיד מחרוזת ריקה במצב הזה)
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath)) return;

                key.SetValue(ValueName, $"\"{exePath}\" --minimized");
            }
            else
            {
                if (key.GetValue(ValueName) != null)
                    key.DeleteValue(ValueName);
            }
        }
        catch
        {
            // אין הרשאה לכתוב למפתח ה-Run (מדיניות ארגונית/תוכנת אבטחה) - FileFox ימשיך
            // לרוץ כרגיל, פשוט בלי הפעלה אוטומטית. עדיף מלקרוס על הגדרה משנית.
        }
    }

    /// <summary>
    /// המצב האמיתי כרגע ברישום של Windows - לא ההגדרה השמורה ב-settings.json. השניים
    /// יכולים להתבדר בפועל (למשל ערך שנמחק ידנית מ-Task Manager ← Startup, או אנטי-וירוס
    /// שניקה אותו), ולכן מסך ההגדרות מציג את זה במקום לסמוך עיוורת על ההגדרה השמורה.
    /// </summary>
    public static bool IsCurrentlyEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) != null;
        }
        catch
        {
            return false;
        }
    }
}
