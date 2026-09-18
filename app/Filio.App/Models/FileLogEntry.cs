using System.Text.Json.Serialization;
using Filio.Services;

namespace Filio.Models;

public class FileLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;

    public string OriginalPath { get; set; } = string.Empty;

    public string? NewPath { get; set; }

    public string DetectedType { get; set; } = string.Empty;

    public string DetectedClient { get; set; } = string.Empty;

    public string DetectedDate { get; set; } = string.Empty;

    /// <summary>הסבר בשפה פשוטה למה הקובץ סווג/זוהה כך - מוצג כ-tooltip על השורה ביומן הפעילות.</summary>
    public string? MatchExplanation { get; set; }

    public bool Success { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>true אם זוהה כתוכן זהה לקובץ שכבר תויק בעבר - הועבר ל"כפילויות אפשריות" במקום למקום הרגיל.</summary>
    public bool IsDuplicate { get; set; }

    public string? DuplicateOfPath { get; set; }

    /// <summary>true אם Windows Defender סימן את הקובץ כאיום לפני שהוא תויק - הקובץ נשאר
    /// במקומו המקורי במקרה כזה (Success יהיה false גם כן).</summary>
    public bool ThreatDetected { get; set; }

    /// <summary>true כאשר הסיווג האוטומטי לא זיהה לא לקוח ולא סוג מסמך (ביטחון נמוך) וההגדרה
    /// "בדיקה לפני תיוק" מופעלת - הקובץ נשאר במקומו המקורי (לא הועבר בכלל, NewPath ריק) עד
    /// שהמשתמש יבחר ידנית לקוח/סוג מתוך יומן הפעילות ("תקן/תייק"). בהשראת Hazel, שמאפשר
    /// לבדוק כלל לפני שהוא רץ במקום לתייק עיוורת ולתקן אחר כך.</summary>
    public bool NeedsReview { get; set; }

    [JsonIgnore]
    public string StatusText
    {
        get
        {
            if (ThreatDetected)
                return LocalizationService.Get("LogStatusThreatDetected");

            if (NeedsReview)
                return LocalizationService.Get("LogStatusNeedsReview");

            if (!Success)
                return LocalizationService.Format("LogStatusErrorFormat", ErrorMessage ?? string.Empty);

            return IsDuplicate ? LocalizationService.Get("LogStatusDuplicate") : LocalizationService.Get("LogStatusFiled");
        }
    }
}
