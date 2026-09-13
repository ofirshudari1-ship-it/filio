using System.IO;
using FileFox.Models;

namespace FileFox.Services;

/// <summary>
/// "AI-lite" מקומי: לא קורא לשום שירות חיצוני ולא שולח קובץ אחד החוצה. פשוט מריץ את
/// אותו מנוע הסיווג המקומי (DocumentClassifier) על קבצי PDF קיימים בתיקיות המעקב כדי
/// לנחש לקוחות ולדווח כמה קבצים כבר מזוהים נכון על ידי חוקי ברירת המחדל - שימושי בתור
/// המלצה חד-פעמית באשף ההגדרה הראשוני, כדי שהמשתמש לא יתחיל ממסך ריק.
/// </summary>
public static class SmartScanService
{
    private const int MaxFilesToScan = 40;

    /// <summary>גרסה לתיקייה בודדת - נוחה לבדיקות ולשימושים פשוטים.</summary>
    public static SmartScanResult Scan(string folderPath, IEnumerable<DocTypeRule> rules) =>
        Scan(new[] { folderPath }, rules);

    public static SmartScanResult Scan(IEnumerable<string> folderPaths, IEnumerable<DocTypeRule> rules)
    {
        var result = new SmartScanResult();
        var ruleList = rules.ToList();
        var fallbackTypeName = ruleList.FirstOrDefault(r => r.IsFallback)?.TypeName;

        var files = new List<FileInfo>();
        foreach (var folderPath in folderPaths.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(folderPath))
                continue;

            try
            {
                files.AddRange(new DirectoryInfo(folderPath).GetFiles("*.pdf", SearchOption.TopDirectoryOnly));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        files = files.OrderByDescending(f => f.LastWriteTimeUtc).Take(MaxFilesToScan).ToList();
        result.FilesScanned = files.Count;

        var clientCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            string content;
            try
            {
                content = PdfTextService.ExtractText(file.FullName);
            }
            catch
            {
                content = string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(content))
                result.FilesWithReadableText++;

            // רשימת לקוחות ריקה בכוונה - כך שהמנוע תמיד ינסה לנחש שם לקוח מתוך התוכן
            // עצמו, בדיוק מה שדרוש כדי להציע לקוחות שהמשתמש עוד לא הגדיר.
            var classification = DocumentClassifier.Classify(
                file.Name, content, ruleList, Enumerable.Empty<ClientProfile>(), file.CreationTimeUtc);

            result.DocTypeCounts[classification.DocType] =
                result.DocTypeCounts.GetValueOrDefault(classification.DocType) + 1;

            // אם לא הוגדר חוק ברירת מחדל, DocumentClassifier מחזיר את המחרוזת המילולית
            // "Unrecognized" (לא null) - fallbackTypeName נשאר null באותו מקרה, אז ההשוואה
            // חייבת ליפול חזרה לאותה מחרוזת ברירת מחדל, אחרת כל קובץ לא-מזוהה נספר בטעות
            // כ"מזוהה" (fallbackTypeName==null לעולם לא שווה ל-DocType בפועל).
            if (classification.DocType == (fallbackTypeName ?? "Unrecognized"))
                result.UnrecognizedCount++;
            else
                result.RecognizedCount++;

            if (classification.ClientName != "Unknown Client")
            {
                clientCounts[classification.ClientName] =
                    clientCounts.GetValueOrDefault(classification.ClientName) + 1;
            }
        }

        result.SuggestedClients = clientCounts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new SuggestedClient { Name = kv.Key, MatchCount = kv.Value })
            .ToList();

        return result;
    }
}
