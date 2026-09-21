using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Filio.Models;
using Filio.Services;

namespace Filio;

public partial class MainWindow : Window
{
    private static readonly SolidColorBrush ActiveStatusBrush = new(Color.FromRgb(0x2E, 0x8B, 0x57));
    private static readonly SolidColorBrush PausedStatusBrush = new(Color.FromRgb(0xC9, 0x7B, 0x1C));

    private static readonly string WindowStatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Filio", "window-state.json");

    private record WindowStateData(double Left, double Top, double Width, double Height, bool IsMaximized);

    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly FileWatcherService _watcherService;
    private readonly UpdateService _updateService = new();
    private ObservableCollection<FileLogEntry> _logEntries = new();
    private UpdateManifest? _pendingUpdate;
    private string? _pendingGitHubReleaseUrl;
    private GitHubUpdateResult? _pendingGitHubUpdate;
    private bool _isInitializing = true;

    /// <summary>מאפשר ל-App.xaml.cs לסנכרן את תפריט אייקון המגש כשהמצב משתנה מכאן.</summary>
    public event Action<bool>? PauseStateChanged;

    /// <summary>מאפשר ל-App.xaml.cs לסנכרן את תפריט אייקון המגש (טקסטים) אחרי שינוי שפה.</summary>
    public event Action? LanguageChanged;

    /// <summary>מבוקש כשמתחיל עדכון שקט - האפליקציה צריכה להשתחרר (לשחרר את הקובץ הרץ) ולצאת.</summary>
    public event Action? ExitRequestedForUpdate;

    /// <summary>מבוקש בפעם הראשונה שהחלון ממוזער למגש, כדי ש-App.xaml.cs יציג הודעת balloon הסברתית.</summary>
    public event Action? FirstTrayMinimize;

    public MainWindow(SettingsService settingsService, AppSettings settings, FileWatcherService watcherService)
    {
        InitializeComponent();
        RestoreWindowState();

        _settingsService = settingsService;
        _settings = settings;
        _watcherService = watcherService;

        Closing += MainWindow_Closing;

        ApplyFlowDirection();
        SelectLanguageComboItem();
        LoadGeneralTab();
        ClientsGrid.ItemsSource = _settings.Clients;
        DocTypesGrid.ItemsSource = _settings.DocTypeRules;
        UpdateClientsEmptyHint();
        UpdateStatusPill();
        RefreshLogGrid();
        LoadUpdatesTab();

        _isInitializing = false;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        SaveWindowState();
        if (_settings.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            Hide();

            if (!_settings.HasShownTrayMinimizeHint)
            {
                _settings.HasShownTrayMinimizeHint = true;
                _settingsService.Save(_settings);
                FirstTrayMinimize?.Invoke();
            }
        }
        // if false, let the window close normally (App will handle shutdown)
    }

    private void RestoreWindowState()
    {
        try
        {
            if (!File.Exists(WindowStatePath)) return;
            var json = File.ReadAllText(WindowStatePath);
            var state = System.Text.Json.JsonSerializer.Deserialize<WindowStateData>(json);
            if (state == null) return;

            var screenWidth = SystemParameters.VirtualScreenWidth;
            var screenHeight = SystemParameters.VirtualScreenHeight;
            if (state.Left >= 0 && state.Top >= 0 &&
                state.Left + state.Width <= screenWidth &&
                state.Top + state.Height <= screenHeight)
            {
                Left = state.Left;
                Top = state.Top;
                Width = state.Width;
                Height = state.Height;
            }
            if (state.IsMaximized) WindowState = System.Windows.WindowState.Maximized;
        }
        catch { }
    }

    private void SaveWindowState()
    {
        try
        {
            var state = new WindowStateData(
                Left, Top, Width, Height,
                WindowState == System.Windows.WindowState.Maximized);
            Directory.CreateDirectory(Path.GetDirectoryName(WindowStatePath)!);
            File.WriteAllText(WindowStatePath,
                System.Text.Json.JsonSerializer.Serialize(state,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void QuickLangToggle_Click(object sender, RoutedEventArgs e)
    {
        var newLang = _settings.Language == LocalizationService.Hebrew
            ? LocalizationService.English
            : LocalizationService.Hebrew;

        _settings.Language = newLang;
        _settingsService.Save(_settings);
        LocalizationService.SetLanguage(newLang);
        SelectLanguageComboItem();
        ApplyFlowDirection();
        UpdateStatusPill();
        UpdateQuickLangButton();
        RefreshLogGrid();
        LanguageChanged?.Invoke();
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        _settings.Theme = _settings.Theme == AppTheme.Light ? AppTheme.Dark : AppTheme.Light;
        _settingsService.Save(_settings);
        App.ApplyTheme(_settings.Theme);
        UpdateThemeIcon();
        ThemeLightRadio.IsChecked = _settings.Theme == AppTheme.Light;
        ThemeDarkRadio.IsChecked = _settings.Theme == AppTheme.Dark;
    }

    private void UpdateQuickLangButton()
    {
        if (QuickLangButton.Template.FindName("LangLabel", QuickLangButton) is System.Windows.Controls.TextBlock lb)
            lb.Text = _settings.Language == LocalizationService.Hebrew ? "עב" : "EN";
    }

    private void UpdateThemeIcon()
    {
        if (ThemeToggleButton.Template.FindName("ThemeIcon", ThemeToggleButton) is System.Windows.Controls.TextBlock ti)
            ti.Text = _settings.Theme == AppTheme.Dark ? "☀️" : "🌙";
    }

    public void NotifyNewLogEntry(FileLogEntry entry)
    {
        _logEntries.Insert(0, entry);
        UpdateLogSummary(_logEntries);
    }

    private void ApplyFlowDirection()
    {
        FlowDirection = _settings.Language == LocalizationService.Hebrew
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
    }

    private void SelectLanguageComboItem()
    {
        var target = _settings.Language == LocalizationService.Hebrew ? LanguageHebrewItem : LanguageEnglishItem;
        LanguageComboBox.SelectedItem = target;
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (LanguageComboBox.SelectedItem is not ComboBoxItem item) return;

        var code = item.Tag as string ?? LocalizationService.English;
        if (code == _settings.Language) return;

        _settings.Language = code;
        _settingsService.Save(_settings);
        LocalizationService.SetLanguage(code);
        ApplyFlowDirection();
        UpdateStatusPill();
        RefreshLogGrid(); // StatusText תלוי-שפה - חייב לחשב מחדש עם המילון החדש
        LanguageChanged?.Invoke();
    }

    /// <summary>ה"רק בכישלון" הוא תת-אפשרות של "הצג התראות" - אין טעם להשאיר אותה זמינה
    /// כשהתראות כבויות לגמרי, אחרת המשתמש רואה תיבה מסומנת שלא באמת עושה כלום.</summary>
    private void ShowNotificationsCheckBox_Toggled(object sender, RoutedEventArgs e)
    {
        if (NotifyOnlyOnFailureCheckBox != null)
            NotifyOnlyOnFailureCheckBox.IsEnabled = ShowNotificationsCheckBox.IsChecked ?? false;
    }

    private void LoadGeneralTab()
    {
        WatchFoldersListBox.ItemsSource = _settings.WatchFolders;
        OutputFolderTextBox.Text = _settings.RootOutputFolder;

        // מציגים את המצב האמיתי ברישום של Windows, לא את ההגדרה השמורה בעיוורון - השניים
        // יכולים להתבדר בפועל (נתפס בשימוש אמיתי: settings.json אמר "כן" אבל הרישום היה
        // ריק). אם יש פער, שמירת המסך הזה תתקן אותו מיד (SaveGeneral_Click קורא ל-
        // StartupService.SetEnabled בכל מקרה).
        StartWithWindowsCheckBox.IsChecked = StartupService.IsCurrentlyEnabled();
        ShowNotificationsCheckBox.IsChecked = _settings.ShowNotifications;
        NotifyOnlyOnFailureCheckBox.IsChecked = _settings.NotifyOnlyOnFailure;
        NotifyOnlyOnFailureCheckBox.IsEnabled = _settings.ShowNotifications;
        IsPausedCheckBox.IsChecked = _settings.IsPaused;
        MoveRadioButton.IsChecked = _settings.MoveInsteadOfCopy;
        CopyRadioButton.IsChecked = !_settings.MoveInsteadOfCopy;
        DetectDuplicatesCheckBox.IsChecked = _settings.DetectDuplicates;
        OrganizeInstallersCheckBox.IsChecked = _settings.OrganizeInstallers;
        EnableDefenderScanCheckBox.IsChecked = _settings.EnableDefenderScan;
        ReviewLowConfidenceCheckBox.IsChecked = _settings.ReviewLowConfidenceMatches;
        WatchedExtensionsTextBox.Text = _settings.WatchedFileExtensions;
        EnableImageOcrCheckBox.IsChecked = _settings.EnableImageOcr;
        IgnorePatternsTextBox.Text = _settings.IgnoredFileNamePatterns;
        ThemeLightRadio.IsChecked = _settings.Theme == AppTheme.Light;
        ThemeDarkRadio.IsChecked = _settings.Theme == AppTheme.Dark;
        MinimizeToTrayCheckBox.IsChecked = _settings.MinimizeToTrayOnClose;
        LogRetentionDaysTextBox.Text = _settings.LogRetentionDays.ToString();
        SelectFolderOrderComboItem();
        UpdateCloudSyncInfo();
        UpdateQuickLangButton();
        UpdateThemeIcon();
    }

    private void SelectFolderOrderComboItem()
    {
        foreach (var item in FolderOrderComboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag as string == _settings.FolderOrder.ToString())
            {
                FolderOrderComboBox.SelectedItem = item;
                return;
            }
        }

        FolderOrderComboBox.SelectedIndex = 0;
    }

    private void UpdateCloudSyncInfo()
    {
        var syncedFolder = _settings.WatchFolders
            .Concat(new[] { OutputFolderTextBox.Text })
            .FirstOrDefault(CloudSyncDetector.IsInsideCloudSyncFolder);

        if (syncedFolder == null)
        {
            CloudSyncInfoText.Visibility = Visibility.Collapsed;
            return;
        }

        CloudSyncInfoText.Text = LocalizationService.Format("CloudSyncInfoFormat", CloudSyncDetector.DetectProvider(syncedFolder));
        CloudSyncInfoText.Visibility = Visibility.Visible;
    }

    private void UpdateStatusPill()
    {
        var isPaused = _settings.IsPaused;
        StatusPill.Background = isPaused ? PausedStatusBrush : ActiveStatusBrush;
        StatusTextBlock.Text = LocalizationService.Get(isPaused ? "StatusPaused" : "StatusActive");
        TogglePauseButton.Content = LocalizationService.Get(isPaused ? "ResumeButton" : "PauseButton");
    }

    private void TogglePause_Click(object sender, RoutedEventArgs e)
    {
        _settings.IsPaused = !_settings.IsPaused;
        IsPausedCheckBox.IsChecked = _settings.IsPaused;

        _settingsService.Save(_settings);
        _watcherService.UpdateSettings(_settings);

        UpdateStatusPill();
        PauseStateChanged?.Invoke(_settings.IsPaused);
    }

    private void AddWatchFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = PickFolder(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (folder == null) return;

        if (_settings.WatchFolders.Any(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase)))
            return;

        _settings.WatchFolders.Add(folder);
        UpdateCloudSyncInfo();
    }

    private void RemoveWatchFolder_Click(object sender, RoutedEventArgs e)
    {
        if (WatchFoldersListBox.SelectedItem is not string folder)
            return;

        if (_settings.WatchFolders.Count <= 1)
        {
            ShowMessage("AtLeastOneWatchFolderRequired", MessageBoxImage.Warning);
            return;
        }

        _settings.WatchFolders.Remove(folder);
        UpdateCloudSyncInfo();
    }

    private void BrowseOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = PickFolder(OutputFolderTextBox.Text);
        if (folder != null)
        {
            OutputFolderTextBox.Text = folder;
            UpdateCloudSyncInfo();
        }
    }

    private static string? PickFolder(string initial)
    {
        var dialog = new OpenFolderDialog
        {
            InitialDirectory = Directory.Exists(initial) ? initial : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private void SaveGeneral_Click(object sender, RoutedEventArgs e)
    {
        var outputFolder = OutputFolderTextBox.Text;

        if (_settings.WatchFolders.Count == 0 || string.IsNullOrWhiteSpace(outputFolder))
        {
            ShowMessage("MissingFoldersWarning", MessageBoxImage.Warning);
            return;
        }

        if (PathValidationHelper.FindConflictingWatchFolder(_settings.WatchFolders, outputFolder) != null)
        {
            ShowMessage("NestedFoldersWarning", MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(WatchedExtensionsTextBox.Text))
        {
            ShowMessage("WatchedExtensionsRequired", MessageBoxImage.Warning);
            return;
        }

        var wasPaused = _settings.IsPaused;

        _settings.RootOutputFolder = outputFolder;
        _settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked ?? true;
        _settings.ShowNotifications = ShowNotificationsCheckBox.IsChecked ?? true;
        _settings.NotifyOnlyOnFailure = NotifyOnlyOnFailureCheckBox.IsChecked ?? false;
        _settings.IsPaused = IsPausedCheckBox.IsChecked ?? false;
        _settings.MoveInsteadOfCopy = MoveRadioButton.IsChecked ?? true;
        _settings.DetectDuplicates = DetectDuplicatesCheckBox.IsChecked ?? true;
        _settings.OrganizeInstallers = OrganizeInstallersCheckBox.IsChecked ?? false;
        _settings.EnableDefenderScan = EnableDefenderScanCheckBox.IsChecked ?? true;
        _settings.ReviewLowConfidenceMatches = ReviewLowConfidenceCheckBox.IsChecked ?? false;
        _settings.WatchedFileExtensions = WatchedExtensionsTextBox.Text.Trim();
        _settings.EnableImageOcr = EnableImageOcrCheckBox.IsChecked ?? true;
        _settings.IgnoredFileNamePatterns = IgnorePatternsTextBox.Text.Trim();

        if (FolderOrderComboBox.SelectedItem is ComboBoxItem selectedOrder &&
            Enum.TryParse<FolderStructureOrder>(selectedOrder.Tag as string, out var folderOrder))
        {
            _settings.FolderOrder = folderOrder;
        }

        _settings.Theme = ThemeDarkRadio.IsChecked == true ? AppTheme.Dark : AppTheme.Light;
        _settings.MinimizeToTrayOnClose = MinimizeToTrayCheckBox.IsChecked ?? true;
        if (int.TryParse(LogRetentionDaysTextBox.Text, out var days) && days >= 0)
            _settings.LogRetentionDays = days;
        App.ApplyTheme(_settings.Theme);

        _settingsService.Save(_settings);
        StartupService.SetEnabled(_settings.StartWithWindows);
        _watcherService.UpdateSettings(_settings);

        UpdateStatusPill();
        UpdateCloudSyncInfo();
        if (wasPaused != _settings.IsPaused)
            PauseStateChanged?.Invoke(_settings.IsPaused);

        ShowMessage("SettingsSavedMessage", MessageBoxImage.Information);
    }

    private void AddClient_Click(object sender, RoutedEventArgs e)
    {
        _settings.Clients.Add(new ClientProfile { Name = "New Client" });
        UpdateClientsEmptyHint();
    }

    private void RemoveClient_Click(object sender, RoutedEventArgs e)
    {
        if (ClientsGrid.SelectedItem is not ClientProfile client)
            return;

        var confirm = ShowConfirm(
            LocalizationService.Format("ConfirmDeleteClient", client.Name), LocalizationService.Get("DialogTitle"));
        if (confirm != MessageBoxResult.Yes)
            return;

        _settings.Clients.Remove(client);
        UpdateClientsEmptyHint();
    }

    private void SaveClients_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.Save(_settings);
        _watcherService.UpdateSettings(_settings);
        ShowMessage("ClientsSavedMessage", MessageBoxImage.Information);
    }

    private void UpdateClientsEmptyHint()
    {
        ClientsEmptyHint.Visibility = _settings.Clients.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddDocType_Click(object sender, RoutedEventArgs e) => _settings.DocTypeRules.Add(new DocTypeRule { TypeName = "New Type" });

    private void RemoveDocType_Click(object sender, RoutedEventArgs e)
    {
        if (DocTypesGrid.SelectedItem is not DocTypeRule rule)
            return;

        var confirm = ShowConfirm(
            LocalizationService.Format("ConfirmDeleteRule", rule.TypeName), LocalizationService.Get("DialogTitle"));
        if (confirm != MessageBoxResult.Yes)
            return;

        _settings.DocTypeRules.Remove(rule);
    }

    private void SaveDocTypes_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.Save(_settings);
        _watcherService.UpdateSettings(_settings);
        ShowMessage("RulesSavedMessage", MessageBoxImage.Information);
    }

    private void RefreshLog_Click(object sender, RoutedEventArgs e) => RefreshLogGrid();

    /// <summary>קריאה מבחוץ (App.xaml.cs) אחרי ביטול תיוק שבוצע ישירות מתפריט המגש, כדי
    /// שטאב "פעילות" יציג את המצב העדכני אם החלון הראשי כבר פתוח.</summary>
    public void RefreshLogGridPublic() => Dispatcher.Invoke(RefreshLogGrid);

    private void RefreshLogGrid()
    {
        var entries = FileOrganizerService.ReadLog();
        entries.Reverse();
        _logEntries = new ObservableCollection<FileLogEntry>(entries);
        LogGrid.ItemsSource = _logEntries;
        UpdateLogSummary(_logEntries);
    }

    private void UpdateLogSummary(IReadOnlyCollection<FileLogEntry> entries)
    {
        LogTotalText.Text = entries.Count.ToString();
        LogSuccessText.Text = entries.Count(x => x.Success).ToString();
        LogFailedText.Text = entries.Count(x => !x.Success).ToString();
        LogEmptyHint.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UndoLogEntry_Click(object sender, RoutedEventArgs e)
    {
        if (LogGrid.SelectedItem is not FileLogEntry entry)
            return;

        var organizer = new FileOrganizerService();
        if (organizer.Undo(entry))
        {
            ShowMessage("UndoSuccessMessage", MessageBoxImage.Information);
            RefreshLogGrid();
        }
        else
        {
            ShowMessage("UndoFailureMessage", MessageBoxImage.Warning);
        }
    }

    private async void RetryLogEntry_Click(object sender, RoutedEventArgs e)
    {
        if (LogGrid.SelectedItem is not FileLogEntry entry)
            return;

        if (!File.Exists(entry.OriginalPath))
        {
            ShowMessage("RetryFileMissing", MessageBoxImage.Warning);
            return;
        }

        await _watcherService.RetryFileAsync(entry.OriginalPath);
        RefreshLogGrid();
    }

    /// <summary>
    /// "תקן / תייק…" - הפעולה שמממשת גם את "בדיקה לפני תיוק" (עבור רשומות NeedsReview,
    /// שהקובץ שלהן עדיין יושב במקום המקורי) וגם תיקון של קובץ שכבר תויק ללקוח/סוג לא נכון.
    /// כשהמשתמש מסמן "לזכור את זה", המילה הנלמדת מתווספת לכינויי הלקוח שנבחר ונשמרת מיד -
    /// זו "הלמידה מתיקוני משתמש" שמפחיתה תיקון חוזר על קבצים דומים בעתיד.
    /// </summary>
    private void FixClientLogEntry_Click(object sender, RoutedEventArgs e)
    {
        if (LogGrid.SelectedItem is not FileLogEntry entry)
            return;

        var currentPath = entry.NewPath ?? entry.OriginalPath;
        if (!File.Exists(currentPath))
        {
            ShowMessage("ReassignFileMissing", MessageBoxImage.Warning);
            return;
        }

        var dialog = new ReassignWindow(
            entry.OriginalPath,
            _settings.Clients.Select(c => c.Name),
            _settings.DocTypeRules.Select(r => r.TypeName),
            entry.DetectedClient,
            entry.DetectedType) { Owner = this };

        if (dialog.ShowDialog() != true)
            return;

        if (dialog.ShouldLearn && dialog.SuggestedKeyword != null)
            LearnClientKeyword(dialog.SelectedClient, dialog.SuggestedKeyword);

        var date = DateTime.TryParse(entry.DetectedDate, out var parsed) ? parsed : DateTime.Now;
        var classification = new ClassificationResult
        {
            ClientName = DocumentClassifier.SanitizeForFolderName(dialog.SelectedClient),
            DocType = dialog.SelectedDocType,
            DocumentDate = date,
            MatchExplanation = "Manually corrected by user via \"Fix / File…\""
        };

        var organizer = new FileOrganizerService();
        var result = organizer.Reclassify(entry, classification, _settings);

        ShowMessage(result.Success ? "ReassignSuccessMessage" : "ReassignFailureMessage",
            result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);

        RefreshLogGrid();
    }

    /// <summary>מוסיף מילת-מפתח שנלמדה מתיקון משתמש לכינויי הלקוח שנבחר (או יוצר פרופיל
    /// לקוח חדש אם עדיין לא קיים אחד בשם הזה) ושומר מיד - כך שהתיוג הבא של קובץ דומה יזהה
    /// את הלקוח אוטומטית בלי תיקון חוזר.</summary>
    private void LearnClientKeyword(string clientName, string keyword)
    {
        var client = _settings.Clients.FirstOrDefault(c => string.Equals(c.Name, clientName, StringComparison.OrdinalIgnoreCase));
        if (client == null)
        {
            client = new ClientProfile { Name = clientName, Keywords = keyword };
            _settings.Clients.Add(client);
        }
        else
        {
            var existing = client.Keywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!existing.Contains(keyword, StringComparer.OrdinalIgnoreCase))
                client.Keywords = string.IsNullOrWhiteSpace(client.Keywords) ? keyword : $"{client.Keywords}, {keyword}";
        }

        _settingsService.Save(_settings);
    }

    private void IgnoreLogEntryFile_Click(object sender, RoutedEventArgs e)
    {
        if (LogGrid.SelectedItem is not FileLogEntry entry)
            return;

        var fileName = Path.GetFileName(entry.OriginalPath);

        var confirm = ShowConfirm(
            LocalizationService.Format("ConfirmIgnoreFile", fileName), LocalizationService.Get("DialogTitle"));
        if (confirm != MessageBoxResult.Yes)
            return;

        var existing = (_settings.IgnoredFileNamePatterns ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (!existing.Contains(fileName, StringComparer.OrdinalIgnoreCase))
        {
            existing.Add(fileName);
            _settings.IgnoredFileNamePatterns = string.Join(", ", existing);
            _settingsService.Save(_settings);
            _watcherService.UpdateSettings(_settings);
            IgnorePatternsTextBox.Text = _settings.IgnoredFileNamePatterns;

            // כמו כל שמירת הגדרות אחרת באפליקציה (SaveGeneral/SaveClients/SaveDocTypes/
            // SaveUpdatesSettings) - אישור מיידי ✅ עקבי, לא שינוי שקט של state (סעיף 18.5).
            ShowMessage("FileIgnoredMessage", MessageBoxImage.Information);
        }
    }

    private void OpenOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_settings.RootOutputFolder);
        System.Diagnostics.Process.Start("explorer.exe", _settings.RootOutputFolder);
    }

    private void RerunSetupWizard_Click(object sender, RoutedEventArgs e)
    {
        var onboarding = new OnboardingWindow(_settingsService, _settings, _watcherService);
        onboarding.ShowDialog();

        // האשף עשוי לשנות שפה/תיקיות/לקוחות - מרעננים את כל התצוגה (אותה רשימת ריענון
        // כמו אחרי ייבוא הגדרות, כדי שטאב Updates לא יישאר עם ערכים ישנים אם האשף אי-פעם
        // ישפיע עליהם בעתיד)
        ApplyFlowDirection();
        LoadGeneralTab();
        LoadUpdatesTab();
        UpdateClientsEmptyHint();
        UpdateStatusPill();
        LanguageChanged?.Invoke();
    }

    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = Path.GetDirectoryName(SettingsService.LogPath);
        if (folder != null)
            System.Diagnostics.Process.Start("explorer.exe", folder);
    }

    private void ExportSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON (*.json)|*.json",
            FileName = "Filio-settings.json"
        };

        if (dialog.ShowDialog() != true)
            return;

        if (_settingsService.Export(_settings, dialog.FileName))
            ShowMessage("ExportSettingsSuccess", MessageBoxImage.Information);
        else
            ShowMessage("ExportSettingsFailed", MessageBoxImage.Error);
    }

    private void ImportSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "JSON (*.json)|*.json" };
        if (dialog.ShowDialog() != true)
            return;

        var imported = _settingsService.Import(dialog.FileName);
        if (imported == null)
        {
            ShowMessage("ImportSettingsFailed", MessageBoxImage.Error);
            return;
        }

        _settings.CopyFrom(imported);
        _settingsService.Save(_settings);
        StartupService.SetEnabled(_settings.StartWithWindows);
        LocalizationService.SetLanguage(_settings.Language);
        _watcherService.UpdateSettings(_settings);

        ApplyFlowDirection();
        SelectLanguageComboItem();
        LoadGeneralTab();
        LoadUpdatesTab();
        UpdateClientsEmptyHint();
        UpdateStatusPill();
        LanguageChanged?.Invoke();

        ShowMessage("ImportSettingsSuccess", MessageBoxImage.Information);
    }

    // ===================== Updates tab =====================

    private void LoadUpdatesTab()
    {
        var v = UpdateService.CurrentVersion;
        var versionText = $"{v.Major}.{v.Minor}.{v.Build}";
        CurrentVersionText.Text = versionText;
        HelpVersionText.Text = versionText;
        AutoCheckUpdatesCheckBox.IsChecked = _settings.AutoCheckForUpdates;
        AutoInstallUpdatesCheckBox.IsChecked = _settings.AutoInstallUpdates;
        UpdateFeedUrlTextBox.Text = _settings.UpdateFeedUrl;
    }

    private void SaveUpdatesSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings.AutoCheckForUpdates = AutoCheckUpdatesCheckBox.IsChecked ?? true;
        _settings.AutoInstallUpdates = AutoInstallUpdatesCheckBox.IsChecked ?? false;
        _settings.UpdateFeedUrl = UpdateFeedUrlTextBox.Text.Trim();
        _settingsService.Save(_settings);
        ShowMessage("SettingsSavedMessage", MessageBoxImage.Information);
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        await RunUpdateCheckAsync(UpdateFeedUrlTextBox.Text.Trim());
    }

    public async Task RunUpdateCheckAsync(string feedUrl)
    {
        CheckForUpdatesButton.IsEnabled = false;
        DownloadAndInstallButton.Visibility = Visibility.Collapsed;
        UpdateNowButton.Visibility = Visibility.Collapsed;
        OpenReleasePageButton.Visibility = Visibility.Collapsed;
        ChangelogBox.Visibility = Visibility.Collapsed;
        UpdateStatusText.Text = LocalizationService.Get("CheckingStatus");
        _pendingGitHubReleaseUrl = null;
        _pendingGitHubUpdate = null;

        // UpdateFeedUrl הוא הזרימה הישנה יותר (manifest ייעודי + הורדה+התקנה שקטה),
        // ודורש שהמשתמש יזין כתובת בעצמו. כברירת מחדל השדה ריק, אז ללא כתובת feed
        // בודקים ישירות מול GitHub Releases - המקור הרשמי היום - ומפנים לעמוד ה-Release
        // לצפייה/הורדה ידנית במקום התקנה שקטה.
        if (string.IsNullOrWhiteSpace(feedUrl))
        {
            await RunGitHubUpdateCheckAsync();
            return;
        }

        try
        {
            _pendingUpdate = await _updateService.CheckForUpdateAsync(feedUrl);

            if (_pendingUpdate == null)
            {
                UpdateStatusText.Text = LocalizationService.Get("UpToDateStatus");
            }
            else
            {
                UpdateStatusText.Text = LocalizationService.Format("UpdateAvailableStatus", _pendingUpdate.Version);
                if (!string.IsNullOrWhiteSpace(_pendingUpdate.Notes))
                {
                    ChangelogText.Text = _pendingUpdate.Notes;
                    ChangelogBox.Visibility = Visibility.Visible;
                }
                DownloadAndInstallButton.Visibility = Visibility.Visible;
            }
        }
        catch (InsecureUpdateUrlException)
        {
            // כתובת http:// לא-מוצפנת - עדכון רץ בשקט בלי שהמשתמש רואה כל שלב, אז אין
            // מתפשרים על זה: אפילו כתובת feed מקומית/פנימית חייבת להיות https.
            UpdateStatusText.Text = LocalizationService.Get("UpdateInsecureUrlStatus");
        }
        catch
        {
            UpdateStatusText.Text = LocalizationService.Get("UpdateErrorStatus");
        }
        finally
        {
            CheckForUpdatesButton.IsEnabled = true;
        }
    }

    private async Task RunGitHubUpdateCheckAsync()
    {
        try
        {
            var result = await _updateService.CheckGitHubReleaseAsync();
            if (result == null)
            {
                UpdateStatusText.Text = LocalizationService.Get("UpToDateStatus");
            }
            else
            {
                UpdateStatusText.Text = LocalizationService.Format("UpdateAvailableStatus", result.LatestVersion.ToString(3));
                _pendingGitHubReleaseUrl = result.ReleaseUrl;
                _pendingGitHubUpdate = result;

                // "Update Now" (silent download + install) needs a direct installer asset URL;
                // if the Release was published without one (or under an unexpected file name),
                // fall back to the old flow - a button that just opens the Release page for a
                // manual download, same as before this feature existed.
                if (!string.IsNullOrWhiteSpace(result.InstallerAssetUrl))
                    UpdateNowButton.Visibility = Visibility.Visible;
                else
                    OpenReleasePageButton.Visibility = Visibility.Visible;
            }
        }
        catch
        {
            UpdateStatusText.Text = LocalizationService.Get("UpdateErrorStatus");
        }
        finally
        {
            CheckForUpdatesButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// "עדכן עכשיו" - הכפתור החדש שדורש קליק אחד (בניגוד לזרימה הישנה: פתיחת דפדפן והתקנה
    /// ידנית). מוריד את קובץ ההתקנה שהתגלה ב-GitHub Releases, מאמת שההורדה הושלמה במלואה
    /// (גודל מול מה שדווח ב-API), ואז מריץ אותו בשקט ויוצא - בדיוק כמו DownloadAndInstall_Click
    /// לזרימת ה-manifest הישנה. בכל כשל (רשת, HTTPS לא-מאובטח, גודל לא תואם) חוזרים לזרימת
    /// הגיבוי הקיימת: הודעת שגיאה + כפתור "פתיחת עמוד הגרסה" להורדה ידנית, בלי לגעת בהתקנה
    /// הקיימת של Filio.
    /// </summary>
    private async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingGitHubUpdate?.InstallerAssetUrl == null) return;

        UpdateNowButton.IsEnabled = false;
        CheckForUpdatesButton.IsEnabled = false;
        UpdateStatusText.Text = LocalizationService.Get("DownloadingStatus");

        try
        {
            var installerPath = await _updateService.DownloadInstallerAsync(
                _pendingGitHubUpdate.InstallerAssetUrl,
                expectedSizeBytes: _pendingGitHubUpdate.InstallerAssetSizeBytes);

            UpdateStatusText.Text = LocalizationService.Get("InstallingStatus");
            UpdateService.LaunchSilentInstall(installerPath);
            ExitRequestedForUpdate?.Invoke();
        }
        catch (InsecureUpdateUrlException)
        {
            UpdateStatusText.Text = LocalizationService.Get("UpdateInsecureUrlStatus");
            FallBackToManualUpdate();
        }
        catch (UpdateIntegrityException)
        {
            UpdateStatusText.Text = LocalizationService.Get("UpdateIntegrityFailedStatus");
            FallBackToManualUpdate();
        }
        catch
        {
            // רשת נפלה, דיסק מלא (IOException בזמן כתיבת הקובץ הזמני), timeout וכו' - כל כשל
            // אחר שלא נתפס במפורש למעלה. UpdateService.DownloadInstallerAsync כבר מנקה את
            // הקובץ החלקי לפני שהחריגה מגיעה לכאן.
            UpdateStatusText.Text = LocalizationService.Get("UpdateErrorStatus");
            FallBackToManualUpdate();
        }
    }

    /// <summary>נקודת הגיבוי המשותפת לכל כשל ב-UpdateNow_Click: Filio הרץ כרגע לא נגעו בו כלל
    /// (הכשל תמיד קורה לפני LaunchSilentInstall/ExitRequestedForUpdate), אז פשוט מחזירים את
    /// כפתורי הבקרה ומציגים את כפתור הגיבוי הידן - בדיוק כמו הזרימה שהייתה קיימת לפני
    /// שהתווסף עדכון-בקליק-אחד.</summary>
    private void FallBackToManualUpdate()
    {
        UpdateNowButton.Visibility = Visibility.Collapsed;
        OpenReleasePageButton.Visibility = Visibility.Visible;
        UpdateNowButton.IsEnabled = true;
        CheckForUpdatesButton.IsEnabled = true;
    }

    private void OpenReleasePage_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_pendingGitHubReleaseUrl))
            return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_pendingGitHubReleaseUrl)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            UpdateStatusText.Text = LocalizationService.Get("UpdateErrorStatus");
        }
    }

    private async void DownloadAndInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate == null) return;

        DownloadAndInstallButton.IsEnabled = false;
        CheckForUpdatesButton.IsEnabled = false;
        UpdateStatusText.Text = LocalizationService.Get("DownloadingStatus");

        try
        {
            var installerPath = await _updateService.DownloadInstallerAsync(_pendingUpdate.InstallerUrl, _pendingUpdate.Sha256);
            UpdateStatusText.Text = LocalizationService.Get("InstallingStatus");

            UpdateService.LaunchSilentInstall(installerPath);
            ExitRequestedForUpdate?.Invoke();
        }
        catch (InsecureUpdateUrlException)
        {
            UpdateStatusText.Text = LocalizationService.Get("UpdateInsecureUrlStatus");
            DownloadAndInstallButton.IsEnabled = true;
            CheckForUpdatesButton.IsEnabled = true;
        }
        catch (UpdateIntegrityException)
        {
            // הקובץ שהתקבל לא תואם את החתימה שהוצהרה ב-manifest - לא מריצים אותו, בלי יוצא מן הכלל.
            UpdateStatusText.Text = LocalizationService.Get("UpdateIntegrityFailedStatus");
            DownloadAndInstallButton.IsEnabled = true;
            CheckForUpdatesButton.IsEnabled = true;
        }
        catch
        {
            UpdateStatusText.Text = LocalizationService.Get("UpdateErrorStatus");
            DownloadAndInstallButton.IsEnabled = true;
            CheckForUpdatesButton.IsEnabled = true;
        }
    }

    // ===================== Helpers =====================

    public void MainTabControlSelectUpdatesTab() => MainTabControl.SelectedIndex = 4;

    /// <summary>מסנכרן את חלון ההגדרות כשמצב ההשהיה שונה ממקור חיצוני (תפריט אייקון המגש).</summary>
    public void SyncPausedStateFromOutside(bool isPaused)
    {
        IsPausedCheckBox.IsChecked = isPaused;
        UpdateStatusPill();
    }

    private static void ShowMessage(string messageKey, MessageBoxImage icon) =>
        MessageBox.Show(LocalizationService.Get(messageKey), LocalizationService.Get("DialogTitle"), MessageBoxButton.OK,
            icon, MessageBoxResult.OK, RtlOptions());

    /// <summary>מציג MessageBox עם כפתורי כן/לא ותומך בכיווניות RTL (§18.1/18.2) - בלי זה, גם
    /// כשהטקסט עברי, החלון עצמו נשאר LTR (כותרת/כפתורים לא הופכים כיוון) כי MessageBox.Show
    /// לא מזהה שפה אוטומטית מ-FlowDirection של שאר האפליקציה.</summary>
    private static MessageBoxResult ShowConfirm(string text, string title) =>
        MessageBox.Show(text, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No, RtlOptions());

    private static MessageBoxOptions RtlOptions() =>
        LocalizationService.IsRightToLeft ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : MessageBoxOptions.None;
}
