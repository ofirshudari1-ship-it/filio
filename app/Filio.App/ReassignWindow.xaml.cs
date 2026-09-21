using System.IO;
using System.Windows;
using System.Windows.Controls;
using Filio.Services;

namespace Filio;

/// <summary>
/// חלון קטן שמאפשר למשתמש לבחור ידנית לקוח וסוג מסמך לקובץ - משמש הן לתיקון קובץ שתויק
/// לא נכון (Activity → "תקן / תייק…") והן לתיוק קובץ שממתין ב"בדיקה לפני תיוק" (ראו
/// AppSettings.ReviewLowConfidenceMatches). זו יכולת ה"סקירה+תיקון" שנלקחה ממחקר תחרותי
/// (Hazel) ומאפשרת גם "למידה" קלה: הצ'קבוקס "לזכור את זה" מוסיף כינוי ללקוח שנבחר כדי
/// שקבצים דומים בעתיד יזוהו אוטומטית בלי תיקון חוזר.
/// </summary>
public partial class ReassignWindow : Window
{
    public string SelectedClient { get; private set; } = string.Empty;
    public string SelectedDocType { get; private set; } = string.Empty;
    public bool ShouldLearn { get; private set; }

    private readonly string? _suggestedKeyword;

    public ReassignWindow(string sourceFileName, IEnumerable<string> knownClients, IEnumerable<string> knownDocTypes,
        string? currentClient, string? currentDocType)
    {
        InitializeComponent();
        FlowDirection = LocalizationService.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        FileNameText.Text = Path.GetFileName(sourceFileName);

        ClientCombo.ItemsSource = knownClients.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderBy(c => c).ToList();
        DocTypeCombo.ItemsSource = knownDocTypes.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().OrderBy(t => t).ToList();

        if (!string.IsNullOrWhiteSpace(currentClient) && currentClient != "Unknown Client")
            ClientCombo.Text = currentClient;
        if (!string.IsNullOrWhiteSpace(currentDocType) && currentDocType != "Unrecognized")
            DocTypeCombo.Text = currentDocType;

        _suggestedKeyword = DocumentClassifier.SuggestClientKeywordFromFileName(sourceFileName);
        ClientCombo.SelectionChanged += (_, _) => UpdateLearnHint();
        ClientCombo.LostFocus += (_, _) => UpdateLearnHint();
        UpdateLearnHint();
    }

    private void UpdateLearnHint()
    {
        var client = ClientCombo.Text?.Trim();
        LearnHintText.Text = !string.IsNullOrWhiteSpace(client) && !string.IsNullOrWhiteSpace(_suggestedKeyword)
            ? LocalizationService.Format("ReassignLearnHintFormat", _suggestedKeyword)
            : string.Empty;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var client = ClientCombo.Text?.Trim();
        var docType = DocTypeCombo.Text?.Trim();

        if (string.IsNullOrWhiteSpace(client))
        {
            var options = LocalizationService.IsRightToLeft
                ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign
                : MessageBoxOptions.None;
            MessageBox.Show(LocalizationService.Get("ReassignClientRequired"), LocalizationService.Get("DialogTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, options);
            return;
        }

        SelectedClient = client;
        SelectedDocType = string.IsNullOrWhiteSpace(docType) ? "Unrecognized" : docType;
        ShouldLearn = LearnCheckBox.IsChecked == true && !string.IsNullOrWhiteSpace(_suggestedKeyword);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>המילה שהוצעה כ"כינוי נלמד" עבור הלקוח שנבחר - null אם לא נמצאה מילה מתאימה בשם הקובץ.</summary>
    public string? SuggestedKeyword => _suggestedKeyword;
}
