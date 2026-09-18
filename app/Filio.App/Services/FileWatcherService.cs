using System.IO;
using Filio.Models;

namespace Filio.Services;

/// <summary>
/// עוקב אחרי כל תיקיות המעקב המוגדרות (לא רק אחת - מגבלה נפוצה בכלים מתחרים), ממתין
/// שקובץ חדש יסיים להיכתב לגמרי (כולל קבצי OneDrive שעדיין לא ירדו בפועל), ואז מפעיל את
/// מנוע הסיווג והתיוק עליו.
///
/// כולל בדיקת-בריאות תקופתית ואוטו-החלמה מכשלים: כלים כמו DropIt ידועים כ"מפסיקים לעבוד"
/// בשקט אחרי שינה/הפעלה מחדש/גלישת buffer פנימית של FileSystemWatcher, בלי שהמשתמש ידע.
/// </summary>
public class FileWatcherService : IDisposable
{
    private static readonly string[] IgnoredExtensions = { ".crdownload", ".part", ".tmp", ".download" };
    private static readonly string[] InstallerExtensions = { ".exe", ".msi" };
    private const int HealthCheckIntervalMs = 30_000;

    private readonly FileOrganizerService _organizer;
    private readonly HashSet<string> _pathsInProgress = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _inProgressLock = new();
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _watchersLock = new();
    private Timer? _healthCheckTimer;
    private AppSettings _settings;

    // כל Stop()/Start() מגדיל את המחזור. משימת רקע שמנסה תיקייה איטית עדיין יכולה להשלים
    // אחרי שמחזור חדש כבר התחיל (למשל המשתמש שינה הגדרות פעמיים ברצף) - בלי בדיקת המחזור
    // הזו, ה-watcher "היתום" הזה עלול להוסיף את עצמו למצב הנוכחי אחרי ש-Stop() כבר ניקה הכל.
    private int _startGeneration;

    public event Action<FileLogEntry>? FileProcessed;

    public FileWatcherService(AppSettings settings) : this(settings, new FileOrganizerService())
    {
    }

    /// <summary>מאפשר להזריק FileOrganizerService (ולכן גם DuplicateDetectionService עם אינדקס
    /// מבודד) - בעיקר לבדיקות, כדי לא לזהם את אינדקס הכפילויות האמיתי ב-%AppData%.</summary>
    public FileWatcherService(AppSettings settings, FileOrganizerService organizer)
    {
        _settings = settings;
        _organizer = organizer;
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        Restart();
    }

    private static readonly TimeSpan PerFolderStartupTimeout = TimeSpan.FromSeconds(3);

    public void Start()
    {
        Stop();
        var generation = _startGeneration;

        // כל תיקייה מקבלת עד 3 שניות: Directory.Exists/FileSystemWatcher על נתיב רשת/ענן לא
        // זמין יכולים להיתקע לעשרות שניות (התנהגות Windows ידועה) ו"לתקוע" את כל האפליקציה
        // אם זה רץ ישירות על thread הממשק (OnStartup / לחיצת "שמירה") - זו הייתה תקלת הקיפאון
        // האמיתית שדווחה. חשוב: כל התיקיות מותנעות *במקביל* (לא אחת-אחרי-השנייה) - אחרת
        // המתנה של עד 3 שניות לכל תיקייה הופכת למכפלה (4 תיקיות איטיות = עד 12 שניות קיפאון
        // במקום 3). Task.WaitAll עם timeout חוסם עד 3 שניות סה"כ, לא לכל תיקייה בנפרד.
        var startTasks = DistinctFolders()
            .Select(folder => Task.Run(() => TryStartWatcher(folder, generation)))
            .ToArray();

        try
        {
            Task.WaitAll(startTasks, PerFolderStartupTimeout);
        }
        catch
        {
            // תיקייה לא זמינה/שגיאה - בדיקת הבריאות התקופתית תנסה שוב מאוחר יותר
        }

        _healthCheckTimer = new Timer(_ => EnsureAllWatchersHealthy(), null, HealthCheckIntervalMs, HealthCheckIntervalMs);
    }

    public void Stop()
    {
        _healthCheckTimer?.Dispose();
        _healthCheckTimer = null;

        lock (_watchersLock)
        {
            _startGeneration++;
            foreach (var watcher in _watchers.Values)
                DisposeWatcher(watcher);
            _watchers.Clear();
        }
    }

    private void Restart() => Start();

    private IEnumerable<string> DistinctFolders() =>
        _settings.WatchFolders.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase);

    private void TryStartWatcher(string folder) => TryStartWatcher(folder, _startGeneration);

    private void TryStartWatcher(string folder, int generation)
    {
        if (!Directory.Exists(folder))
            return;

        FileSystemWatcher watcher;
        try
        {
            watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
            };
        }
        catch (IOException)
        {
            return; // תיקייה לא נגישה כרגע (למשל כונן רשת מנותק) - בדיקת הבריאות הבאה תנסה שוב
        }

        lock (_watchersLock)
        {
            // המחזור התקדם (Stop/Start נוסף רץ) בזמן שחיכינו כאן לתיקייה איטית - ה-watcher
            // הזה שייך למצב ישן, משליכים אותו במקום להכניס אותו למצב הנוכחי.
            if (generation != _startGeneration || _watchers.ContainsKey(folder))
            {
                DisposeWatcher(watcher);
                return;
            }

            watcher.Created += OnFileCreated;
            watcher.Renamed += OnFileRenamed;
            watcher.Error += OnWatcherError;
            watcher.EnableRaisingEvents = true;

            _watchers[folder] = watcher;
        }
    }

    /// <summary>
    /// בדיקת בריאות תקופתית (וגם קריאה ישירה לצורך בדיקות אוטומטיות): מנקה watcher-ים
    /// לתיקיות שכבר לא מוגדרות, ומפעיל מחדש watcher-ים לתיקיות שנעלמו וחזרו או שהפסיקו
    /// לדווח אירועים.
    /// </summary>
    public void EnsureAllWatchersHealthy()
    {
        var configuredFolders = DistinctFolders().ToList();
        var foldersNeedingRestart = new List<string>();

        lock (_watchersLock)
        {
            foreach (var stale in _watchers.Keys.Where(f => !configuredFolders.Contains(f, StringComparer.OrdinalIgnoreCase)).ToList())
            {
                DisposeWatcher(_watchers[stale]);
                _watchers.Remove(stale);
            }

            foreach (var folder in configuredFolders)
            {
                var isHealthy = _watchers.TryGetValue(folder, out var watcher)
                                 && watcher.EnableRaisingEvents
                                 && Directory.Exists(folder);

                if (isHealthy) continue;

                if (_watchers.TryGetValue(folder, out var unhealthy))
                {
                    DisposeWatcher(unhealthy);
                    _watchers.Remove(folder);
                }

                foldersNeedingRestart.Add(folder);
            }
        }

        // רק תיקיות שבאמת לא היו תקינות - לא כל התיקיות המוגדרות. בלי זה, כל 30 שניות
        // נבנה ונזרוק FileSystemWatcher חדש גם לתיקיות שכבר עובדות מצוין, בזבוז מיותר של
        // handle-ים ילידים על כל בדיקת בריאות.
        foreach (var folder in foldersNeedingRestart)
            TryStartWatcher(folder);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        // FileSystemWatcher מפסיק לדווח אירועים בשקט אחרי גלישת buffer פנימית (הרבה שינויים
        // בבת אחת) - זו בדיוק התקלה המתועדת שגורמת לכלים מתחרים "להפסיק לעבוד" בלי שהמשתמש
        // ישים לב. בונים מיד מחדש במקום להשאיר חור במעקב.
        if (sender is FileSystemWatcher watcher)
        {
            lock (_watchersLock)
            {
                var folder = _watchers.FirstOrDefault(kv => ReferenceEquals(kv.Value, watcher)).Key;
                if (folder != null)
                {
                    DisposeWatcher(watcher);
                    _watchers.Remove(folder);
                }
            }
        }

        EnsureAllWatchersHealthy();
    }

    private void DisposeWatcher(FileSystemWatcher watcher)
    {
        try
        {
            watcher.EnableRaisingEvents = false;
            watcher.Created -= OnFileCreated;
            watcher.Renamed -= OnFileRenamed;
            watcher.Error -= OnWatcherError;
            watcher.Dispose();
        }
        catch
        {
            // ניקוי best-effort - לא קריטי אם נכשל
        }
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e) => _ = TryProcessFileAsync(e.FullPath);

    private void OnFileCreated(object sender, FileSystemEventArgs e) => _ = TryProcessFileAsync(e.FullPath);

    /// <summary>מריץ מחדש את אותו pipeline על קובץ קיים - למשל מכפתור "נסה שוב" ביומן הפעילות
    /// על רשומה שנכשלה (קובץ שהיה נעול, למשל).</summary>
    public Task RetryFileAsync(string path) => TryProcessFileAsync(path);

    private async Task TryProcessFileAsync(string path)
    {
        if (_settings.IsPaused)
            return;

        // תיקיות (למשל "לקוח/2026/חשבוניות" שנוצרת ע"י תיוק קודם) גם מעוררות אירוע Created -
        // אנחנו מתייקים קבצים בלבד.
        if (Directory.Exists(path))
            return;

        var ext = Path.GetExtension(path);
        if (IgnoredExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return;

        // רק סוגי קבצים שמוגדרים כ"נצפים" מטופלים - בלי זה, כל קובץ שנוחת בתיקייה (תמונות,
        // ZIP, HTML, מסמכי עבודה כלליים וכו') היה מועבר ל"לא מזוהה" בלי שהמשתמש התכוון לזה.
        if (!IsWatchedExtension(ext))
            return;

        // שליטה עדינה נוספת: קובץ ספציפי/תבנית ששם שלו תמיד ידלג, גם אם הסיומת נצפית.
        if (MatchesIgnorePattern(Path.GetFileName(path)))
            return;

        // מונע טיפול כפול באותו קובץ אם המערכת יורה כמה אירועים רצופים עליו (Created + Renamed וכו')
        lock (_inProgressLock)
        {
            if (!_pathsInProgress.Add(path))
                return;
        }

        try
        {
            var waitResult = await WaitUntilFileIsReadyAsync(path);

            if (waitResult == FileReadyResult.Vanished)
                return; // הקובץ נעלם/הוזז ע"י תהליך אחר בזמן ההמתנה - אין מה לתייק

            if (waitResult == FileReadyResult.TimedOut)
            {
                FileProcessed?.Invoke(new FileLogEntry
                {
                    OriginalPath = path,
                    Success = false,
                    ErrorMessage = "הקובץ נשאר נעול (בשימוש ע\"י תוכנה אחרת) במשך 30 שניות ולא תויק"
                });
                return;
            }

            if (_settings.EnableDefenderScan)
            {
                // סריקה חד-פעמית מול Windows Defender - לא מנוע אבטחה תוצרת-בית, רק שאלה
                // למה שכבר מותקן וחינמי בכל Windows. קובץ שמזוהה כאיום נשאר בדיוק במקומו -
                // לא זז, לא נמחק, רק מדווח ביומן הפעילות כדי שהמשתמש יידע ויטפל בו בעצמו.
                var scanResult = await Task.Run(() => WindowsDefenderScanService.ScanFile(path));
                if (scanResult == DefenderScanResult.ThreatFound)
                {
                    var threatEntry = new FileLogEntry
                    {
                        OriginalPath = path,
                        Success = false,
                        ThreatDetected = true,
                        ErrorMessage = LocalizationService.Get("DefenderThreatDetected")
                    };
                    FileOrganizerService.LogExternalEntry(threatEntry);
                    FileProcessed?.Invoke(threatEntry);
                    return;
                }
            }

            FileLogEntry entry;

            if (_settings.OrganizeInstallers && InstallerExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                // קובצי התקנה לא עוברים דרך מנוע הסיווג של מסמכים בכלל - אין להם "לקוח" או
                // "סוג מסמך" שיש טעם לנחש, הם פשוט מקבלים תיקייה משלהם.
                entry = _organizer.OrganizeInstaller(path, _settings);
            }
            else
            {
                var fileName = Path.GetFileName(path);
                var content = ExtractContent(path);
                var createdUtc = File.GetCreationTimeUtc(path);

                var classification = DocumentClassifier.Classify(fileName, content, _settings.DocTypeRules, _settings.Clients, createdUtc);

                // "בדיקה לפני תיוק" (הגדרה אופציונלית, בהשראת Hazel): כשהסיווג לא זיהה בכלל
                // לקוח מוכר (הביטחון הכי נמוך שיש - לא רק "סוג לא מזוהה", גם "לקוח לא מזוהה"),
                // לא נוגעים בקובץ בכלל - הוא נשאר במקום ומחכה שהמשתמש יבחר לקוח/סוג ידנית
                // מיומן הפעילות. כשההגדרה כבויה (ברירת המחדל), ההתנהגות הישנה נשארת זהה.
                if (_settings.ReviewLowConfidenceMatches && classification.ClientName == "Unknown Client")
                {
                    var reviewEntry = new FileLogEntry
                    {
                        OriginalPath = path,
                        DetectedType = classification.DocType,
                        DetectedClient = classification.ClientName,
                        DetectedDate = classification.DocumentDate.ToString("yyyy-MM-dd"),
                        MatchExplanation = classification.MatchExplanation,
                        NeedsReview = true,
                        Success = false
                    };
                    FileOrganizerService.LogExternalEntry(reviewEntry);
                    FileProcessed?.Invoke(reviewEntry);
                    return;
                }

                entry = _organizer.Organize(path, classification, _settings);
            }

            if (entry.Success)
                DiagnosticLogger.Info($"Filed: {path} -> {entry.NewPath}");
            else
                DiagnosticLogger.Warn($"Failed to file: {path} — {entry.ErrorMessage}");

            FileProcessed?.Invoke(entry);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Error($"Exception while processing file: {path}", ex);
            FileProcessed?.Invoke(new FileLogEntry
            {
                OriginalPath = path,
                Success = false,
                ErrorMessage = ex.Message
            });
        }
        finally
        {
            lock (_inProgressLock)
                _pathsInProgress.Remove(path);
        }
    }

    /// <summary>בוחר את שיטת חילוץ הטקסט המתאימה לפי סוג הקובץ - PDF, תמונה (OCR מקומי,
    /// אם מופעל), או כלום (מסמכי Office עדיין מסווגים לפי שם הקובץ בלבד בגרסה הזו).</summary>
    private string ExtractContent(string path)
    {
        if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return PdfTextService.ExtractText(path);

        if (_settings.EnableImageOcr && ImageTextService.IsSupportedImage(path))
            return ImageTextService.ExtractText(path);

        return string.Empty;
    }

    private bool IsWatchedExtension(string ext)
    {
        if (string.IsNullOrEmpty(ext))
            return false;

        // קובצי התקנה נשקלים רק אם המשתמש הפעיל את זה במפורש - לא ברירת מחדל, כי בניגוד
        // למסמכים ותמונות, הרצת התקנה מיד אחרי ההורדה היא שימוש לגיטימי ונפוץ.
        if (_settings.OrganizeInstallers && InstallerExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return true;

        var watched = (_settings.WatchedFileExtensions ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return watched.Any(w => string.Equals(
            w.StartsWith('.') ? w : "." + w, ext, StringComparison.OrdinalIgnoreCase));
    }

    private bool MatchesIgnorePattern(string fileName)
    {
        var patterns = (_settings.IgnoredFileNamePatterns ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return patterns.Any(p => fileName.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    private enum FileReadyResult { Ready, Vanished, TimedOut }

    /// <summary>
    /// ממתין עד שהקובץ אינו נעול לכתיבה (כלומר ההורדה/השמירה הסתיימה) ואינו placeholder של
    /// ענן שעדיין לא ירד בפועל, עד 30 שניות.
    /// </summary>
    private static async Task<FileReadyResult> WaitUntilFileIsReadyAsync(string path)
    {
        const int maxAttempts = 30;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            await Task.Delay(1000);

            try
            {
                if (!File.Exists(path))
                    return FileReadyResult.Vanished;

                // קובץ OneDrive Files On-Demand שעדיין לא ירד בפועל - ממתינים שירד במקום
                // לגעת בו (מגע עלול לאלץ הורדה מיותרת או להתנגש עם לקוח הסנכרון)
                if (CloudSyncDetector.IsCloudPlaceholder(path))
                    continue;

                using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
                return FileReadyResult.Ready;
            }
            catch (IOException)
            {
                // עדיין נעול על ידי תהליך ההורדה - ננסה שוב
            }
            catch
            {
                return FileReadyResult.Vanished;
            }
        }

        return FileReadyResult.TimedOut;
    }

    public void Dispose() => Stop();
}
