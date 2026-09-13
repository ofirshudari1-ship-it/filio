using System.Windows;

namespace FileFox.Services;

/// <summary>
/// עברית ↔ English בזמן ריצה, בלי צורך להפעיל מחדש: מחליף את מילון המשאבים הממוזג
/// שמכיל את כל הטקסטים, כך שכל בינדינג {DynamicResource} מתעדכן אוטומטית.
/// </summary>
public static class LocalizationService
{
    public const string English = "en";
    public const string Hebrew = "he";

    private static ResourceDictionary? _currentLanguageDictionary;

    public static string CurrentLanguage { get; private set; } = English;

    public static void SetLanguage(string languageCode)
    {
        var normalized = languageCode == Hebrew ? Hebrew : English;
        var uri = new Uri($"Resources/Strings.{normalized}.xaml", UriKind.Relative);
        var newDictionary = new ResourceDictionary { Source = uri };

        var merged = Application.Current.Resources.MergedDictionaries;

        if (_currentLanguageDictionary != null)
            merged.Remove(_currentLanguageDictionary);

        merged.Add(newDictionary);
        _currentLanguageDictionary = newDictionary;
        CurrentLanguage = normalized;
    }

    public static bool IsRightToLeft => CurrentLanguage == Hebrew;

    /// <summary>שליפת מחרוזת מתורגמת מקוד (למשל תפריט מגש/MessageBox), עם נפילה חזרה למפתח עצמו אם חסר.
    /// בטוח לקריאה גם מחוץ להקשר UI רץ (למשל בדיקות יחידה) - במקרה כזה פשוט מחזיר את המפתח.</summary>
    public static string Get(string key) =>
        Application.Current?.TryFindResource(key) as string ?? key;

    public static string Format(string key, params object[] args) =>
        string.Format(Get(key), args);
}
