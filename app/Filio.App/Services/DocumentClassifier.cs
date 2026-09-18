using System.IO;
using System.Text.RegularExpressions;
using Filio.Models;

namespace Filio.Services;

public class ClassificationResult
{
    public string DocType { get; set; } = "Unrecognized";
    public string ClientName { get; set; } = "Unknown Client";
    public DateTime DocumentDate { get; set; } = DateTime.Now;

    /// <summary>הסבר בשפה פשוטה למה המסמך סווג כך - כדי שהמשתמש יראה "למה" ולא רק "מה",
    /// מוצג כ-tooltip על שורת יומן הפעילות. זה ה"AI" של הכלי מסביר את עצמו במקום להישאר קופסה שחורה.</summary>
    public string MatchExplanation { get; set; } = string.Empty;
}

/// <summary>
/// מנוע הסיווג: חוקים (מילות מפתח + regex) על שם הקובץ ועל תוכן ה-PDF שחולץ.
/// זוהי ה"בינה" של הכלי - היא רצה לגמרי מקומית, ללא קריאה לשירות חיצוני, ולכן חינמית ומהירה.
/// </summary>
public static class DocumentClassifier
{
    // תומך בפורמטים נפוצים: 31/12/2025, 31.12.2025, 31-12-2025, 2025-12-31
    private static readonly Regex[] DatePatterns =
    {
        new(@"\b(?<d>\d{1,2})[./-](?<m>\d{1,2})[./-](?<y>\d{4})\b", RegexOptions.Compiled),
        new(@"\b(?<y>\d{4})[./-](?<m>\d{1,2})[./-](?<d>\d{1,2})\b", RegexOptions.Compiled),
    };

    private static readonly string[] ClientHintPrefixes = { "לכבוד", "עבור", "שם הלקוח", "לקוח:", "client:", "bill to", "customer:" };

    public static ClassificationResult Classify(string fileName, string content, IEnumerable<DocTypeRule> rules, IEnumerable<ClientProfile> clients, DateTime fileCreatedUtc)
    {
        var (docType, docTypeReason) = ClassifyDocType(fileName, content, rules);
        var (clientName, clientReason) = ClassifyClient(fileName, content, clients);
        var (date, dateReason) = ExtractDate(fileName, content, fileCreatedUtc);

        return new ClassificationResult
        {
            DocType = docType,
            ClientName = clientName,
            DocumentDate = date,
            MatchExplanation = string.Join("  •  ", new[] { docTypeReason, clientReason, dateReason })
        };
    }

    private static (string TypeName, string Reason) ClassifyDocType(string fileName, string content, IEnumerable<DocTypeRule> rules)
    {
        var ruleList = rules.ToList();
        var normalizedContent = content.ToLowerInvariant();
        var normalizedFileName = fileName.ToLowerInvariant();

        DocTypeRule? bestRule = null;
        var bestScore = 0;
        string? bestMatchedText = null;
        var bestMatchedInContent = false;

        foreach (var rule in ruleList.Where(r => !r.IsFallback))
        {
            var score = 0;
            string? matchedText = null;
            var matchedInContent = false;

            foreach (var keyword in rule.KeywordList)
            {
                if (normalizedContent.Contains(keyword.ToLowerInvariant()))
                {
                    score += 2; // התאמה בתוכן המסמך שווה יותר מהתאמה בשם הקובץ
                    matchedText ??= keyword;
                    matchedInContent = true;
                }
            }

            foreach (var pattern in rule.FileNamePatternList)
            {
                if (normalizedFileName.Contains(pattern.ToLowerInvariant()))
                {
                    score += 1;
                    matchedText ??= pattern;
                }
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestRule = rule;
                bestMatchedText = matchedText;
                bestMatchedInContent = matchedInContent;
            }
        }

        if (bestRule != null && bestScore > 0)
        {
            var reason = LocalizationService.Format(
                bestMatchedInContent ? "ExplainDocTypeContent" : "ExplainDocTypeFileName",
                bestRule.TypeName, bestMatchedText ?? string.Empty);
            return (bestRule.TypeName, reason);
        }

        var fallback = ruleList.FirstOrDefault(r => r.IsFallback);
        if (fallback != null)
            return (fallback.TypeName, LocalizationService.Format("ExplainDocTypeFallback", fallback.TypeName));

        return ("Unrecognized", LocalizationService.Get("ExplainDocTypeNone"));
    }

    private static (string ClientName, string Reason) ClassifyClient(string fileName, string content, IEnumerable<ClientProfile> clients)
    {
        var haystack = (fileName + "\n" + content).ToLowerInvariant();

        foreach (var client in clients)
        {
            if (string.IsNullOrWhiteSpace(client.Name))
                continue;

            var aliases = new List<string> { client.Name };
            aliases.AddRange(client.Keywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            var matchedAlias = aliases.FirstOrDefault(alias => alias.Length > 0 && haystack.Contains(alias.ToLowerInvariant()));
            if (matchedAlias != null)
                return (client.FolderName, LocalizationService.Format("ExplainClientAlias", client.FolderName, matchedAlias));
        }

        // ניסיון חילוץ שם חברה/לקוח ליד רמזים נפוצים במסמכים עסקיים בעברית/אנגלית
        var guessedName = TryGuessClientNameFromContent(content);
        return guessedName != null
            ? (guessedName, LocalizationService.Format("ExplainClientGuessed", guessedName))
            : ("Unknown Client", LocalizationService.Get("ExplainClientUnknown"));
    }

    private static string? TryGuessClientNameFromContent(string content)
    {
        foreach (var prefix in ClientHintPrefixes)
        {
            var idx = content.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;

            // ":"/רווח/טאב נגררים בדרך כלל אחרי "לקוח:" וכו', אבל לפעמים השם מתחיל בשורה הבאה (אחרי "לכבוד")
            var afterPrefix = content[(idx + prefix.Length)..].TrimStart(':', ' ', '\t', '\r', '\n');
            var lineEnd = afterPrefix.IndexOfAny(new[] { '\n', '\r' });
            var candidate = (lineEnd >= 0 ? afterPrefix[..lineEnd] : afterPrefix).Trim();

            if (candidate.Length is > 1 and < 60)
                return SanitizeForFolderName(candidate);
        }

        return null;
    }

    private static (DateTime Date, string Reason) ExtractDate(string fileName, string content, DateTime fileCreatedUtc)
    {
        // מעדיפים תאריך מתוך תוכן המסמך; אם לא נמצא, מנסים משם הקובץ
        var sources = new[] { (content, "ExplainDateContent"), (fileName, "ExplainDateFileName") };

        foreach (var (source, reasonKey) in sources)
        {
            foreach (var pattern in DatePatterns)
            {
                var match = pattern.Match(source);
                if (!match.Success) continue;

                if (int.TryParse(match.Groups["d"].Value, out var day) &&
                    int.TryParse(match.Groups["m"].Value, out var month) &&
                    int.TryParse(match.Groups["y"].Value, out var year))
                {
                    try
                    {
                        return (new DateTime(year, month, day), LocalizationService.Get(reasonKey));
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // תאריך לא חוקי (למשל חודש 13) - ממשיכים לחפש התאמה אחרת
                    }
                }
            }
        }

        return (fileCreatedUtc.ToLocalTime(), LocalizationService.Get("ExplainDateFallback"));
    }

    // מילים נפוצות מדי מכדי לשמש כרמז-לקוח נלמד (סוג מסמך/תבנית שם קובץ גנרית, לא שם לקוח).
    private static readonly HashSet<string> GenericFileNameWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "invoice", "receipt", "contract", "agreement", "report", "scan", "scanned", "document",
        "doc", "copy", "final", "new", "untitled", "img", "image", "file", "download", "company",
        "חשבונית", "קבלה", "חוזה", "הסכם", "דוח", "דוח", "סריקה", "מסמך", "העתק", "סופי", "חברת", "חברה",
    };

    /// <summary>
    /// "למידה מתיקון משתמש": כשהמשתמש מתקן ידנית לאיזה לקוח קובץ שייך (למידה ומעלה שדרוג
    /// אוטומציה - ראו MainWindow.FixClient), מנחשים מילה אחת מתוך שם הקובץ שכדאי לזכור בתור
    /// כינוי/מילת-מפתח של הלקוח הזה, כדי שקבצים דומים בעתיד יזוהו אוטומטית בלי תיקון חוזר.
    /// היוריסטיקה פשוטה בכוונה (המנוע כולו מקומי וללא AI חיצוני): המילה הכי ארוכה בשם הקובץ
    /// שאינה גנרית/מספרית/תאריך - לרוב זה שם החברה/הלקוח במסמכים אמיתיים (למשל
    /// "חשבונית-חברת-כרמל-2026.pdf" -&gt; "כרמל").
    /// </summary>
    public static string? SuggestClientKeywordFromFileName(string fileName)
    {
        var nameOnly = Path.GetFileNameWithoutExtension(fileName);
        var words = nameOnly.Split(new[] { ' ', '-', '_', '.', ',', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);

        return words
            .Where(w => w.Length >= 3)
            .Where(w => !w.All(char.IsDigit)) // תאריכים/מספרי מסמך לא מלמדים כלום על זהות הלקוח
            .Where(w => !GenericFileNameWords.Contains(w))
            .OrderByDescending(w => w.Length)
            .FirstOrDefault();
    }

    public static string SanitizeForFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return cleaned.Length == 0 ? "Unnamed" : cleaned;
    }
}
