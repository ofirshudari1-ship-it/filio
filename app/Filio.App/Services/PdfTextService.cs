using UglyToad.PdfPig;

namespace Filio.Services;

/// <summary>
/// חילוץ טקסט מקומי מקובצי PDF (ללא שירות חיצוני). עובד רק על PDF עם טקסט אמיתי -
/// סריקות/תמונות (PDF סרוק) לא נתמכות בגרסה הזו ומחזירות מחרוזת ריקה.
/// </summary>
public static class PdfTextService
{
    private const int MaxPagesToRead = 3;

    public static string ExtractText(string filePath)
    {
        try
        {
            if (!filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            using var document = PdfDocument.Open(filePath);
            var pages = document.GetPages().Take(MaxPagesToRead);
            return string.Join("\n", pages.Select(p => p.Text));
        }
        catch
        {
            // קובץ נעול, פגום, או לא PDF תקין - לא עוצרים את התהליך, פשוט אין תוכן לנתח
            return string.Empty;
        }
    }
}
