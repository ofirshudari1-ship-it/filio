using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using FileFox.Models;

namespace FileFox.Services;

/// <summary>נזרקת כשכתובת ה-feed/ההתקנה אינה HTTPS. עדכון רץ בשקט וללא אישור המשתמש, אז
/// כתובת לא-מוצפנת חושפת אותו להתקפת "אדם בתיווך" שמחליפה את קובץ ההתקנה בדרך.</summary>
public class InsecureUpdateUrlException : Exception
{
    public InsecureUpdateUrlException(string message) : base(message) { }
}

/// <summary>נזרקת כשה-hash של קובץ ההתקנה שהתקבל לא תואם את מה שהוצהר ב-manifest - כלומר
/// הקובץ בדרך השתנה (שרת שהוחלף, CDN פגום, תקיפה) ואסור להריץ אותו.</summary>
public class UpdateIntegrityException : Exception
{
    public UpdateIntegrityException(string message) : base(message) { }
}

/// <summary>
/// בדיקת גרסה חדשה מול קובץ manifest חיצוני, והפעלת עדכון "במקום": מוריד את קובץ ההתקנה
/// החדש ומריץ אותו בשקט (VERYSILENT) כדי לעדכן את ההתקנה הקיימת בלי אינטראקציה מהמשתמש.
/// לא דורש שרת ייעודי - כל קובץ JSON סטטי מתאים (למשל GitHub Releases / קובץ סטטי בכל אחסון).
///
/// שני עוגני אבטחה חשובים בזרימה הזו, כי היא מורידה ומריצה קובץ הפעלה בלי שהמשתמש רואה
/// כל שלב: (1) גם כתובת ה-feed וגם כתובת ההתקנה בתוכו חייבות להיות HTTPS - בלעדיו קובץ
/// ההתקנה יכול להוחלף בדרך על ידי כל מי שבין המחשב לשרת. (2) אם ה-manifest מצהיר על
/// sha256, מאמתים את הגיבוב בפועל מול הקובץ שהתקבל לפני שמריצים אותו.
/// </summary>
public class UpdateService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    // HttpClient.Timeout בודק את משך הקריאה כולה, כולל קריאת הגוף בזרימה - לא רק את
    // ההדרים. קובץ התקנה אמיתי (עשרות MB) לוקח יותר מ-15 שניות על כל חיבור שאינו מהיר
    // במיוחד, אז ההורדה חייבת לקוח נפרד עם חלון זמן ארוך משמעותית מבדיקת ה-manifest הקטנה.
    private static readonly HttpClient DownloadHttp = new() { Timeout = TimeSpan.FromMinutes(10) };

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public async Task<UpdateManifest?> CheckForUpdateAsync(string feedUrl)
    {
        if (string.IsNullOrWhiteSpace(feedUrl))
            return null;

        EnsureHttps(feedUrl);

        var json = await Http.GetStringAsync(feedUrl);
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (manifest == null || string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.InstallerUrl))
            return null;

        if (!Version.TryParse(manifest.Version, out var remoteVersion))
            return null;

        if (remoteVersion <= CurrentVersion)
            return null;

        // הבדיקה הזו קורית רק אחרי שכבר קבענו שיש עדכון רלוונטי - אחרת ערך ישן/שגוי
        // ב-installerUrl (למשל רשומת manifest ישנה שנשארה עם כתובת http:// מוקדמת) היה
        // גורם לחסימה בכל בדיקת עדכונים, גם כשאין בכלל עדכון להציע.
        EnsureHttps(manifest.InstallerUrl);

        return manifest;
    }

    private static void EnsureHttps(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InsecureUpdateUrlException(url);
    }

    public async Task<string> DownloadInstallerAsync(string installerUrl, string? expectedSha256 = null, IProgress<double>? progress = null)
    {
        EnsureHttps(installerUrl);

        var tempPath = Path.Combine(Path.GetTempPath(), $"FileFoxUpdate_{Guid.NewGuid():N}.exe");

        try
        {
            using var response = await DownloadHttp.GetAsync(installerUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using (var httpStream = await response.Content.ReadAsStreamAsync())
            await using (var fileStream = File.Create(tempPath))
            {
                var buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await httpStream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    totalRead += bytesRead;

                    if (totalBytes > 0)
                        progress?.Report((double)totalRead / totalBytes * 100);
                }
            }

            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                var actualHash = await ComputeSha256Async(tempPath);
                if (!string.Equals(actualHash, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(tempPath);
                    throw new UpdateIntegrityException(actualHash);
                }
            }

            return tempPath;
        }
        catch
        {
            // הורדה שנקטעת (רשת נופלת, timeout באמצע קובץ גדול) הייתה משאירה קובץ .exe
            // חלקי ב-%TEMP% לצמיתות - כל ניסיון עדכון כושל מצטבר עוד קובץ יתום. מנקים
            // בכל כשל, לא רק באי-התאמת חתימה, ואז זורקים הלאה כדי ש-MainWindow עדיין יציג
            // את הודעת השגיאה המתאימה.
            TryDelete(tempPath);
            throw;
        }
    }

    private static async Task<string> ComputeSha256Async(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(stream);
        return Convert.ToHexString(hashBytes);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best-effort - אם ננעל לרגע לא קריטי */ }
    }

    /// <summary>
    /// מריץ את קובץ ההתקנה בשקט (ללא אשף) ובלי לבקש הפעלה מחדש של Windows. Inno Setup
    /// מזהה שהתוכנה רצה (CloseApplications) וסוגר/מפעיל אותה מחדש אוטומטית בסיום.
    /// קוראים לפעולה הזו ואז יוצאים מהתוכנה הנוכחית.
    ///
    /// חשוב: /LANG= חובה גם ב-VERYSILENT - בלעדיו Inno Setup עדיין מציג את דיאלוג בחירת
    /// שפת ההתקנה בהרצה הראשונה על מכונה נתונה, מה שהופך עדכון "שקט" לתקוע וממתין לקלט
    /// שאף אחד לא רואה. תוקן אחרי שנתפס בבדיקה בפועל של ההתקנה.
    /// </summary>
    public static void LaunchSilentInstall(string installerPath)
    {
        var lang = LocalizationService.CurrentLanguage == LocalizationService.Hebrew ? "hebrew" : "english";

        Process.Start(new ProcessStartInfo(installerPath,
            $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LANG={lang} /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS")
        {
            UseShellExecute = true
        });
    }
}
