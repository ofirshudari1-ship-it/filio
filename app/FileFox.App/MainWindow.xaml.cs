using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using FileFox.Models;
using FileFox.Services;

namespace FileFox;

public partial class MainWindow : Window
{
    private static readonly SolidColorBrush ActiveStatusBrush = new(Color.FromRgb(0x2E, 0x8B, 0x57));
    private static readonly SolidColorBrush PausedStatusBrush = new(Color.FromRgb(0xC9, 0x7B, 0x1C));

    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly FileWatcherService _watcherService;
    private readonly UpdateService _updateService = new();
    private ObservableCollection<FileLogEntry> _logEntries = new();
    private UpdateManifest? _pendingUpdate;
    private bool _isInitializing = true;

    /// <summary>מאפשר ל-App.xaml.cs לסנכרן את תפריט אייקון המגש כשהמצב משתנה מכאן.</summary>
    public event Action<bool>? PauseStateChanged;

    /// <summary>מאפשר ל-App.xaml.cs לסנכרן את תפריט אייקון המגש (טקסטים) אחרי שינוי שפה.</summary>
    public event Action? LanguageChanged;

    /// <summary>מבוקש כשמתחיל עדכון שקט - האפליקציה צריכה להשתחרר (לשחרר את הקובץ הרץ) ולצאת.</summary>
    public event Action? ExitRequestedForUpdate;

    public MainWindow(SettingsService settingsService, AppSettings settings, FileWatcherService watcherService)
    {
        InitializeComponent();

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
        // סגירת חלון ההגדרות לא סוגרת את האפליקציה - היא ממשיכה לפעול במגש המערכת
        e.Cancel = true;
        Hide();
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
        IsPausedCheckBox.IsChecked = _settings.IsPaused;
        MoveRadioButton.IsChecked = _settings.MoveInsteadOfCopy;
        CopyRadioButton.IsChecked = !_settings.MoveInsteadOfCopy;
        DetectDuplicatesCheckBox.IsChecked = _settings.DetectDuplicates;
        OrganizeInstallersCheckBox.IsChecked = _settings.OrganizeInstallers;
        EnableDefenderScanCheckBox.IsChecked = _settings.EnableDefenderScan;
        WatchedExtensionsTextBox.Text = _settings.WatchedFileExtensions;
        EnableImageOcrCheckBox.IsChecked = _settings.EnableImageOcr;
        IgnorePatternsTextBox.Text = _settings.IgnoredFileNamePatterns;
        SelectFolderOrderComboItem();
        UpdateCloudSyncInfo();
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
        _settings.IsPaused = IsPausedCheckBox.IsChecked ?? false;
        _settings.MoveInsteadOfCopy = MoveRadioButton.IsChecked ?? true;
        _settings.DetectDuplicates = DetectDuplicatesCheckBox.IsChecked ?? true;
        _settings.OrganizeInstallers = OrganizeInstallersCheckBox.IsChecked ?? false;
        _settings.EnableDefenderScan = EnableDefenderScanCheckBox.IsChecked ?? true;
        _settings.WatchedFileExtensions = WatchedExtensionsTextBox.Text.Trim();
        _settings.EnableImageOcr = EnableImageOcrCheckBox.IsChecked ?? true;
        _settings.IgnoredFileNamePatterns = IgnorePatternsTextBox.Text.Trim();

        if (FolderOrderComboBox.SelectedItem is ComboBoxItem selectedOrder &&
            Enum.TryParse<FolderStructureOrder>(selectedOrder.Tag as string, out var folderOrder))
        {
            _settings.FolderOrder = folderOrder;
        }

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

        var confirm = MessageBox.Show(
            LocalizationService.Format("ConfirmDeleteClient", client.Name),
            LocalizationService.Get("DialogTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
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

        var confirm = MessageBox.Show(
            LocalizationService.Format("ConfirmDeleteRule", rule.TypeName),
            LocalizationService.Get("DialogTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
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

    private void IgnoreLogEntryFile_Click(object sender, RoutedEventArgs e)
    {
        if (LogGrid.SelectedItem is not FileLogEntry entry)
            return;

        var fileName = Path.GetFileName(entry.OriginalPath);

        var confirm = MessageBox.Show(
            LocalizationService.Format("ConfirmIgnoreFile", fileName),
            LocalizationService.Get("DialogTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
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
            FileName = "FileFox-settings.json"
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
        CurrentVersionText.Text = UpdateService.CurrentVersion.ToString();
        HelpVersionText.Text = UpdateService.CurrentVersion.ToString();
        AutoCheckUpdatesCheckBox.IsChecked = _settings.AutoCheckForUpdates;
        UpdateFeedUrlTextBox.Text = _settings.UpdateFeedUrl;
    }

    private void SaveUpdatesSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings.AutoCheckForUpdates = AutoCheckUpdatesCheckBox.IsChecked ?? true;
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
        ChangelogBox.Visibility = Visibility.Collapsed;
        UpdateStatusText.Text = LocalizationService.Get("CheckingStatus");

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
        MessageBox.Show(LocalizationService.Get(messageKey), LocalizationService.Get("DialogTitle"), MessageBoxButton.OK, icon);
}
