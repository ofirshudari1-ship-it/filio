using System.IO;
using Tesseract;

namespace Filio.Services;

/// <summary>
/// חילוץ טקסט מתמונות (JPG/PNG) באמצעות מנוע OCR מקומי (Tesseract) - זה מה שמאפשר ל-Filio
/// לסווג גם צילום של חשבונית/קבלה, לא רק PDF עם שכבת טקסט. בדיוק כמו PdfTextService, רץ
/// לגמרי על המחשב: שום תמונה, ואף חלק ממנה, לא נשלחת לשום שרת חיצוני.
///
/// נתוני השפה (eng.traineddata, heb.traineddata) חייבים לשבת בתיקיית "tessdata" לצד קובץ
/// ה-exe - מותקנים אוטומטית עם Filio. אם הם חסרים (למשל התקנה פגומה), OCR נכשל בשקט
/// ו-ExtractText מחזירה מחרוזת ריקה, בדיוק כמו PDF סרוק היום - הקובץ עדיין מסווג לפי שם.
/// </summary>
public static class ImageTextService
{
    private static readonly string[] SupportedExtensions = { ".jpg", ".jpeg", ".png" };

    // מנוע Tesseract לא Thread-Safe לקריאות Process() מקבילות, אבל FileWatcherService יכול
    // לעבד כמה קבצים בו-זמנית (כמה תיקיות מעקב) - הנעילה מונעת קריסה/תוצאה שגויה מגישה
    // מקבילה לאותו מופע מנוע, בלי לוותר על שיתוף מנוע יחיד (טעינת נתוני השפה איטית).
    private static readonly object EngineLock = new();
    private static TesseractEngine? _engine;

    // תמונה חריגה בגודלה (תמונת מצלמה ברזולוציה גבוהה מאוד, סריקה לא-מכווצת וכו') יכולה
    // לקחת ל-Tesseract עשרות שניות עד דקות - ומכיוון שהמנוע חולק ננעל (Lock יחיד, למעלה),
    // קובץ אחד כזה חוסם בפועל את כל שאר הקבצים הממתינים ל-OCR בכל תיקיות המעקב, לא רק את
    // עצמו. במקום זאת מדלגים על OCR לקבצים חריגים בגודלם ונופלים חזרה לסיווג לפי שם הקובץ
    // בלבד (כמו PDF סרוק היום) - עדיף תיוג לפי שם מאשר קיפאון של כל התור.
    private const long MaxOcrFileSizeBytes = 15 * 1024 * 1024; // 15MB

    public static bool IsSupportedImage(string filePath) =>
        SupportedExtensions.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase);

    public static string ExtractText(string filePath)
    {
        if (!IsSupportedImage(filePath))
            return string.Empty;

        try
        {
            var info = new FileInfo(filePath);
            if (info.Exists && info.Length > MaxOcrFileSizeBytes)
            {
                DiagnosticLogger.Warn($"Skipping OCR for unusually large image ({info.Length / 1024 / 1024}MB, limit 15MB): {filePath} — filing by filename only.");
                return string.Empty;
            }

            lock (EngineLock)
            {
                var engine = GetOrCreateEngine();
                if (engine == null)
                    return string.Empty;

                using var image = Pix.LoadFromFile(filePath);
                using var page = engine.Process(image);
                return page.GetText() ?? string.Empty;
            }
        }
        catch
        {
            // מנוע OCR לא זמין, נתוני שפה חסרים, או קובץ תמונה פגום - לא עוצרים את התיוק,
            // פשוט נופלים חזרה לסיווג לפי שם הקובץ בלבד (כמו PDF סרוק).
            return string.Empty;
        }
    }

    /// <summary>יוצר את מנוע ה-OCR פעם אחת בלבד אחרי הצלחה (טעינת נתוני השפה איטית) ומשתף
    /// אותו בין קריאות. נקרא כבר בתוך EngineLock. בכוונה לא "נועל" כישלון לצמיתות - אם
    /// האתחול נכשל פעם אחת (למשל תיקיית tessdata נעולה רגעית ע"י אנטי-וירוס בזמן ההתקנה),
    /// כל קריאה הבאה תנסה שוב במקום לוותר על OCR עד הפעלה מחדש של האפליקציה. אתחול כושל
    /// נכשל מהר (קבצים חסרים/פגומים), אז זה לא יוצר עומס אמיתי גם אם tessdata חסרה לצמיתות.</summary>
    private static TesseractEngine? GetOrCreateEngine()
    {
        if (_engine != null)
            return _engine;

        try
        {
            var tessdataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
            _engine = new TesseractEngine(tessdataPath, "eng+heb", EngineMode.Default);
        }
        catch
        {
            _engine = null;
        }

        return _engine;
    }
}
