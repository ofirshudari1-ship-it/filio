using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using Filio.Models;

namespace Filio.Services;

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
/// <summary>תוצאת בדיקת עדכונים מול GitHub Releases: הגרסה שפורסמה, עמוד ה-Release הציבורי
/// שלה, וכשקיים - כתובת ההורדה הישירה + הגודל המדווח של קובץ ההתקנה (Filio-Setup-X.Y.Z.exe)
/// המצורף ל-Release, כדי לאפשר עדכון שקט בלי לעבור דרך דפדפן.</summary>
public record GitHubUpdateResult(
    Version LatestVersion,
    string ReleaseUrl,
    string? InstallerAssetUrl = null,
    long? InstallerAssetSizeBytes = null);

public class UpdateService
{
    // המקור הרשמי להפצת Filio ולעדכוני גרסה - מאגר GitHub פומבי, כך שבדיקת העדכון היא
    // קריאת GET לא-מאומתת (unauthenticated) רגילה מול ה-REST API הציבורי, בלי טוקן/מפתח
    // כלשהו בקוד. ראו https://github.com/ofirshudari1-ship-it/filio/releases
    private const string GitHubReleasesApiUrl = "https://api.github.com/repos/ofirshudari1-ship-it/filio/releases/latest";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    // HttpClient.Timeout בודק את משך הקריאה כולה, כולל קריאת הגוף בזרימה - לא רק את
    // ההדרים. קובץ התקנה אמיתי (עשרות MB) לוקח יותר מ-15 שניות על כל חיבור שאינו מהיר
    // במיוחד, אז ההורדה חייבת לקוח נפרד עם חלון זמן ארוך משמעותית מבדיקת ה-manifest הקטנה.
    private static readonly HttpClient DownloadHttp = new() { Timeout = TimeSpan.FromMinutes(10) };

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    // שם/מיקום קבועים, מוכרים גם ל-Setup.cs (build\installer) - פרויקט נפרד שלא יכול
    // להפנות ל-UpdateService, אז שני הצדדים משתמשים באותה מוסכמת נתיב בכתיבה עצמאית.
    // ב-RunSilentInstall של Setup.cs: כשההתקנה השקטה נכשלת (קוד יציאה שונה מ-0 - רשת/דיסק
    // מלא/לא הצליח לסגור את Filio הרץ), הוא כותב לכאן לפני שהוא חוזר; Filio קורא את זה
    // בהפעלה הבאה (App.OnStartup) ומציג הודעת נפילה-בחזרה עם קישור להורדה ידנית, בדיוק
    // כמו כשל שנתפס ישירות (רשת/HTTPS/checksum) בזמן שהאפליקציה עדיין רצה.
    public const string UpdateFailedMarkerFileName = "update-failed.txt";

    public static string GetUpdateFailedMarkerPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Filio", UpdateFailedMarkerFileName);

    /// <summary>קורא ומוחק (best-effort, לא זורק) את קובץ הסימון שנכתב על ידי Setup.cs
    /// כשההתקנה השקטה נכשלה. מחזיר null אם אין סימון ממתין.</summary>
    public static string? TryConsumeUpdateFailedMarker()
    {
        var path = GetUpdateFailedMarkerPath();
        try
        {
            if (!File.Exists(path)) return null;
            var content = File.ReadAllText(path);
            File.Delete(path);
            return string.IsNullOrWhiteSpace(content) ? "unknown error" : content.Trim();
        }
        catch
        {
            return null;
        }
    }

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

    /// <summary>
    /// בדיקת עדכון פשוטה מול GitHub Releases: קריאת GET לא-מאומתת ל-releases/latest של
    /// המאגר הפומבי, השוואת tag_name לגרסה המותקנת, ומחזירה את כתובת עמוד ה-Release
    /// הציבורי (לא קובץ ההתקנה עצמו) כדי שהמשתמש יפתח אותו בדפדפן וימשיך ידנית משם.
    /// שונה במכוון מ-<see cref="CheckForUpdateAsync"/>: לא מורידה ולא מתקינה כלום בשקט,
    /// רק מודיעה שיש גרסה חדשה. נכשלת בשקט (מחזירה null) בכל תקלת רשת/פענוח - היא רצה
    /// אוטומטית ברקע כמה שניות אחרי עלייה, ואסור שתפריע/תיתקע את האתחול.
    /// </summary>
    public async Task<GitHubUpdateResult?> CheckGitHubReleaseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, GitHubReleasesApiUrl);
            // GitHub REST API מחזיר 403 לכל בקשה בלי User-Agent - חובה, גם לנקודת קצה פומבית.
            request.Headers.UserAgent.ParseAdd("Filio-App-UpdateChecker");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await Http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var release = JsonSerializer.Deserialize<GitHubReleaseInfo>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (release == null || release.Draft || release.Prerelease)
                return null;

            if (!TryParseTagVersion(release.TagName, out var remoteVersion))
                return null;

            if (remoteVersion <= CurrentVersion)
                return null;

            var releaseUrl = string.IsNullOrWhiteSpace(release.HtmlUrl)
                ? "https://github.com/ofirshudari1-ship-it/filio/releases"
                : release.HtmlUrl;

            var installerAsset = SelectInstallerAsset(release.Assets);

            return new GitHubUpdateResult(
                remoteVersion,
                releaseUrl,
                installerAsset?.BrowserDownloadUrl,
                installerAsset != null ? installerAsset.Size : null);
        }
        catch
        {
            // כשל רשת/JSON/DNS וכו' - בדיקת עדכונים היא נוחות, לא פונקציונליות קריטית,
            // ואסור שתזרוק החוצה ותפריע לשום קורא (במיוחד לא לרצף האתחול).
            return null;
        }
    }

    /// <summary>
    /// בוחרת את קובץ ההתקנה (Filio-Setup-X.Y.Z.exe) מבין ה-assets המצורפים ל-Release, אם
    /// יש כזה. מופרדת כמתודה סטטית טהורה (בלי רשת) כדי שניתן יהיה לבדוק אותה ביחידה - כולל
    /// המקרים "אין assets בכלל", "יש assets אבל בלי קובץ התקנה" ו"יש כמה קבצים ורק אחד
    /// תואם את השם/הסיומת הצפויים".
    /// </summary>
    public static GitHubReleaseAsset? SelectInstallerAsset(IEnumerable<GitHubReleaseAsset>? assets)
    {
        if (assets == null) return null;
        return assets.FirstOrDefault(a =>
            a != null &&
            !string.IsNullOrWhiteSpace(a.BrowserDownloadUrl) &&
            a.Name.StartsWith("Filio-Setup-", StringComparison.OrdinalIgnoreCase) &&
            a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// מנתח שם תג של GitHub Release (למשל "v2.9.1" או "2.9.1") לגרסה בת-השוואה. מופרד
    /// כמתודה סטטית טהורה כדי שניתן יהיה לבדוק אותה ביחידה בלי לגעת ברשת.
    /// </summary>
    public static bool TryParseTagVersion(string? tagName, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tagName))
            return false;

        var trimmed = tagName.Trim().TrimStart('v', 'V');
        return Version.TryParse(trimmed, out version!);
    }

    private static void EnsureHttps(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InsecureUpdateUrlException(url);
    }

    /// <param name="installerUrl">כתובת ההורדה הישירה (HTTPS) של קובץ ההתקנה.</param>
    /// <param name="expectedSha256">גיבוב sha256 מוצהר (זרימת ה-manifest הישנה) - אם קיים, מאומת מול הקובץ שהתקבל.</param>
    /// <param name="progress">התקדמות הורדה (0-100), אם ה-Content-Length ידוע.</param>
    /// <param name="expectedSizeBytes">גודל קובץ מוצהר (זרימת GitHub Releases - "size" ב-asset) - עוגן פשוט
    /// יותר מ-sha256 לוודא שההורדה הושלמה במלואה ולא נקטעה, גם ל-Release שלא מפרסם checksum ייעודי.</param>
    public async Task<string> DownloadInstallerAsync(
        string installerUrl,
        string? expectedSha256 = null,
        IProgress<double>? progress = null,
        long? expectedSizeBytes = null)
    {
        EnsureHttps(installerUrl);

        var tempPath = Path.Combine(Path.GetTempPath(), $"FilioUpdate_{Guid.NewGuid():N}.exe");

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

            // עוגן "הורדה הושלמה" בסיסי - לפני שבודקים sha256 (שלא תמיד קיים, למשל ב-GitHub
            // Releases שלא מפרסם checksum ייעודי לכל asset): קובץ שנקטע (רשת נפלה, החיבור
            // נסגר באמצע) הרבה פעמים עדיין "מצליח" מבחינת ה-HTTP status אבל מגיע קטן מהמדווח.
            if (expectedSizeBytes.HasValue)
            {
                var actualSize = new FileInfo(tempPath).Length;
                if (actualSize != expectedSizeBytes.Value)
                {
                    TryDelete(tempPath);
                    throw new UpdateIntegrityException(
                        $"downloaded size {actualSize} bytes does not match expected {expectedSizeBytes.Value} bytes");
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
    /// מריץ את קובץ ההתקנה בשקט (ללא אשף) ובלי לבקש הפעלה מחדש של Windows. Filio-Setup.exe
    /// (build\installer\Setup.cs) מזהה את הדגלים האלה בעצמו (Program.Main / RunSilentInstall) -
    /// זה לא Inno Setup, אבל השמות תואמים במכוון למוסכמת Inno Setup (VERYSILENT וכו') כי אלה
    /// כבר מוכרים לכל מי שמריץ עדכון שקט מסקריפט. /CLOSEAPPLICATIONS גורם להתקנה לנסות לסגור
    /// Filio הרץ בעצמה (במקום להיכשל על קובץ נעול), ו-/RESTARTAPPLICATIONS מפעיל אותה מחדש
    /// בסיום מוצלח. קוראים לפעולה הזו ואז יוצאים מהתוכנה הנוכחית - היא זו שמחזיקה את הנעילה
    /// על Filio.exe, אז ההתקנה לא יכולה לדרוס אותו כל עוד התהליך הנוכחי עדיין חי.
    ///
    /// חשוב: /LANG= חובה גם ב-VERYSILENT - בלעדיו ההתקנה השקטה הייתה יכולה ליפול חזרה
    /// לברירת מחדל שגויה. תוקן אחרי שנתפס בבדיקה בפועל של ההתקנה.
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
