using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Filio.Services;

namespace Filio.Models;

/// <summary>סדר תתי-התיקיות מתחת לתיקיית היעד. ברירת המחדל (לקוח/שנה/סוג) מתאימה לרוב
/// המשתמשים, אבל חלקם מעדיפים לארגן קודם לפי שנה או לפי סוג מסמך.</summary>
public enum FolderStructureOrder
{
    ClientYearType,
    YearClientType,
    TypeClientYear
}

public enum AppTheme { Light, Dark }

public class AppSettings
{
    /// <summary>
    /// יותר מתיקיית מעקב אחת (למשל הורדות + שולחן עבודה + תיקיית צירופי מייל) - מגבלה נפוצה
    /// בכלים מתחרים כמו Hazel/DropIt שתומכים רק בתיקייה בודדת אמיתית.
    /// </summary>
    public ObservableCollection<string> WatchFolders { get; set; } =
        new() { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads" };

    public string RootOutputFolder { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Documents\\Filio";

    public bool StartWithWindows { get; set; } = true;

    public bool MoveInsteadOfCopy { get; set; } = true;

    public bool ShowNotifications { get; set; } = true;

    /// <summary>כשמופעל (ורק אם ShowNotifications מופעל): מציג הודעת מגש רק על תיוק שנכשל,
    /// לא על כל קובץ שתויק בהצלחה - מפחית רעש למשתמש שמתייק הרבה קבצים ביום ורוצה לדעת
    /// רק כשמשהו דורש תשומת לב. כבוי כברירת מחדל כדי לא לשנות התנהגות קיימת בלי בחירה מודעת.</summary>
    public bool NotifyOnlyOnFailure { get; set; } = false;

    public bool IsPaused { get; set; } = false;

    /// <summary>מזהה קבצים כפולים לפי תוכן (hash) לפני תיוק, ומפנה אותם ל"כפילות אפשריות" במקום לדרוס בשקט.</summary>
    public bool DetectDuplicates { get; set; } = true;

    /// <summary>
    /// רק קבצים עם אחת הסיומות האלה נשקלים לתיוק - כל השאר (ZIP, קבצי התקנה, וידאו וכו')
    /// נשארים בתיקיית ההורדות בלי שנוגעים בהם. בלי המגבלה הזו, כל קובץ שנוחת בתיקיית המעקב
    /// היה מועבר ל"לא מזוהה" - בדיוק התקלה האמיתית שגרמה לבלבול ולתחושת "לא ברור מה קורה".
    /// כולל תמונות (jpg/png) כברירת מחדל - אלה נסרקות ב-OCR מקומי (ראו EnableImageOcr) כדי
    /// שגם צילום של חשבונית/קבלה יסווג, לא רק PDF.
    /// </summary>
    public string WatchedFileExtensions { get; set; } = ".pdf, .doc, .docx, .xls, .xlsx, .ppt, .pptx, .jpg, .jpeg, .png";

    /// <summary>
    /// זיהוי טקסט בתמונות (OCR מקומי, Tesseract) - מאפשר לסווג צילום/סריקה של חשבונית לפי
    /// התוכן שלה, לא רק לפי שם הקובץ. רץ לגמרי על המחשב, בלי לשלוח שום תמונה החוצה. איטי
    /// יותר מקריאת PDF (כמה שניות לתמונה), ולכן ניתן לכבות למי שרוצה מהירות על פני דיוק.
    /// </summary>
    public bool EnableImageOcr { get; set; } = true;

    /// <summary>
    /// לתייק גם קובצי התקנה (exe/msi) לתיקיית "_Installers" נפרדת (לא מעורבים עם מסמכי
    /// לקוחות, כי אין להם "לקוח" או "סוג מסמך" משמעותיים). כבוי כברירת מחדל - בניגוד
    /// למסמכים ותמונות, קובצי התקנה לפעמים נועדו להרצה מיידית, אז זו הרחבה שהמשתמש בוחר
    /// בעצמו להפעיל, לא ברירת מחדל "לתייק הכל".
    /// </summary>
    public bool OrganizeInstallers { get; set; } = false;

    /// <summary>
    /// לבדוק כל קובץ מול Windows Defender (האנטי-וירוס המובנה והחינמי של Windows) לפני
    /// שהוא מתויק. קובץ שמזוהה כאיום נשאר במקומו המקורי ולא עובר תיוק. Filio לא בונה
    /// מנוע אבטחה משלו - הוא רק שואל את Defender מה הוא כבר יודע/בודק.
    /// </summary>
    public bool EnableDefenderScan { get; set; } = true;

    /// <summary>
    /// כשמופעל: קובץ שהסיווג האוטומטי לא זיהה בו לא לקוח ולא סוג מסמך (ביטחון נמוך משמעותית)
    /// לא מתויק אוטומטית - הוא נשאר במקומו המקורי ומסומן ביומן הפעילות כ"ממתין לבדיקה", עד
    /// שהמשתמש בוחר ידנית לקוח/סוג מתאים דרך כפתור "תקן/תייק". כבוי כברירת מחדל - שינוי
    /// התנהגות ברירת המחדל (תיוג אוטומטי תמיד) דורש בחירה מודעת של המשתמש, לא ברירת מחדל
    /// חדשה שמפתיעה משתמשים קיימים. בהשראת המלצת "review before file" ממחקר תחרותי (Hazel).
    /// </summary>
    public bool ReviewLowConfidenceMatches { get; set; } = false;

    /// <summary>קבצים ששם שלהם מכיל אחת המילים/התבניות האלה מדולגים לגמרי, גם אם הסיומת
    /// שלהם נצפית - שליטה עדינה מעבר לרשימת הסיומות, למשל קובץ ספציפי שחוזר על עצמו.</summary>
    public string IgnoredFileNamePatterns { get; set; } = string.Empty;

    /// <summary>סדר תתי-התיקיות מתחת לתיקיית היעד (לקוח/שנה/סוג כברירת מחדל).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FolderStructureOrder FolderOrder { get; set; } = FolderStructureOrder.ClientYearType;

    /// <summary>ערכת נושא: בהירה (ברירת מחדל) או כהה.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppTheme Theme { get; set; } = AppTheme.Light;

    /// <summary>כאשר המשתמש לוחץ X, מינימיזציה למגש במקום יציאה מהאפליקציה (ברירת מחדל: כן).</summary>
    public bool MinimizeToTrayOnClose { get; set; } = true;

    /// <summary>מספר הימים לשמור רשומות ביומן הפעילות לפני ניקוי אוטומטי. 0 = שמור לנצח.</summary>
    public int LogRetentionDays { get; set; } = 90;

    /// <summary>"en" (ברירת מחדל) או "he".</summary>
    public string Language { get; set; } = LocalizationService.English;

    public bool AutoCheckForUpdates { get; set; } = true;

    /// <summary>כתובת JSON שמתארת את הגרסה האחרונה. ריק = בדיקת עדכונים כבויה.</summary>
    public string UpdateFeedUrl { get; set; } = string.Empty;

    /// <summary>
    /// כשמופעל: עדכון שמתגלה מול GitHub Releases (ראו UpdateService.CheckGitHubReleaseAsync)
    /// מוריד ומתקין את עצמו בשקט ברקע, בלי לשאול - במקום להציג רק הודעת מגש עם קישור לעמוד
    /// ה-Release. כבוי כברירת מחדל (opt-in): התקנה שקטה שמחליטה בעצמה מתי לסגור ולהפעיל
    /// מחדש את Filio היא שינוי משמעותי בהתנהגות, שדורש בחירה מודעת של המשתמש - לא ברירת
    /// מחדל שמפתיעה מישהו שהתרגל לזרימה הישנה (התראה + הורדה/הפעלה ידנית).
    /// </summary>
    public bool AutoInstallUpdates { get; set; } = false;

    /// <summary>false בהתקנה ראשונה - גורם לאשף ה"ברוכים הבאים" לרוץ במקום לקפוץ ישר למסך ההגדרות.</summary>
    public bool HasCompletedOnboarding { get; set; } = false;

    /// <summary>האם כבר הוצגה למשתמש ההודעה שמסבירה שסגירת החלון ממזערת למגש ולא סוגרת את Filio. מוצגת פעם אחת בלבד.</summary>
    public bool HasShownTrayMinimizeHint { get; set; } = false;

    public ObservableCollection<ClientProfile> Clients { get; set; } = new();

    public ObservableCollection<DocTypeRule> DocTypeRules { get; set; } = new();

    /// <summary>
    /// מעתיק את כל הערכים מ-<paramref name="other"/> לתוך המופע הזה במקום, בלי להחליף את
    /// המופע עצמו - חשוב כי MainWindow/FileWatcherService/App כולם מחזיקים הפניה לאותו
    /// אובייקט הגדרות; אם היינו רק מחליפים את ה-reference, השינוי לא היה מגיע לשם. משמש
    /// בייבוא הגדרות מקובץ.
    /// </summary>
    public void CopyFrom(AppSettings other)
    {
        WatchFolders.Clear();
        foreach (var folder in other.WatchFolders)
            WatchFolders.Add(folder);

        RootOutputFolder = other.RootOutputFolder;
        StartWithWindows = other.StartWithWindows;
        MoveInsteadOfCopy = other.MoveInsteadOfCopy;
        ShowNotifications = other.ShowNotifications;
        NotifyOnlyOnFailure = other.NotifyOnlyOnFailure;
        IsPaused = other.IsPaused;
        DetectDuplicates = other.DetectDuplicates;
        WatchedFileExtensions = other.WatchedFileExtensions;
        EnableImageOcr = other.EnableImageOcr;
        OrganizeInstallers = other.OrganizeInstallers;
        EnableDefenderScan = other.EnableDefenderScan;
        ReviewLowConfidenceMatches = other.ReviewLowConfidenceMatches;
        IgnoredFileNamePatterns = other.IgnoredFileNamePatterns;
        FolderOrder = other.FolderOrder;
        Theme = other.Theme;
        MinimizeToTrayOnClose = other.MinimizeToTrayOnClose;
        LogRetentionDays = other.LogRetentionDays;
        Language = other.Language;
        AutoCheckForUpdates = other.AutoCheckForUpdates;
        UpdateFeedUrl = other.UpdateFeedUrl;
        AutoInstallUpdates = other.AutoInstallUpdates;
        HasShownTrayMinimizeHint = other.HasShownTrayMinimizeHint;

        Clients.Clear();
        foreach (var client in other.Clients)
            Clients.Add(client);

        DocTypeRules.Clear();
        foreach (var rule in other.DocTypeRules)
            DocTypeRules.Add(rule);
    }

    public static AppSettings CreateDefault()
    {
        var settings = new AppSettings();

        settings.DocTypeRules.Add(new DocTypeRule
        {
            TypeName = "Invoices",
            Keywords = "חשבונית, חשבונית מס, חשבונית מס-קבלה, invoice, receipt, קבלה, tax invoice",
            FileNamePatterns = "invoice, חשבונית, קבלה, receipt"
        });
        settings.DocTypeRules.Add(new DocTypeRule
        {
            TypeName = "Contracts",
            Keywords = "הסכם, חוזה, contract, agreement, תנאי התקשרות, הואיל וברצון הצדדים",
            FileNamePatterns = "contract, agreement, הסכם, חוזה"
        });
        settings.DocTypeRules.Add(new DocTypeRule
        {
            TypeName = "Reports",
            Keywords = "דוח, דו\"ח, report, סיכום חודשי, תקופת הדיווח, נספח",
            FileNamePatterns = "report, דוח, דו״ח"
        });
        settings.DocTypeRules.Add(new DocTypeRule
        {
            TypeName = "Unrecognized",
            Keywords = "",
            FileNamePatterns = "",
            IsFallback = true
        });

        return settings;
    }
}
