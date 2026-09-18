using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using Filio.Models;
using Filio.Services;

namespace Filio;

/// <summary>
/// אשף "ברוכים הבאים" שרץ בפעם הראשונה בלבד: מיתוג, בחירת שפה, תיקיות, וסריקה מקומית
/// חד-פעמית (SmartScanService) שמציעה לקוחות במקום להשליך את המשתמש למסך הגדרות ריק.
/// </summary>
public partial class OnboardingWindow : Window
{
    private const int TotalSteps = 5;

    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly FileWatcherService _watcherService;
    private readonly List<UIElement> _steps;
    private readonly List<Ellipse> _stepDots = new();
    private readonly ObservableCollection<SuggestedClient> _suggestedClientsVm = new();

    private int _currentStep;

    public OnboardingWindow(SettingsService settingsService, AppSettings settings, FileWatcherService watcherService)
    {
        InitializeComponent();

        _settingsService = settingsService;
        _settings = settings;
        _watcherService = watcherService;

        FlowDirection = _settings.Language == LocalizationService.Hebrew ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        _steps = new List<UIElement> { StepWelcome, StepLanguage, StepFolders, StepScan, StepFinish };

        WatchFolderTextBox.Text = _settings.WatchFolders.FirstOrDefault()
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads";
        OutputFolderTextBox.Text = _settings.RootOutputFolder;
        OnboardOrganizeInstallersCheckBox.IsChecked = _settings.OrganizeInstallers;
        OnboardEnableDefenderScanCheckBox.IsChecked = _settings.EnableDefenderScan;
        OnboardStartWithWindowsCheckBox.IsChecked = _settings.StartWithWindows;
        SuggestedClientsList.ItemsSource = _suggestedClientsVm;

        BuildStepIndicator();
        UpdateLanguageCardSelection();
        ShowStep(0);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        // אם המשתמש סגר את החלון עם ה-X באמצע האשף - לא נציג אותו שוב בפעם הבאה,
        // פשוט ממשיכים עם מה שכבר נבחר עד כה (או ברירות המחדל).
        if (!_settings.HasCompletedOnboarding)
        {
            _settings.HasCompletedOnboarding = true;
            _settingsService.Save(_settings);
        }
    }

    // ===================== Step indicator =====================

    private void BuildStepIndicator()
    {
        StepIndicatorPanel.Children.Clear();
        _stepDots.Clear();

        for (var i = 0; i < TotalSteps; i++)
        {
            if (i > 0)
            {
                StepIndicatorPanel.Children.Add(new System.Windows.Shapes.Rectangle
                {
                    Width = 2,
                    Height = 14,
                    Fill = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }

            var dot = new Ellipse
            {
                Width = 11,
                Height = 11,
                Fill = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                Margin = new Thickness(0, 3, 0, 3)
            };
            _stepDots.Add(dot);
            StepIndicatorPanel.Children.Add(dot);
        }
    }

    private void UpdateStepIndicator()
    {
        for (var i = 0; i < _stepDots.Count; i++)
        {
            var isCurrentOrDone = i <= _currentStep;
            _stepDots[i].Fill = isCurrentOrDone ? Brushes.White : new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
            _stepDots[i].Width = _stepDots[i].Height = i == _currentStep ? 13 : 11;
        }
    }

    // ===================== Navigation =====================

    private void ShowStep(int index)
    {
        _currentStep = index;

        for (var i = 0; i < _steps.Count; i++)
            _steps[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;

        BackButton.Visibility = index == 0 ? Visibility.Collapsed : Visibility.Visible;

        // "דלג" גלוי בכל מסך (כולל לאחר סריקה חכמה) - חוץ ממסך הסיום, שבו אין מה לדלג עליו.
        SkipButton.Visibility = index == TotalSteps - 1 ? Visibility.Collapsed : Visibility.Visible;

        NextButton.Content = index switch
        {
            0 => LocalizationService.Get("OnboardGetStartedButton"),
            TotalSteps - 1 => LocalizationService.Get("OnboardStartButton"),
            _ => LocalizationService.Get("OnboardNextButton")
        };

        if (index == TotalSteps - 1)
            PopulateFinishStep();

        UpdateStepIndicator();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 0)
            ShowStep(_currentStep - 1);
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep == 2)
        {
            if (string.IsNullOrWhiteSpace(WatchFolderTextBox.Text) || string.IsNullOrWhiteSpace(OutputFolderTextBox.Text))
                return;

            if (!ValidateFolders())
                return;

            // מעדכנים רק את התיקייה הראשונה - כדי לא למחוק תיקיות מעקב נוספות שכבר הוגדרו
            // אם האשף רץ מחדש (למשל דרך טאב Help) על הגדרות קיימות.
            if (_settings.WatchFolders.Count > 0)
                _settings.WatchFolders[0] = WatchFolderTextBox.Text;
            else
                _settings.WatchFolders.Add(WatchFolderTextBox.Text);

            _settings.RootOutputFolder = OutputFolderTextBox.Text;
            _settings.OrganizeInstallers = OnboardOrganizeInstallersCheckBox.IsChecked ?? false;
            _settings.EnableDefenderScan = OnboardEnableDefenderScanCheckBox.IsChecked ?? true;
            _settings.StartWithWindows = OnboardStartWithWindowsCheckBox.IsChecked ?? true;
        }

        if (_currentStep == TotalSteps - 1)
        {
            FinishOnboarding();
            return;
        }

        ShowStep(_currentStep + 1);
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e) => ShowStep(_currentStep + 1);

    private void OnboardingWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && SkipButton.Visibility == Visibility.Visible)
        {
            SkipButton_Click(SkipButton, new RoutedEventArgs());
        }
    }

    // ===================== Step 1: language =====================

    private void EnglishCard_Click(object sender, RoutedEventArgs e) => SelectLanguage(LocalizationService.English);

    private void HebrewCard_Click(object sender, RoutedEventArgs e) => SelectLanguage(LocalizationService.Hebrew);

    private void SelectLanguage(string code)
    {
        _settings.Language = code;
        LocalizationService.SetLanguage(code);
        FlowDirection = code == LocalizationService.Hebrew ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        UpdateLanguageCardSelection();
        ShowStep(_currentStep); // מרענן טקסטי כפתורים לשפה החדשה
    }

    private void UpdateLanguageCardSelection()
    {
        var isHebrew = _settings.Language == LocalizationService.Hebrew;
        var accent = (Brush)FindResource("BrandPrimaryBrush");
        var neutral = (Brush)FindResource("BorderBrush2");

        EnglishCard.BorderBrush = isHebrew ? neutral : accent;
        HebrewCard.BorderBrush = isHebrew ? accent : neutral;
    }

    // ===================== Step 2: folders =====================

    private void BrowseWatchFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = PickFolder(WatchFolderTextBox.Text);
        if (folder != null)
        {
            WatchFolderTextBox.Text = folder;
            ValidateFolders();
        }
    }

    private void BrowseOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = PickFolder(OutputFolderTextBox.Text);
        if (folder != null)
        {
            OutputFolderTextBox.Text = folder;
            ValidateFolders();
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

    /// <summary>שדה העריכה כאן מייצג את התיקייה הראשונה בלבד - אם יש נוספות (למשל האשף רץ
    /// מחדש על הגדרות קיימות), הן נשמרות בלי שינוי ונבדקות גם הן.</summary>
    private List<string> CandidateWatchFolders()
    {
        var folders = new List<string> { WatchFolderTextBox.Text };
        folders.AddRange(_settings.WatchFolders.Skip(1));
        return folders;
    }

    private bool ValidateFolders()
    {
        var valid = PathValidationHelper.FindConflictingWatchFolder(CandidateWatchFolders(), OutputFolderTextBox.Text) == null;
        FoldersWarningText.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
        return valid;
    }

    // ===================== Step 3: smart scan =====================

    private async void ScanNowButton_Click(object sender, RoutedEventArgs e)
    {
        ScanNowButton.IsEnabled = false;
        ScanNowButton.Content = LocalizationService.Get("OnboardScanningStatus");

        var foldersToScan = CandidateWatchFolders();
        var rules = _settings.DocTypeRules.ToList();

        var result = await Task.Run(() => SmartScanService.Scan(foldersToScan, rules));
        ShowScanResults(result);
    }

    private void ShowScanResults(SmartScanResult result)
    {
        ScanPromptPanel.Visibility = Visibility.Collapsed;
        ScanResultsPanel.Visibility = Visibility.Visible;
        // "דלג" נשאר גלוי - עדיין ניתן לדלג הלאה גם אחרי שהסריקה הציגה תוצאות.

        if (result.FilesScanned == 0)
        {
            ScanSummaryText.Text = LocalizationService.Get("OnboardScanNoFilesFound");
            SuggestedClientsSection.Visibility = Visibility.Collapsed;
            return;
        }

        ScanSummaryText.Text = LocalizationService.Format(
            "OnboardScanSummaryFormat", result.FilesScanned, result.RecognizedCount, result.UnrecognizedCount);

        _suggestedClientsVm.Clear();
        foreach (var client in result.SuggestedClients)
            _suggestedClientsVm.Add(client);

        SuggestedClientsSection.Visibility = _suggestedClientsVm.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ===================== Step 4: finish =====================

    private void PopulateFinishStep()
    {
        FinishWatchText.Text = LocalizationService.Format("OnboardFinishWatchFormat", string.Join(", ", _settings.WatchFolders));
        FinishOutputText.Text = LocalizationService.Format("OnboardFinishOutputFormat", _settings.RootOutputFolder);

        var addedCount = _suggestedClientsVm.Count(c => c.IsSelected);
        FinishClientsText.Text = LocalizationService.Format("OnboardFinishClientsFormat", addedCount);
    }

    private void FinishOnboarding()
    {
        foreach (var suggestion in _suggestedClientsVm.Where(c => c.IsSelected))
        {
            if (_settings.Clients.Any(c => string.Equals(c.Name, suggestion.Name, StringComparison.OrdinalIgnoreCase)))
                continue;

            _settings.Clients.Add(new ClientProfile { Name = suggestion.Name });
        }

        _settings.HasCompletedOnboarding = true;
        _settingsService.Save(_settings);
        _watcherService.UpdateSettings(_settings);
        StartupService.SetEnabled(_settings.StartWithWindows);

        Close();
    }
}
