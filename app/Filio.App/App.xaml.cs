using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Filio.Models;
using Filio.Services;

namespace Filio;

public partial class App : Application
{
    // Matches AppMutex in installer\setup.iss - Inno Setup checks this same name before
    // install/update so it can prompt to close a running instance instead of failing to
    // overwrite a locked file. Global\ makes it visible across user sessions (RDP/services),
    // matching what a system-wide file-watcher app needs.
    private const string SingleInstanceMutexName = @"Global\Filio-SingleInstance-7C2A9E1D";
    private Mutex? _singleInstanceMutex;

    private readonly SettingsService _settingsService = new();
    private readonly UpdateService _updateService = new();
    private FileWatcherService? _watcherService;
    private TaskbarIcon? _trayIcon;
    private System.Windows.Controls.MenuItem? _openSettingsMenuItem;
    private System.Windows.Controls.MenuItem? _pauseMenuItem;
    private System.Windows.Controls.MenuItem? _openFolderMenuItem;
    private System.Windows.Controls.MenuItem? _undoLastMenuItem;
    private System.Windows.Controls.MenuItem? _checkUpdatesMenuItem;
    private System.Windows.Controls.MenuItem? _exitMenuItem;
    private AppSettings _settings = null!;
    private MainWindow? _mainWindow;
    private string? _lastFiledPath;

    // עמוד ה-GitHub Release הציבורי שהתגלה בבדיקת העדכון האחרונה ברקע (אם יש). לחיצה על
    // הבועית הזו פותחת אותו בדפדפן; כל עוד null, לחיצה על בועית מתנהגת כרגיל (פתיחת הקובץ
    // שתויק לאחרונה).
    private string? _pendingGitHubReleaseUrl;
    private bool _gitHubUpdateCheckedThisSession;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            // A second launch: another Filio is already watching Downloads. Running two
            // at once would double-process every new file (race on the same folder) and
            // show two tray icons - just exit instead of doing anything else.
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        DiagnosticLogger.PruneOldLogs();
        DiagnosticLogger.Info("Filio starting up");

        var splash = new SplashWindow();
        // Reads the real assembly version (baked in at publish time via -p:Version=$version
        // in build.ps1, sourced from version.json) instead of a literal here - a hardcoded
        // string here would silently drift out of sync on every version bump.
        splash.SetVersion(UpdateService.CurrentVersion.ToString(3));
        splash.Show();

        // Settings (and with them, the language) must load before the first status line is
        // shown - otherwise the splash text would flash in the wrong language, or fall back to
        // showing the raw resource key, before LocalizationService has a dictionary merged in.
        _settings = _settingsService.Load();
        LocalizationService.SetLanguage(_settings.Language);
        splash.SetStatus(LocalizationService.Get("SplashLoadingSettings"), 20);
        ApplyTheme(_settings.Theme);

        // Detect system theme on first run if user hasn't set a preference
        // (Theme defaults to Light, so we only auto-detect if it's still the default)
        if (_settings.Theme == AppTheme.Light)
        {
            var systemTheme = GetSystemTheme();
            if (systemTheme == AppTheme.Dark)
            {
                ApplyTheme(AppTheme.Dark);
                // Don't persist — only apply visually; user can override via the toggle
            }
        }

        // Listen for system theme changes while app is running
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnSystemThemeChanged;

        Directory.CreateDirectory(_settings.RootOutputFolder);

        // מסנכרן את רישום ההפעלה האוטומטית בווינדוס עם ההגדרה השמורה, בכל הפעלה - לא רק
        // כשלוחצים "שמירה" בהגדרות. בלי זה, ההגדרה יכולה "לדרוך מים": settings.json אומר
        // StartWithWindows=true, אבל ערך הרישום עצמו נמחק/אף פעם לא נכתב (למשל אחרי הסרת
        // התקנה + הפעלה ידנית של קובץ ה-exe בלי לעבור דרך ההתקנה מחדש) - והמשתמש רואה תיבת
        // סימון מסומנת שלא משקפת את מה שבאמת יקרה באתחול הבא. נתפס בשימוש אמיתי.
        StartupService.SetEnabled(_settings.StartWithWindows);

        splash.SetStatus(LocalizationService.Get("SplashStartingWatcher"), 60);

        _watcherService = new FileWatcherService(_settings);
        _watcherService.FileProcessed += OnFileProcessed;
        _watcherService.Start();

        PruneOldLogEntries(_settings.LogRetentionDays);

        SetupTrayIcon();

        splash.SetStatus(LocalizationService.Get("SplashReady"), 100);
        await Task.Delay(800); // minimum visible time
        splash.Close();

        if (!_settings.HasCompletedOnboarding)
        {
            var onboarding = new OnboardingWindow(_settingsService, _settings, _watcherService);
            onboarding.ShowDialog();
            RefreshTrayTexts(notifyPauseChange: false); // האשף עשוי לשנות שפה
        }

        var startMinimized = e.Args.Contains("--minimized");
        if (!startMinimized)
            ShowMainWindow();

        if (_settings.AutoCheckForUpdates && !string.IsNullOrWhiteSpace(_settings.UpdateFeedUrl))
            _ = CheckForUpdatesInBackgroundAsync();

        // בדיקת עדכונים מול GitHub Releases (המקור הרשמי כיום) - לא תלויה ב-UpdateFeedUrl
        // (שנועד לזרימת ה-manifest/silent-install הישנה יותר). רצה פעם אחת בלבד לכל הפעלה,
        // כמה שניות אחרי העלייה כדי לא להתחרות על רוחב פס/CPU עם אתחול חלון הראשי.
        if (_settings.AutoCheckForUpdates)
            _ = CheckGitHubUpdatesInBackgroundAsync();
    }

    private static AppTheme GetSystemTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            if (value is int intValue && intValue == 0)
                return AppTheme.Dark;
        }
        catch { }
        return AppTheme.Light;
    }

    private void OnSystemThemeChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category != Microsoft.Win32.UserPreferenceCategory.General) return;
        // Only auto-follow system if user hasn't pinned a preference in settings
        // (We detect this by checking if their saved setting matches what we'd auto-detect)
        var systemTheme = GetSystemTheme();
        if (_settings.Theme != systemTheme)
        {
            Dispatcher.Invoke(() => ApplyTheme(systemTheme));
        }
    }

    private static void PruneOldLogEntries(int retentionDays)
    {
        if (retentionDays <= 0) return; // 0 = keep forever
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
            var all = FileOrganizerService.ReadLog();
            var pruned = all.Where(e =>
            {
                if (string.IsNullOrEmpty(e.DetectedDate)) return true;
                return DateTime.TryParse(e.DetectedDate, out var d) && d >= cutoff;
            }).ToList();
            if (pruned.Count < all.Count)
            {
                var logPath = SettingsService.LogPath;
                File.WriteAllText(logPath, System.Text.Json.JsonSerializer.Serialize(
                    pruned, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch { }
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = LocalizationService.Get("TrayTooltip"),
            Icon = GetTrayIcon(_settings.IsPaused)
        };

        var menu = new System.Windows.Controls.ContextMenu();

        _openSettingsMenuItem = new System.Windows.Controls.MenuItem();
        _openSettingsMenuItem.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(_openSettingsMenuItem);

        _pauseMenuItem = new System.Windows.Controls.MenuItem();
        _pauseMenuItem.Click += (_, _) =>
        {
            _settings.IsPaused = !_settings.IsPaused;
            _settingsService.Save(_settings);
            _watcherService?.UpdateSettings(_settings);
            RefreshTrayTexts(notifyPauseChange: true);
            _mainWindow?.SyncPausedStateFromOutside(_settings.IsPaused);
        };
        menu.Items.Add(_pauseMenuItem);

        _openFolderMenuItem = new System.Windows.Controls.MenuItem();
        _openFolderMenuItem.Click += (_, _) =>
        {
            Directory.CreateDirectory(_settings.RootOutputFolder);
            System.Diagnostics.Process.Start("explorer.exe", _settings.RootOutputFolder);
        };
        menu.Items.Add(_openFolderMenuItem);

        // גישה מהירה לביטול התיוק האחרון בלי לפתוח את החלון הראשי ולנווט לטאב "פעילות" -
        // שימושי כשמסתכלים על Downloads ורואים שקובץ שזה עתה תויק הלך למקום הלא נכון.
        _undoLastMenuItem = new System.Windows.Controls.MenuItem();
        _undoLastMenuItem.Click += (_, _) => UndoLastFiledFile();
        menu.Items.Add(_undoLastMenuItem);

        _checkUpdatesMenuItem = new System.Windows.Controls.MenuItem();
        _checkUpdatesMenuItem.Click += (_, _) =>
        {
            ShowMainWindow();
            _mainWindow!.MainTabControlSelectUpdatesTab();
            _ = _mainWindow.RunUpdateCheckAsync(_settings.UpdateFeedUrl);
        };
        menu.Items.Add(_checkUpdatesMenuItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        _exitMenuItem = new System.Windows.Controls.MenuItem();
        _exitMenuItem.Click += (_, _) => ExitApplication();
        menu.Items.Add(_exitMenuItem);

        _trayIcon.ContextMenu = menu;
        _trayIcon.TrayMouseDoubleClick += (_, _) => ShowMainWindow();

        // לחיצה על ההתראה עצמה פותחת את הסייר עם הקובץ שתויק מודגש - נוחות קטנה שחוסכת
        // חיפוש ידני אחרי איפה בדיוק הקובץ נחת.
        _trayIcon.TrayBalloonTipClicked += (_, _) =>
        {
            if (_pendingGitHubReleaseUrl != null)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_pendingGitHubReleaseUrl)
                    {
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    DiagnosticLogger.Warn($"Failed to open release page: {ex.Message}");
                }
                return;
            }

            if (_lastFiledPath != null && File.Exists(_lastFiledPath))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_lastFiledPath}\"");
        };

        RefreshTrayTexts(notifyPauseChange: false);

        // התיקון יוצר את אייקון המגש בלי להיות מוצהר ב-XAML/עץ ויזואלי - חובה לקרוא ל-ForceCreate
        _trayIcon.ForceCreate();
    }

    /// <summary>מעדכן את כל טקסטי תפריט המגש לפי השפה/מצב ההשהיה הנוכחיים.</summary>
    private void RefreshTrayTexts(bool notifyPauseChange)
    {
        if (_trayIcon != null)
        {
            _trayIcon.Icon = GetTrayIcon(_settings.IsPaused);
            _trayIcon.ToolTipText = LocalizationService.Get("TrayTooltip");
        }

        if (_openSettingsMenuItem != null) _openSettingsMenuItem.Header = LocalizationService.Get("TrayOpenSettings");
        if (_pauseMenuItem != null) _pauseMenuItem.Header = LocalizationService.Get(_settings.IsPaused ? "TrayResume" : "TrayPause");
        if (_openFolderMenuItem != null) _openFolderMenuItem.Header = LocalizationService.Get("TrayOpenOutputFolder");
        if (_undoLastMenuItem != null) _undoLastMenuItem.Header = LocalizationService.Get("TrayUndoLast");
        if (_checkUpdatesMenuItem != null) _checkUpdatesMenuItem.Header = LocalizationService.Get("TrayCheckForUpdates");
        if (_exitMenuItem != null) _exitMenuItem.Header = LocalizationService.Get("TrayExit");

        if (notifyPauseChange)
        {
            _trayIcon?.ShowNotification(
                LocalizationService.Get("DialogTitle"),
                LocalizationService.Get(_settings.IsPaused ? "NotificationPausedTitle" : "NotificationResumedTitle"),
                NotificationIcon.Info);
        }
    }

    private static System.Drawing.Icon LoadAppIcon()
    {
        // Environment.ProcessPath מחזיר את נתיב ה-exe גם כשמדובר בפרסום single-file
        // (בניגוד ל-Assembly.Location שיוצא ריק במצב הזה)
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
            {
                var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon != null) return icon;
            }
        }
        catch
        {
            // ממשיכים לאייקון ברירת מחדל של המערכת
        }

        return System.Drawing.SystemIcons.Application;
    }

    private static System.Drawing.Icon? _activeIconCache;
    private static System.Drawing.Icon? _pausedIconCache;

    /// <summary>מחזיר את אייקון המגש המתאים למצב הנוכחי - הרגיל, או גרסה מעומעמת/מנוכה-רוויה
    /// כשהתיוק מושהה, כך שהעצירה ניכרת גם באייקון עצמו ולא רק בטולטיפ (STANDARDS.md 12.2).</summary>
    private static System.Drawing.Icon GetTrayIcon(bool isPaused)
    {
        _activeIconCache ??= LoadAppIcon();
        if (!isPaused) return _activeIconCache;

        if (_pausedIconCache != null) return _pausedIconCache;

        try
        {
            using var bmp = _activeIconCache.ToBitmap();
            using var dimmed = new System.Drawing.Bitmap(bmp.Width, bmp.Height);
            using (var g = System.Drawing.Graphics.FromImage(dimmed))
            {
                // מטריצת צבע שמנמיכה רוויה+בהירות - נותן מראה "מושהה"/אפרפר בלי לבנות אייקון נפרד מאפס
                var matrix = new System.Drawing.Imaging.ColorMatrix(new float[][]
                {
                    new float[] {0.35f, 0.35f, 0.35f, 0, 0},
                    new float[] {0.35f, 0.35f, 0.35f, 0, 0},
                    new float[] {0.35f, 0.35f, 0.35f, 0, 0},
                    new float[] {0, 0, 0, 0.75f, 0},
                    new float[] {0, 0, 0, 0, 1}
                });
                using var attrs = new System.Drawing.Imaging.ImageAttributes();
                attrs.SetColorMatrix(matrix);
                g.DrawImage(bmp, new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height),
                    0, 0, bmp.Width, bmp.Height, System.Drawing.GraphicsUnit.Pixel, attrs);
            }
            var hIcon = dimmed.GetHicon();
            _pausedIconCache = System.Drawing.Icon.FromHandle(hIcon);
            return _pausedIconCache;
        }
        catch
        {
            return _activeIconCache; // best-effort: אם היצירה נכשלת, נשארים עם האייקון הרגיל
        }
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null)
        {
            _mainWindow = new MainWindow(_settingsService, _settings, _watcherService!);
            _mainWindow.Closed += (_, _) => _mainWindow = null;
            _mainWindow.PauseStateChanged += _ => RefreshTrayTexts(notifyPauseChange: false);
            _mainWindow.LanguageChanged += () => RefreshTrayTexts(notifyPauseChange: false);
            _mainWindow.ExitRequestedForUpdate += ExitApplication;
            _mainWindow.FirstTrayMinimize += () =>
            {
                _trayIcon?.ShowNotification(
                    LocalizationService.Get("DialogTitle"),
                    LocalizationService.Get("TrayMinimizeHintMessage"),
                    NotificationIcon.Info);
            };
        }

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    /// <summary>מבטל את הקובץ האחרון שתויק בהצלחה, ישירות מתפריט המגש - בלי לפתוח את החלון
    /// הראשי ולנווט לטאב "פעילות" קודם. מחפש מהסוף ליומן את הרשומה המוצלחת האחרונה, כי
    /// רשומות כישלון (Success=false) אין להן מה לבטל (הקובץ מעולם לא זז).</summary>
    private void UndoLastFiledFile()
    {
        var lastSuccess = FileOrganizerService.ReadLog()
            .LastOrDefault(entry => entry.Success && !string.IsNullOrEmpty(entry.NewPath));

        if (lastSuccess == null)
        {
            _trayIcon?.ShowNotification(
                LocalizationService.Get("DialogTitle"),
                LocalizationService.Get("UndoLastNoneMessage"),
                NotificationIcon.Info);
            return;
        }

        var organizer = new FileOrganizerService();
        var success = organizer.Undo(lastSuccess);
        _trayIcon?.ShowNotification(
            LocalizationService.Get("DialogTitle"),
            LocalizationService.Get(success ? "UndoSuccessMessage" : "UndoFailureMessage"),
            success ? NotificationIcon.Info : NotificationIcon.Warning);

        if (success)
            _mainWindow?.RefreshLogGridPublic();
    }

    private async Task CheckForUpdatesInBackgroundAsync()
    {
        try
        {
            var manifest = await _updateService.CheckForUpdateAsync(_settings.UpdateFeedUrl);
            if (manifest != null)
            {
                _trayIcon?.ShowNotification(
                    LocalizationService.Get("DialogTitle"),
                    LocalizationService.Format("UpdateAvailableStatus", manifest.Version),
                    NotificationIcon.Info);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Warn($"Update check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// בדיקת עדכון "רגילה" מול GitHub Releases (ראו UpdateService.CheckGitHubReleaseAsync):
    /// לא מורידה/מתקינה כלום - רק מציגה התראה במגש עם קישור לעמוד ה-Release, שהמשתמש
    /// לוחץ עליו כדי להמשיך בדפדפן. נכשלת בשקט תמיד (השירות עצמו כבר בולע חריגות, וזה
    /// כאן ליתר ביטחון) ולא חוסמת שום דבר באתחול, כי היא רצה fire-and-forget.
    /// </summary>
    private async Task CheckGitHubUpdatesInBackgroundAsync()
    {
        if (_gitHubUpdateCheckedThisSession)
            return;
        _gitHubUpdateCheckedThisSession = true;

        try
        {
            // השהייה קצרה כדי לא להתחרות עם עליית חלון האתחול/הצגת החלון הראשי על
            // רוחב פס ו-CPU, ולא כדי לעכב את אף שלב באתחול (זה רץ בלי await מהקורא).
            await Task.Delay(TimeSpan.FromSeconds(5));

            if (!_settings.AutoCheckForUpdates)
                return;

            var result = await _updateService.CheckGitHubReleaseAsync();
            if (result != null)
            {
                _pendingGitHubReleaseUrl = result.ReleaseUrl;
                _trayIcon?.ShowNotification(
                    LocalizationService.Get("DialogTitle"),
                    LocalizationService.Format("UpdateAvailableStatus", result.LatestVersion.ToString(3)),
                    NotificationIcon.Info);
            }
        }
        catch (Exception ex)
        {
            // best-effort בלבד - בדיקת עדכונים אף פעם לא אמורה להשפיע על שאר האפליקציה.
            DiagnosticLogger.Warn($"GitHub update check failed: {ex.Message}");
        }
    }

    private void OnFileProcessed(FileLogEntry entry)
    {
        Dispatcher.Invoke(() =>
        {
            if (!_settings.ShowNotifications || _trayIcon == null)
                return;

            // "רק בכישלון" מדלג על הבועית עבור קבצים שתויקו בהצלחה, אבל לעולם לא על כשלים -
            // המשתמש שבחר לצמצם רעש עדיין צריך לדעת כשמשהו דורש תשומת לב ממנו.
            if (_settings.NotifyOnlyOnFailure && entry.Success)
            {
                _lastFiledPath = entry.NewPath;
                _mainWindow?.NotifyNewLogEntry(entry);
                return;
            }

            var message = entry.Success
                ? LocalizationService.Format("NotificationFiledTypeClientFormat", entry.DetectedType, entry.DetectedClient)
                : LocalizationService.Format("NotificationFailedFormat", Path.GetFileName(entry.OriginalPath));

            if (entry.Success)
                _lastFiledPath = entry.NewPath;

            _trayIcon.ShowNotification(Path.GetFileName(entry.OriginalPath), message, entry.Success ? NotificationIcon.Info : NotificationIcon.Error);
            _mainWindow?.NotifyNewLogEntry(entry);
        });
    }

    public static void ApplyTheme(AppTheme theme)
    {
        var res = Application.Current.Resources;
        if (theme == AppTheme.Dark)
        {
            res["BrandBackgroundBrush"] = res["Dark_BrandBackgroundBrush"];
            res["SurfaceBrush"] = res["Dark_SurfaceBrush"];
            res["SurfaceAltBrush"] = res["Dark_SurfaceAltBrush"];
            res["BorderBrush2"] = res["Dark_BorderBrush2"];
            res["TextMutedBrush"] = res["Dark_TextMutedBrush"];
            res["NeutralButtonBrush"] = res["Dark_NeutralButtonBrush"];
            res["NeutralButtonForegroundBrush"] = res["Dark_NeutralButtonForegroundBrush"];
        }
        else
        {
            res["BrandBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xF7, 0xF9, 0xF8));
            res["SurfaceBrush"] = new SolidColorBrush(Colors.White);
            res["SurfaceAltBrush"] = new SolidColorBrush(Color.FromRgb(0xF2, 0xF6, 0xF4));
            res["BorderBrush2"] = new SolidColorBrush(Color.FromRgb(0xDC, 0xE5, 0xE2));
            res["TextMutedBrush"] = new SolidColorBrush(Color.FromRgb(0x5E, 0x6E, 0x69));
            res["NeutralButtonBrush"] = new SolidColorBrush(Color.FromRgb(0xE8, 0xEE, 0xEC));
            res["NeutralButtonForegroundBrush"] = new SolidColorBrush(Color.FromRgb(0x1B, 0x2B, 0x28));
        }
    }

    private void ExitApplication()
    {
        DiagnosticLogger.Info("Filio shutting down");
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnSystemThemeChanged;
        _watcherService?.Dispose();
        _trayIcon?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        Shutdown();
    }
}
