using System.IO;
using System.Windows;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using FileFox.Models;
using FileFox.Services;

namespace FileFox;

public partial class App : Application
{
    private readonly SettingsService _settingsService = new();
    private readonly UpdateService _updateService = new();
    private FileWatcherService? _watcherService;
    private TaskbarIcon? _trayIcon;
    private System.Windows.Controls.MenuItem? _openSettingsMenuItem;
    private System.Windows.Controls.MenuItem? _pauseMenuItem;
    private System.Windows.Controls.MenuItem? _openFolderMenuItem;
    private System.Windows.Controls.MenuItem? _checkUpdatesMenuItem;
    private System.Windows.Controls.MenuItem? _exitMenuItem;
    private AppSettings _settings = null!;
    private MainWindow? _mainWindow;
    private string? _lastFiledPath;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settings = _settingsService.Load();
        LocalizationService.SetLanguage(_settings.Language);
        Directory.CreateDirectory(_settings.RootOutputFolder);

        // מסנכרן את רישום ההפעלה האוטומטית בווינדוס עם ההגדרה השמורה, בכל הפעלה - לא רק
        // כשלוחצים "שמירה" בהגדרות. בלי זה, ההגדרה יכולה "לדרוך מים": settings.json אומר
        // StartWithWindows=true, אבל ערך הרישום עצמו נמחק/אף פעם לא נכתב (למשל אחרי הסרת
        // התקנה + הפעלה ידנית של קובץ ה-exe בלי לעבור דרך ההתקנה מחדש) - והמשתמש רואה תיבת
        // סימון מסומנת שלא משקפת את מה שבאמת יקרה באתחול הבא. נתפס בשימוש אמיתי.
        StartupService.SetEnabled(_settings.StartWithWindows);

        _watcherService = new FileWatcherService(_settings);
        _watcherService.FileProcessed += OnFileProcessed;
        _watcherService.Start();

        SetupTrayIcon();

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
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = LocalizationService.Get("TrayTooltip"),
            Icon = LoadAppIcon()
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
            _trayIcon.ToolTipText = LocalizationService.Get("TrayTooltip");

        if (_openSettingsMenuItem != null) _openSettingsMenuItem.Header = LocalizationService.Get("TrayOpenSettings");
        if (_pauseMenuItem != null) _pauseMenuItem.Header = LocalizationService.Get(_settings.IsPaused ? "TrayResume" : "TrayPause");
        if (_openFolderMenuItem != null) _openFolderMenuItem.Header = LocalizationService.Get("TrayOpenOutputFolder");
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

    private void ShowMainWindow()
    {
        if (_mainWindow == null)
        {
            _mainWindow = new MainWindow(_settingsService, _settings, _watcherService!);
            _mainWindow.Closed += (_, _) => _mainWindow = null;
            _mainWindow.PauseStateChanged += _ => RefreshTrayTexts(notifyPauseChange: false);
            _mainWindow.LanguageChanged += () => RefreshTrayTexts(notifyPauseChange: false);
            _mainWindow.ExitRequestedForUpdate += ExitApplication;
        }

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
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
        catch
        {
            // בדיקת עדכונים ברקע בהפעלה - כשל שקט, המשתמש עדיין יכול לבדוק ידנית בטאב עדכונים
        }
    }

    private void OnFileProcessed(FileLogEntry entry)
    {
        Dispatcher.Invoke(() =>
        {
            if (!_settings.ShowNotifications || _trayIcon == null)
                return;

            var message = entry.Success
                ? LocalizationService.Format("NotificationFiledTypeClientFormat", entry.DetectedType, entry.DetectedClient)
                : LocalizationService.Format("NotificationFailedFormat", Path.GetFileName(entry.OriginalPath));

            if (entry.Success)
                _lastFiledPath = entry.NewPath;

            _trayIcon.ShowNotification(Path.GetFileName(entry.OriginalPath), message, entry.Success ? NotificationIcon.Info : NotificationIcon.Error);
            _mainWindow?.NotifyNewLogEntry(entry);
        });
    }

    private void ExitApplication()
    {
        _watcherService?.Dispose();
        _trayIcon?.Dispose();
        Shutdown();
    }
}
