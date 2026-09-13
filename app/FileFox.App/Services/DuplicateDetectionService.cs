using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace FileFox.Services;

/// <summary>
/// זיהוי קבצים כפולים לפי תוכן (SHA-256), לא לפי שם. שומר אינדקס JSON קטן ומקומי של
/// hash → נתיב הקובץ המתויק. בכוונה לא מוחק ולא דורס כלום - מחקר על כלים מתחרים הראה
/// שמשתמשים רוצים שקיפות ושליטה על כפילויות, לא מחיקה אוטומטית "עיוורת".
/// </summary>
public class DuplicateDetectionService
{
    private readonly string _indexPath;

    // מגן על _hashToPath: כמה קבצים מתויקים כמעט בו-זמנית (למשל שתי תיקיות מעקב מקבלות
    // קובץ באותה שנייה, או כמה watcher-ים מתאוששים יחד) קוראים ל-FindExistingDuplicate/
    // RememberFiledFile על thread-ים נפרדים - בלי נעילה, כתיבה בו-זמנית ל-Dictionary
    // רגיל יכולה לזרוק חריגה או לשחית את מבנה הנתונים הפנימי שלו.
    private readonly object _lock = new();
    private Dictionary<string, string> _hashToPath;

    public DuplicateDetectionService(string? indexPath = null)
    {
        _indexPath = indexPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileFox", "duplicate-index.json");

        _hashToPath = Load();
    }

    /// <summary>מחשב hash לקובץ ומחזיר את הנתיב הקיים אם כבר תויק קובץ זהה בעבר, אחרת null.</summary>
    public string? FindExistingDuplicate(string filePath)
    {
        var hash = ComputeHash(filePath);
        if (hash == null) return null;

        lock (_lock)
        {
            if (_hashToPath.TryGetValue(hash, out var existingPath) && File.Exists(existingPath))
                return existingPath;
        }

        return null;
    }

    /// <summary>רושם קובץ שתויק בהצלחה כדי שיזוהה אם ינחת שוב (או עותק שלו) בעתיד.</summary>
    public void RememberFiledFile(string filePath)
    {
        var hash = ComputeHash(filePath);
        if (hash == null) return;

        lock (_lock)
        {
            _hashToPath[hash] = filePath;
            Save();
        }
    }

    private static string? ComputeHash(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var hashBytes = SHA256.HashData(stream);
            return Convert.ToHexString(hashBytes);
        }
        catch
        {
            // קובץ לא נגיש/נעול - פשוט מדלגים על בדיקת כפילות עבורו, לא עוצרים את התיוק
            return null;
        }
    }

    private Dictionary<string, string> Load()
    {
        try
        {
            if (!File.Exists(_indexPath))
                return new Dictionary<string, string>();

            var json = File.ReadAllText(_indexPath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_indexPath);
            if (directory != null)
                Directory.CreateDirectory(directory);

            // שומרים רק את 2000 הרשומות האחרונות כדי שהאינדקס לא יתנפח לנצח
            if (_hashToPath.Count > 2000)
                _hashToPath = _hashToPath.Skip(_hashToPath.Count - 2000).ToDictionary(kv => kv.Key, kv => kv.Value);

            File.WriteAllText(_indexPath, JsonSerializer.Serialize(_hashToPath));
        }
        catch
        {
            // כשל בשמירת האינדקס לא אמור להפיל את תהליך התיוק עצמו
        }
    }
}
