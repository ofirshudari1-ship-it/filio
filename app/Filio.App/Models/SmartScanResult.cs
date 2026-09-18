namespace Filio.Models;

public class SuggestedClient
{
    public string Name { get; set; } = string.Empty;
    public int MatchCount { get; set; }

    /// <summary>האם המשתמש סימן את ההצעה הזו להוספה בפועל לרשימת הלקוחות (משמש ב-UI בלבד).</summary>
    public bool IsSelected { get; set; } = true;
}

/// <summary>
/// תוצאת "סריקה חכמה" חד-פעמית ומקומית לגמרי (ללא שום קריאת רשת) שרצה בזמן ההגדרה
/// הראשונית: מריצה את אותו מנוע הסיווג המקומי על קבצים קיימים כדי להציע לקוחות ולהראות
/// כמה מהקבצים כבר מזוהים נכון על ידי חוקי ברירת המחדל.
/// </summary>
public class SmartScanResult
{
    public int FilesScanned { get; set; }
    public int FilesWithReadableText { get; set; }
    public int RecognizedCount { get; set; }
    public int UnrecognizedCount { get; set; }
    public List<SuggestedClient> SuggestedClients { get; set; } = new();
    public Dictionary<string, int> DocTypeCounts { get; set; } = new();
}
