using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FilioSetup
{
    // Brand palette straight from assets/BRAND.md (dark-mode column) — this installer uses
    // Filio's own dark theme, not a generic "themed shell around native controls" look. Every
    // control below sets these colors explicitly (BackColor/ForeColor/FlatStyle) rather than
    // relying on Application.EnableVisualStyles() or the form's own BackColor to cascade down -
    // that cascade does NOT happen for CheckBox/ProgressBar in WinForms (they always paint with
    // the OS theme regardless of parent BackColor), which is exactly the "silently falls back to
    // native OS chrome" bug class flagged for this build. See ThemedCheckBox/ThemedProgressBar
    // below, which own-draw instead of relying on the native control.
    static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(0x0D, 0x1A, 0x18);          // background (dark)
        public static readonly Color Panel = Color.FromArgb(0x15, 0x24, 0x20);        // surface (dark)
        public static readonly Color Panel2 = Color.FromArgb(0x1C, 0x30, 0x2B);       // surface-alt (dark)
        public static readonly Color Border = Color.FromArgb(0x2A, 0x40, 0x40);       // border (dark)
        public static readonly Color HeaderBg = Color.FromArgb(0x0F, 0x53, 0x48);     // primary-dark
        public static readonly Color HeaderBg2 = Color.FromArgb(0x0A, 0x38, 0x30);
        public static readonly Color HeaderText = Color.White;
        public static readonly Color HeaderSub = Color.FromArgb(0x8A, 0xBD, 0xB5);    // text-muted (dark)
        public static readonly Color Primary = Color.FromArgb(0x1E, 0x7F, 0x72);      // primary
        public static readonly Color Accent = Color.FromArgb(0xDB, 0x8A, 0x1F);       // accent (CTA fill)
        public static readonly Color AccentDark = Color.FromArgb(0xB8, 0x72, 0x18);
        public static readonly Color Text = Color.FromArgb(0xE9, 0xF3, 0xF1);
        public static readonly Color TextMuted = Color.FromArgb(0x8A, 0xBD, 0xB5);    // text-muted (dark)
        public static readonly Color Warning = Color.FromArgb(0xC9, 0x7B, 0x1C);      // warning
        public static readonly Color Error = Color.FromArgb(0xC0, 0x39, 0x2B);        // error
    }

    // English is the default for every tool in this portfolio (2026-09-14 decision), with a
    // Hebrew option offered on the first page rather than auto-detected from OS locale.
    static class Loc
    {
        public static string Lang = "en";

        private static readonly Dictionary<string, Dictionary<string, string>> T =
            new Dictionary<string, Dictionary<string, string>>();

        static Loc()
        {
            var en = new Dictionary<string, string>
            {
                { "select_language", "Select Language" },
                { "select_language_desc", "Choose your preferred language for the installer." },
                { "title_install", "Install " },
                { "title_update", "Update " },
                { "header_sub", "Files itself away — Setup" },
                { "welcome_title", "Filio automatically files your downloads" },
                { "welcome_body",
                    "Filio watches your Downloads folder (and any others you choose) and quietly moves " +
                    "every new file into a tidy Client \\ Year \\ Document-type structure — invoices, " +
                    "contracts, scans and everything else, sorted the moment they land." },
                { "bullet_ocr", "Recognizes clients from file names AND from the document's own text, using local OCR (Hebrew + English) — no cloud, no upload." },
                { "bullet_dupes", "Detects duplicate files by content hash and keeps a full activity log, with one-click undo for the last move." },
                { "bullet_defender", "Scans every file with Windows Defender before filing it, so nothing unsafe gets organized into your archive." },
                { "bullet_privacy", "100% local processing. Filio never deletes a file outright and never sends your files or their contents anywhere." },
                { "btn_next", "Next" },
                { "btn_back", "Back" },
                { "btn_cancel", "Cancel" },
                { "license_title", "License Agreement" },
                { "license_desc", "Please read the following license agreement before continuing." },
                { "license_agree", "I accept the terms of the License Agreement" },
                { "location_title", "Choose Install Location" },
                { "desc_same_version", "The installed version ({0}) is already up to date. You can reinstall anyway to repair files." },
                { "btn_reinstall", "Reinstall" },
                { "desc_update", "An existing installation was detected (version {0}). Setup will update it to version {1} in place — your settings and activity log are kept." },
                { "btn_update", "Update" },
                { "desc_fresh", "{0} version {1} will be installed." },
                { "btn_install", "Install" },
                { "lbl_located_at", "Located at:" },
                { "lbl_will_install_to", "Install to:" },
                { "btn_browse", "Browse..." },
                { "chk_desktop_shortcut", "Create a desktop shortcut" },
                { "chk_startmenu_shortcut", "Create a Start Menu shortcut" },
                { "chk_autostart", "Start automatically with Windows" },
                { "note_admin", "Filio installs to Program Files for all users and needs administrator rights (UAC) to do that." },
                { "lbl_installing", "Installing..." },
                { "title_update_complete", "Update Complete" },
                { "title_install_complete", "Installation Complete" },
                { "desc_finish", "{0} version {1} is installed and ready to use. You can remove it any time from Windows \"Apps\" settings." },
                { "chk_launch_now", "Launch {0} now" },
                { "btn_finish", "Finish" },
                { "app_running_title", "Filio Setup" },
                { "app_running_msg", "Filio is currently running.\n\nPlease close it before continuing the installation." },
                { "browse_folder_title", "Choose install folder" },
                { "footer_copyright", "© 2026 Ofir Shudari — All Rights Reserved" },
                { "install_error_title", "Setup Error" },
                { "install_error_body", "Setup error:\n{0}" },
                { "uninstall_confirm_title", "Uninstall Filio" },
                { "uninstall_confirm_body", "Are you sure you want to remove Filio {0} from this computer?" },
                { "uninstall_delete_data_title", "Delete Filio's data?" },
                { "uninstall_delete_data_body", "Do you also want to delete Filio's settings and activity log? (This cannot be undone.)" },
                { "uninstall_done_title", "Uninstall Complete" },
                { "uninstall_done_body", "Filio has been removed from this computer." },
            };

            var he = new Dictionary<string, string>
            {
                { "select_language", "בחירת שפה" },
                { "select_language_desc", "בחר/י את השפה המועדפת עבור תוכנת ההתקנה." },
                { "title_install", "התקנת " },
                { "title_update", "עדכון " },
                { "header_sub", "מתייקת לבד — התקנה" },
                { "welcome_title", "Filio מתייקת את ההורדות שלך אוטומטית" },
                { "welcome_body",
                    "Filio עוקבת אחרי תיקיית ההורדות שלך (וכל תיקייה נוספת שתבחר/י) ומעבירה בשקט כל קובץ " +
                    "חדש למבנה מסודר של לקוח \\ שנה \\ סוג מסמך — חשבוניות, חוזים, סריקות וכל השאר, ממוינים " +
                    "ברגע שהם נוחתים." },
                { "bullet_ocr", "מזהה לקוחות משם הקובץ וגם מתוך תוכן המסמך עצמו, בעזרת OCR מקומי (עברית + אנגלית) — בלי ענן, בלי העלאה." },
                { "bullet_dupes", "מזהה קבצים כפולים לפי hash תוכן ושומרת יומן פעילות מלא, עם ביטול בלחיצה אחת להעברה האחרונה." },
                { "bullet_defender", "סורקת כל קובץ עם Windows Defender לפני תיוק, כדי שכלום לא בטוח לא ייכנס לארכיון שלך." },
                { "bullet_privacy", "עיבוד מקומי ב-100%. Filio אף פעם לא מוחקת קובץ לחלוטין ואף פעם לא שולחת את הקבצים שלך או תוכנם לשום מקום." },
                { "btn_next", "הבא" },
                { "btn_back", "הקודם" },
                { "btn_cancel", "ביטול" },
                { "license_title", "הסכם רישיון" },
                { "license_desc", "יש לקרוא את הסכם הרישיון הבא לפני שממשיכים." },
                { "license_agree", "אני מסכים/ה לתנאי הסכם הרישיון" },
                { "location_title", "בחירת מיקום התקנה" },
                { "desc_same_version", "הגרסה המותקנת ({0}) כבר עדכנית. ניתן להתקין מחדש בכל זאת כדי לתקן קבצים." },
                { "btn_reinstall", "התקן מחדש" },
                { "desc_update", "התגלתה התקנה קיימת (גרסה {0}). ההתקנה תעדכן אותה לגרסה {1} במקום — ההגדרות ויומן הפעילות נשמרים." },
                { "btn_update", "עדכן" },
                { "desc_fresh", "{0} גרסה {1} תותקן." },
                { "btn_install", "התקן" },
                { "lbl_located_at", "ממוקם ב:" },
                { "lbl_will_install_to", "יותקן אל:" },
                { "btn_browse", "עיון..." },
                { "chk_desktop_shortcut", "צור קיצור בשולחן העבודה" },
                { "chk_startmenu_shortcut", "צור קיצור בתפריט Start" },
                { "chk_autostart", "הפעלה אוטומטית עם הפעלת Windows" },
                { "note_admin", "Filio מותקנת ב-Program Files עבור כל המשתמשים ודורשת הרשאות מנהל (UAC) לשם כך." },
                { "lbl_installing", "מתקין..." },
                { "title_update_complete", "העדכון הושלם" },
                { "title_install_complete", "ההתקנה הושלמה" },
                { "desc_finish", "{0} גרסה {1} מותקנת ומוכנה לשימוש. ניתן להסיר בכל עת מ'אפליקציות' של Windows." },
                { "chk_launch_now", "הפעל את {0} עכשיו" },
                { "btn_finish", "סיום" },
                { "app_running_title", "התקנת Filio" },
                { "app_running_msg", "Filio רצה כרגע.\n\nיש לסגור אותה לפני שממשיכים בהתקנה." },
                { "browse_folder_title", "בחר תיקיית התקנה" },
                { "footer_copyright", "© 2026 אופיר שודרי — כל הזכויות שמורות" },
                { "install_error_title", "שגיאת התקנה" },
                { "install_error_body", "שגיאת התקנה:\n{0}" },
                { "uninstall_confirm_title", "הסרת Filio" },
                { "uninstall_confirm_body", "להסיר את Filio {0} מהמחשב הזה?" },
                { "uninstall_delete_data_title", "למחוק את הנתונים של Filio?" },
                { "uninstall_delete_data_body", "למחוק גם את ההגדרות ויומן הפעילות של Filio? (לא ניתן לבטל פעולה זו.)" },
                { "uninstall_done_title", "ההסרה הושלמה" },
                { "uninstall_done_body", "Filio הוסרה מהמחשב הזה." },
            };

            T.Add("en", en);
            T.Add("he", he);
        }

        public static string S(string key)
        {
            Dictionary<string, string> dict;
            if (T.TryGetValue(Lang, out dict) && dict.ContainsKey(key)) return dict[key];
            if (T.TryGetValue("en", out dict) && dict.ContainsKey(key)) return dict[key];
            return key;
        }

        public static string F(string key, params object[] args) => string.Format(S(key), args);
    }

    static class UiHelpers
    {
        public static void RoundCorners(Control c, int radius)
        {
            if (c.Width <= 0 || c.Height <= 0) return;
            var path = new GraphicsPath();
            int d = Math.Min(radius * 2, Math.Min(c.Width, c.Height));
            path.AddArc(0, 0, d, d, 180, 90);
            path.AddArc(c.Width - d, 0, d, d, 270, 90);
            path.AddArc(c.Width - d, c.Height - d, d, d, 0, 90);
            path.AddArc(0, c.Height - d, d, d, 90, 90);
            path.CloseFigure();
            c.Region = new Region(path);
        }

        public static GraphicsPath RoundedRectPath(Rectangle bounds, int radius)
        {
            int d = Math.Max(1, radius * 2);
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(Math.Max(bounds.X, bounds.Right - d), bounds.Y, d, d, 270, 90);
            path.AddArc(Math.Max(bounds.X, bounds.Right - d), Math.Max(bounds.Y, bounds.Bottom - d), d, d, 0, 90);
            path.AddArc(bounds.X, Math.Max(bounds.Y, bounds.Bottom - d), d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // Primary (accent/orange) or secondary (panel) flat button — every state (normal,
        // hover, pressed, disabled) gets an EXPLICIT color below. FlatAppearance also has its
        // own MouseOverBackColor/MouseDownBackColor which WinForms uses on top of our MouseEnter/
        // MouseLeave handlers; setting all three keeps hover/pressed from ever silently
        // reverting to the OS's default flat-button gray.
        public static Button MakeButton(string text, Color back, Color fore, int width, bool primary)
        {
            var btn = new Button
            {
                Text = text,
                Width = width,
                Height = 38,
                FlatStyle = FlatStyle.Flat,
                BackColor = back,
                ForeColor = fore,
                Font = new Font("Segoe UI", 10f),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
            };
            btn.FlatAppearance.BorderSize = 0;
            var hover = primary ? ControlPaint.Light(back, 0.18f) : ControlPaint.Light(back, 0.12f);
            var pressed = ControlPaint.Dark(back, 0.10f);
            btn.FlatAppearance.MouseOverBackColor = hover;
            btn.FlatAppearance.MouseDownBackColor = pressed;
            btn.EnabledChanged += (s, e) =>
            {
                btn.BackColor = btn.Enabled ? back : Theme.Panel2;
                btn.ForeColor = btn.Enabled ? fore : Theme.TextMuted;
            };
            btn.Resize += (s, e) => RoundCorners(btn, 8);
            RoundCorners(btn, 8);
            return btn;
        }

        // The real, multi-resolution app icon (assets/filio.ico) is embedded as this exe's own
        // /win32icon at build time (ApplicationIcon in Setup.csproj), so we can just read it
        // back off the running assembly — one source of truth, no drift between the installer's
        // icon and the app's icon.
        public static Icon GetBrandIcon()
        {
            try
            {
                var icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
                if (icon != null) return icon;
            }
            catch { }
            return SystemIcons.Application;
        }
    }

    // Native CheckBox always paints its box with the OS visual style (a plain white/gray square)
    // regardless of the parent's BackColor — that mismatch is exactly the "reads as a themed
    // shell around native controls" failure mode flagged for this build (the SnapCap bug this
    // task calls out). Owner-drawn replacement using Theme colors explicitly, every state.
    public class ThemedCheckBox : CheckBox
    {
        public ThemedCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            AutoSize = false;
            Height = 24;
            Cursor = Cursors.Hand;
            ForeColor = Theme.Text;
        }

        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Bg))
                g.FillRectangle(bg, ClientRectangle);

            const int box = 17;
            var boxRect = new Rectangle(0, (Height - box) / 2, box, box);
            Color boxFill = !Enabled ? Theme.Panel2 : (Checked ? Theme.Primary : Theme.Panel2);
            Color boxBorder = !Enabled ? Theme.Border : (Checked ? Theme.Primary : Theme.TextMuted);
            using (var path = UiHelpers.RoundedRectPath(boxRect, 4))
            {
                using (var fill = new SolidBrush(boxFill)) g.FillPath(fill, path);
                using (var pen = new Pen(boxBorder, 1.4f)) g.DrawPath(pen, path);
            }
            if (Checked)
            {
                using (var checkPen = new Pen(Color.White, 1.9f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    g.DrawLines(checkPen, new[]
                    {
                        new Point(boxRect.Left + 3, boxRect.Top + 9),
                        new Point(boxRect.Left + 7, boxRect.Top + 12),
                        new Point(boxRect.Left + 13, boxRect.Top + 4)
                    });
                }
            }

            var textColor = Enabled ? Theme.Text : Theme.TextMuted;
            var textRect = new Rectangle(box + 8, 0, Math.Max(0, Width - box - 8), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, textColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.WordEllipsis);
        }
    }

    // Native ProgressBarStyle.Marquee is always OS theme-blue with square corners no matter the
    // form's palette. Own-drawn indeterminate bar in Filio's brand accent instead.
    public class ThemedProgressBar : Control
    {
        private readonly System.Windows.Forms.Timer _timer;
        private float _pos = -0.35f;

        public ThemedProgressBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Height = 8;
            _timer = new System.Windows.Forms.Timer { Interval = 16 };
            _timer.Tick += (s, e) =>
            {
                _pos += 0.012f;
                if (_pos > 1.35f) _pos = -0.35f;
                Invalidate();
            };
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); _timer.Start(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var track = new Rectangle(0, 0, Width, Height);
            int radius = Height / 2;

            using (var trackPath = UiHelpers.RoundedRectPath(track, radius))
            using (var trackBrush = new SolidBrush(Theme.Panel2))
                g.FillPath(trackBrush, trackPath);

            int barWidth = Math.Max(30, Width / 4);
            int x = (int)(_pos * (Width + barWidth)) - barWidth / 2;
            var barRect = new Rectangle(x, 0, barWidth, Height);

            var oldClip = g.Clip;
            using (var clipPath = UiHelpers.RoundedRectPath(track, radius))
            {
                g.SetClip(clipPath, CombineMode.Replace);
                using (var barPath = UiHelpers.RoundedRectPath(barRect, radius))
                using (var barBrush = new SolidBrush(Theme.Accent))
                    g.FillPath(barBrush, barPath);
            }
            g.Clip = oldClip;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Parses the Inno-Setup-style silent-install switches (kept for familiarity —
    /// this installer is a hand-rolled WinForms wizard, not Inno Setup) that UpdateService.
    /// LaunchSilentInstall passes. Pure/no I/O so RunSelfTest can exercise it headlessly.</summary>
    internal struct SilentInstallOptions
    {
        public bool Silent;
        public bool VerySilent;
        public bool SuppressMsgBoxes;
        public bool CloseApplications;
        public bool RestartApplications;
        public bool NoRestart;
        public string Lang;

        public static SilentInstallOptions Parse(string[] args)
        {
            var opt = new SilentInstallOptions { Lang = "en" };
            foreach (var raw in args)
            {
                var a = (raw ?? "").Trim();
                if (a.Equals("/VERYSILENT", StringComparison.OrdinalIgnoreCase)) { opt.Silent = true; opt.VerySilent = true; }
                else if (a.Equals("/SILENT", StringComparison.OrdinalIgnoreCase)) { opt.Silent = true; }
                else if (a.Equals("/SUPPRESSMSGBOXES", StringComparison.OrdinalIgnoreCase)) opt.SuppressMsgBoxes = true;
                else if (a.Equals("/CLOSEAPPLICATIONS", StringComparison.OrdinalIgnoreCase)) opt.CloseApplications = true;
                else if (a.Equals("/RESTARTAPPLICATIONS", StringComparison.OrdinalIgnoreCase)) opt.RestartApplications = true;
                else if (a.Equals("/NORESTART", StringComparison.OrdinalIgnoreCase)) opt.NoRestart = true;
                else if (a.StartsWith("/LANG=", StringComparison.OrdinalIgnoreCase))
                {
                    var v = a.Substring("/LANG=".Length).Trim().ToLowerInvariant();
                    opt.Lang = v == "hebrew" || v == "he" ? "he" : "en";
                }
            }
            return opt;
        }
    }

    /// <summary>Process exit codes for the /SILENT and /VERYSILENT unattended-install path
    /// (Program.RunSilentInstall). UpdateService's caller (App/MainWindow) never actually
    /// observes these directly — the running Filio process has already exited by the time the
    /// installer finishes — but they matter for a scripted/enterprise deploy invoking the
    /// installer directly, and they drive whether RunSilentInstall writes the update-failed
    /// marker that the NEXT Filio launch reads back.</summary>
    internal static class SetupExitCodes
    {
        public const int Success = 0;
        public const int GenericError = 1;
        public const int AppStillRunning = 2;
        public const int ElevationDeclined = 3;
    }

    public class SetupForm : Form
    {
        public const string AppName = "Filio";
        public static readonly string AppVersion =
            Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        public const string ExeFileName = "Filio.exe";
        public const string ShortcutFileName = "Filio.lnk";
        private const string InstallDirName = "Filio";
        private const string PayloadResourceName = "FilioSetup.payload.zip";
        private const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + InstallDirName;
        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        // Same mutex name as App.xaml.cs's SingleInstanceMutexName (and the old setup.iss's
        // AppMutex) — this is the one thing that has to keep matching between the app and
        // whichever installer technology wraps it.
        public const string SingleInstanceMutexName = @"Global\Filio-SingleInstance-7C2A9E1D";

        private Panel _pageLanguage, _pageWelcome, _pageLicense, _pageLocation, _pageProgress, _pageFinish;
        private Label _footer, _headerSub;
        private ThemedProgressBar _progressBar;
        private ThemedCheckBox _chkDesktop, _chkStartMenu, _chkAutostart, _chkLaunch, _chkLicenseAgree;
        private Button _btnLicenseNext, _btnLocationInstall;
        private string _installDir;
        private bool _alreadyInstalled;
        private string _existingVersion;
        private string _selectedLanguage = "en";

        // Test-only hook (see Program.RunSelfTest) so the self-test can exercise the Hebrew/RTL
        // page-building code path without going through the language-picker button click.
        internal static string InitialLanguageOverrideForTests = null;

        public SetupForm()
        {
            if (InitialLanguageOverrideForTests != null) _selectedLanguage = InitialLanguageOverrideForTests;
            DetectExistingInstall();

            ClientSize = new Size(620, 560);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            Icon = UiHelpers.GetBrandIcon();
            Font = new Font("Segoe UI", 9.5f);

            Controls.Add(BuildHeader());

            _footer = new Label
            {
                ForeColor = Theme.TextMuted,
                BackColor = Theme.Bg,
                Dock = DockStyle.Bottom,
                TextAlign = ContentAlignment.MiddleCenter,
                Height = 24,
                Font = new Font("Segoe UI", 8f),
            };
            Controls.Add(_footer);

            _pageLanguage = BuildLanguagePage();
            Controls.Add(_pageLanguage);
            RebuildLocalizedPages();
            ShowPage(_pageLanguage);
        }

        private void RebuildLocalizedPages()
        {
            Loc.Lang = _selectedLanguage;
            RightToLeft = _selectedLanguage == "he" ? RightToLeft.Yes : RightToLeft.No;
            RightToLeftLayout = _selectedLanguage == "he";
            Text = (_alreadyInstalled ? Loc.S("title_update") : Loc.S("title_install")) + AppName;
            if (_footer != null) _footer.Text = Loc.S("footer_copyright");
            if (_headerSub != null) _headerSub.Text = Loc.S("header_sub");

            foreach (var p in new[] { _pageWelcome, _pageLicense, _pageLocation, _pageProgress, _pageFinish })
                if (p != null) Controls.Remove(p);

            _pageWelcome = BuildWelcomePage();
            _pageLicense = BuildLicensePage();
            _pageLocation = BuildLocationPage();
            _pageProgress = BuildProgressPage();
            _pageFinish = BuildFinishPage();
            Controls.Add(_pageWelcome);
            Controls.Add(_pageLicense);
            Controls.Add(_pageLocation);
            Controls.Add(_pageProgress);
            Controls.Add(_pageFinish);
            foreach (var p in new[] { _pageWelcome, _pageLicense, _pageLocation, _pageProgress, _pageFinish })
                p.Visible = false;
        }

        private static string DefaultInstallDir() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), InstallDirName);

        private void DetectExistingInstall()
        {
            _installDir = DefaultInstallDir();
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(UninstallKeyPath))
                {
                    if (key != null)
                    {
                        _existingVersion = key.GetValue("DisplayVersion") as string;
                        var existingDir = key.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(existingDir)) _installDir = existingDir;
                        _alreadyInstalled = true;
                    }
                }
            }
            catch { }
        }

        private Control BuildHeader()
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 82, BackColor = Theme.HeaderBg };
            var logo = new PictureBox
            {
                Image = UiHelpers.GetBrandIcon().ToBitmap(),
                Size = new Size(44, 44),
                Location = new Point(22, 19),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
            };
            var title = new Label { Text = AppName, ForeColor = Theme.HeaderText, BackColor = Theme.HeaderBg, Font = new Font("Segoe UI", 16f, FontStyle.Bold), AutoSize = true, Location = new Point(78, 14) };
            _headerSub = new Label { Text = Loc.S("header_sub"), ForeColor = Theme.HeaderSub, BackColor = Theme.HeaderBg, Font = new Font("Segoe UI", 9f), AutoSize = true, Location = new Point(78, 46) };
            panel.Controls.Add(logo);
            panel.Controls.Add(title);
            panel.Controls.Add(_headerSub);
            return panel;
        }

        private Panel NewPage()
        {
            return new Panel { Location = new Point(0, 82), Size = new Size(620, 560 - 82 - 24), BackColor = Theme.Bg };
        }

        private Panel BuildLanguagePage()
        {
            var p = NewPage();
            var title = new Label { Text = "Select Language / בחירת שפה", Font = new Font("Segoe UI", 14f, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Theme.Bg, Location = new Point(28, 34), AutoSize = true };
            var desc = new Label { Text = "Choose your preferred language for the installer.\nבחר/י את השפה המועדפת עבור תוכנת ההתקנה.", Location = new Point(28, 74), Size = new Size(560, 44), ForeColor = Theme.TextMuted, BackColor = Theme.Bg };
            p.Controls.Add(title);
            p.Controls.Add(desc);

            var btnEnglish = UiHelpers.MakeButton("English", Theme.Accent, Color.White, 180, true);
            btnEnglish.Location = new Point(28, 140);
            btnEnglish.Click += (s, e) => { _selectedLanguage = "en"; RebuildLocalizedPages(); ShowPage(_pageWelcome); };
            p.Controls.Add(btnEnglish);

            var btnHebrew = UiHelpers.MakeButton("עברית", Theme.Accent, Color.White, 180, true);
            btnHebrew.Location = new Point(28, 190);
            btnHebrew.Click += (s, e) => { _selectedLanguage = "he"; RebuildLocalizedPages(); ShowPage(_pageWelcome); };
            p.Controls.Add(btnHebrew);

            return p;
        }

        private Panel BuildWelcomePage()
        {
            var p = NewPage();
            bool rtl = _selectedLanguage == "he";
            var align = rtl ? ContentAlignment.TopRight : ContentAlignment.TopLeft;

            var title = new Label { Text = Loc.S("welcome_title"), Font = new Font("Segoe UI", 14f, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Theme.Bg, Location = new Point(28, 24), Size = new Size(560, 50), TextAlign = align };
            p.Controls.Add(title);

            var body = new Label { Text = Loc.S("welcome_body"), Location = new Point(28, 78), Size = new Size(560, 70), ForeColor = Theme.Text, BackColor = Theme.Bg, TextAlign = align, Font = new Font("Segoe UI", 9.5f) };
            p.Controls.Add(body);

            string[] bullets = { Loc.S("bullet_ocr"), Loc.S("bullet_dupes"), Loc.S("bullet_defender"), Loc.S("bullet_privacy") };
            int by = 160;
            foreach (var b in bullets)
            {
                var dot = new Label { Text = "●", ForeColor = Theme.Accent, BackColor = Theme.Bg, Location = new Point(28, by + 2), AutoSize = true, Font = new Font("Segoe UI", 8f) };
                var lbl = new Label { Text = b, ForeColor = Theme.TextMuted, BackColor = Theme.Bg, Location = new Point(50, by), Size = new Size(540, 40), TextAlign = align, Font = new Font("Segoe UI", 9f) };
                p.Controls.Add(dot);
                p.Controls.Add(lbl);
                by += 46;
            }

            var btnNext = UiHelpers.MakeButton(Loc.S("btn_next"), Theme.Accent, Color.White, 130, true);
            btnNext.Location = new Point(28, 400);
            btnNext.Click += (s, e) => ShowPage(_pageLicense);
            p.Controls.Add(btnNext);

            var btnCancel = UiHelpers.MakeButton(Loc.S("btn_cancel"), Theme.Panel2, Theme.Text, 110, false);
            btnCancel.Location = new Point(166, 400);
            btnCancel.Click += (s, e) => Close();
            p.Controls.Add(btnCancel);

            return p;
        }

        private Panel BuildLicensePage()
        {
            var p = NewPage();
            bool rtl = _selectedLanguage == "he";
            var align = rtl ? ContentAlignment.TopRight : ContentAlignment.TopLeft;

            var title = new Label { Text = Loc.S("license_title"), Font = new Font("Segoe UI", 13f, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Theme.Bg, Location = new Point(28, 20), AutoSize = true };
            var desc = new Label { Text = Loc.S("license_desc"), Location = new Point(28, 50), Size = new Size(560, 22), ForeColor = Theme.TextMuted, BackColor = Theme.Bg, TextAlign = align };
            p.Controls.Add(title);
            p.Controls.Add(desc);

            var box = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Location = new Point(28, 80),
                Size = new Size(560, 300),
                BackColor = Theme.Panel,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9f),
                Text = LoadEulaText(_selectedLanguage),
                RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No,
            };
            p.Controls.Add(box);

            _chkLicenseAgree = new ThemedCheckBox { Text = Loc.S("license_agree"), Checked = false, Location = new Point(28, 392), Width = 500 };
            _chkLicenseAgree.CheckedChanged += (s, e) => { if (_btnLicenseNext != null) _btnLicenseNext.Enabled = _chkLicenseAgree.Checked; };
            p.Controls.Add(_chkLicenseAgree);

            _btnLicenseNext = UiHelpers.MakeButton(Loc.S("btn_next"), Theme.Accent, Color.White, 130, true);
            _btnLicenseNext.Location = new Point(28, 428);
            _btnLicenseNext.Enabled = false;
            _btnLicenseNext.Click += (s, e) => ShowPage(_pageLocation);
            p.Controls.Add(_btnLicenseNext);

            var btnBack = UiHelpers.MakeButton(Loc.S("btn_back"), Theme.Panel2, Theme.Text, 110, false);
            btnBack.Location = new Point(166, 428);
            btnBack.Click += (s, e) => ShowPage(_pageWelcome);
            p.Controls.Add(btnBack);

            return p;
        }

        private static string LoadEulaText(string lang)
        {
            var resourceName = lang == "he" ? "FilioSetup.EULA_he.txt" : "FilioSetup.EULA_en.txt";
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return "";
                    using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8))
                        return reader.ReadToEnd();
                }
            }
            catch { return ""; }
        }

        private Panel BuildLocationPage()
        {
            var p = NewPage();
            bool rtl = _selectedLanguage == "he";
            var align = rtl ? ContentAlignment.TopRight : ContentAlignment.TopLeft;

            string descText, buttonText;
            bool sameVersion = _alreadyInstalled && _existingVersion == AppVersion;
            if (sameVersion) { descText = Loc.F("desc_same_version", _existingVersion); buttonText = Loc.S("btn_reinstall"); }
            else if (_alreadyInstalled) { descText = Loc.F("desc_update", _existingVersion ?? "?", AppVersion); buttonText = Loc.S("btn_update"); }
            else { descText = Loc.F("desc_fresh", AppName, AppVersion); buttonText = Loc.S("btn_install"); }

            var title = new Label { Text = Loc.S("location_title"), Font = new Font("Segoe UI", 13f, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Theme.Bg, Location = new Point(28, 20), AutoSize = true };
            p.Controls.Add(title);

            var desc = new Label { Text = descText, Location = new Point(28, 54), Size = new Size(560, 40), ForeColor = Theme.Text, BackColor = Theme.Bg, TextAlign = align };
            p.Controls.Add(desc);

            var lblPath = new Label { Text = (_alreadyInstalled ? Loc.S("lbl_located_at") : Loc.S("lbl_will_install_to")), Location = new Point(28, 104), AutoSize = true, ForeColor = Theme.Text, BackColor = Theme.Bg };
            p.Controls.Add(lblPath);
            var txtPath = new TextBox { Text = _installDir, Location = new Point(28, 127), Width = 470, ReadOnly = true, BackColor = Theme.Panel2, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
            p.Controls.Add(txtPath);

            if (!_alreadyInstalled)
            {
                var btnBrowse = UiHelpers.MakeButton(Loc.S("btn_browse"), Theme.Panel2, Theme.Text, 90, false);
                btnBrowse.Location = new Point(504, 125);
                btnBrowse.Height = 28;
                btnBrowse.Click += (s, e) =>
                {
                    using (var dlg = new FolderBrowserDialog { Description = Loc.S("browse_folder_title"), SelectedPath = _installDir })
                    {
                        if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
                        {
                            _installDir = Path.Combine(dlg.SelectedPath, InstallDirName);
                            txtPath.Text = _installDir;
                        }
                    }
                };
                p.Controls.Add(btnBrowse);
            }

            _chkDesktop = new ThemedCheckBox { Text = Loc.S("chk_desktop_shortcut"), Checked = true, Location = new Point(28, 172), Width = 500 };
            _chkStartMenu = new ThemedCheckBox { Text = Loc.S("chk_startmenu_shortcut"), Checked = true, Location = new Point(28, 198), Width = 500 };
            _chkAutostart = new ThemedCheckBox { Text = Loc.S("chk_autostart"), Checked = true, Location = new Point(28, 224), Width = 500 };
            p.Controls.Add(_chkDesktop);
            p.Controls.Add(_chkStartMenu);
            p.Controls.Add(_chkAutostart);

            var note = new Label { Text = Loc.S("note_admin"), Location = new Point(28, 262), Size = new Size(560, 36), ForeColor = Theme.TextMuted, BackColor = Theme.Bg, TextAlign = align };
            p.Controls.Add(note);

            _btnLocationInstall = UiHelpers.MakeButton(buttonText, Theme.Accent, Color.White, 140, true);
            _btnLocationInstall.Location = new Point(28, 320);
            _btnLocationInstall.Click += async (s, e) => await DoInstall();
            p.Controls.Add(_btnLocationInstall);

            var btnBack = UiHelpers.MakeButton(Loc.S("btn_back"), Theme.Panel2, Theme.Text, 110, false);
            btnBack.Location = new Point(176, 320);
            btnBack.Click += (s, e) => ShowPage(_pageLicense);
            p.Controls.Add(btnBack);

            var btnCancel = UiHelpers.MakeButton(Loc.S("btn_cancel"), Theme.Panel2, Theme.Text, 110, false);
            btnCancel.Location = new Point(296, 320);
            btnCancel.Click += (s, e) => Close();
            p.Controls.Add(btnCancel);

            return p;
        }

        private Panel BuildProgressPage()
        {
            var p = NewPage();
            var label = new Label { Text = Loc.S("lbl_installing"), Location = new Point(28, 190), AutoSize = true, ForeColor = Theme.Text, BackColor = Theme.Bg, Font = new Font("Segoe UI", 10f) };
            _progressBar = new ThemedProgressBar { Location = new Point(28, 224), Size = new Size(560, 8) };
            p.Controls.Add(label);
            p.Controls.Add(_progressBar);
            return p;
        }

        private Panel BuildFinishPage()
        {
            var p = NewPage();
            bool rtl = _selectedLanguage == "he";
            var align = rtl ? ContentAlignment.TopRight : ContentAlignment.TopLeft;

            var title = new Label { Text = _alreadyInstalled ? Loc.S("title_update_complete") : Loc.S("title_install_complete"), Font = new Font("Segoe UI", 14f, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Theme.Bg, Location = new Point(28, 40), AutoSize = true };
            var desc = new Label { Text = Loc.F("desc_finish", AppName, AppVersion), Location = new Point(28, 78), Size = new Size(560, 44), ForeColor = Theme.TextMuted, BackColor = Theme.Bg, TextAlign = align };
            _chkLaunch = new ThemedCheckBox { Text = Loc.F("chk_launch_now", AppName), Checked = true, Location = new Point(28, 136), Width = 500 };
            p.Controls.Add(title);
            p.Controls.Add(desc);
            p.Controls.Add(_chkLaunch);

            var btnFinish = UiHelpers.MakeButton(Loc.S("btn_finish"), Theme.Accent, Color.White, 140, true);
            btnFinish.Location = new Point(28, 190);
            btnFinish.Click += (s, e) =>
            {
                if (_chkLaunch.Checked)
                {
                    try { Process.Start(Path.Combine(_installDir, ExeFileName)); } catch { }
                }
                Close();
            };
            p.Controls.Add(btnFinish);
            return p;
        }

        private void ShowPage(Panel page)
        {
            foreach (var p in new[] { _pageLanguage, _pageWelcome, _pageLicense, _pageLocation, _pageProgress, _pageFinish })
                if (p != null) p.Visible = p == page;
        }

        /// <summary>Blocks (retry/cancel) until Filio isn't running — checks both the process
        /// name AND the named single-instance mutex (App.xaml.cs / SingleInstanceMutexName),
        /// since a renamed or side-loaded copy of the exe would still hold the mutex even if the
        /// process name looked different. Same two-step check the old setup.iss's AppMutex
        /// directive did implicitly via Inno's own mutex-aware install/update guard.</summary>
        private bool EnsureAppNotRunning()
        {
            while (IsFilioRunning())
            {
                var result = MessageBox.Show(this, Loc.S("app_running_msg"), Loc.S("app_running_title"), MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning);
                if (result != DialogResult.Retry) return false;
            }
            return true;
        }

        private static bool IsFilioRunning()
        {
            if (Process.GetProcessesByName("Filio").Length > 0) return true;
            Mutex existing;
            if (Mutex.TryOpenExisting(SingleInstanceMutexName, out existing))
            {
                existing.Dispose();
                return true;
            }
            return false;
        }

        private async System.Threading.Tasks.Task DoInstall()
        {
            if (!EnsureAppNotRunning()) return;

            ShowPage(_pageProgress);
            try
            {
                string installDir = _installDir;
                bool desktop = _chkDesktop.Checked;
                bool startMenu = _chkStartMenu.Checked;
                bool autostart = _chkAutostart.Checked;

                string exePath = await System.Threading.Tasks.Task.Run(() => PerformInstall(installDir, desktop, startMenu));

                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                    {
                        if (key != null)
                        {
                            if (autostart) key.SetValue("Filio", "\"" + exePath + "\" --minimized");
                            else key.DeleteValue("Filio", false);
                        }
                    }
                }
                catch { }

                ShowPage(_pageFinish);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Loc.F("install_error_body", ex.Message), Loc.S("install_error_title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                ShowPage(_pageLocation);
            }
        }

        /// <summary>Static, no-UI version of DetectExistingInstall for Program.RunSilentInstall
        /// (which never constructs a SetupForm at all, since that would build the whole wizard
        /// UI just to read two registry values).</summary>
        public static (bool installed, string installDir, string? version) DetectExistingInstallStatic()
        {
            string dir = DefaultInstallDir();
            bool installed = false;
            string? version = null;
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(UninstallKeyPath))
                {
                    if (key != null)
                    {
                        version = key.GetValue("DisplayVersion") as string;
                        var existingDir = key.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(existingDir)) dir = existingDir;
                        installed = true;
                    }
                }
            }
            catch { }
            return (installed, dir, version);
        }

        public static bool IsAppRunningStatic() => IsFilioRunning();

        /// <summary>Best-effort close of a running Filio for /CLOSEAPPLICATIONS: tries a graceful
        /// WM_CLOSE first (lets it save state / flush the activity log), then escalates to Kill()
        /// for a tray-minimized instance with no visible main window CloseMainWindow can't reach.
        /// Returns false if Filio is still running when the timeout elapses - RunSilentInstall
        /// treats that as a hard failure rather than trying to overwrite a locked exe.</summary>
        public static bool TryCloseRunningApp(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;

            foreach (var proc in Process.GetProcessesByName("Filio"))
            {
                try { proc.CloseMainWindow(); } catch { }
                finally { proc.Dispose(); }
            }

            while (IsFilioRunning() && DateTime.UtcNow < deadline)
                Thread.Sleep(250);

            if (!IsFilioRunning()) return true;

            foreach (var proc in Process.GetProcessesByName("Filio"))
            {
                try { proc.Kill(); proc.WaitForExit(3000); } catch { }
                finally { proc.Dispose(); }
            }

            return !IsFilioRunning();
        }

        public static bool GetAutostartEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                    return key?.GetValue("Filio") != null;
            }
            catch { return false; }
        }

        public static void SetAutostart(bool enabled, string exePath)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null) return;
                    if (enabled) key.SetValue("Filio", "\"" + exePath + "\" --minimized");
                    else key.DeleteValue("Filio", false);
                }
            }
            catch { }
        }

        // No UI dependency, so both the graphical wizard above and Program's --selftest /
        // silent paths use the exact same install logic.
        public static string PerformInstall(string installDir, bool desktopShortcut, bool startMenuShortcut)
        {
            Directory.CreateDirectory(installDir);
            ExtractPayload(installDir);

            string exePath = Path.Combine(installDir, ExeFileName);

            if (desktopShortcut)
                CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutFileName), exePath, installDir);
            if (startMenuShortcut)
            {
                string startMenuDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
                Directory.CreateDirectory(startMenuDir);
                CreateShortcut(Path.Combine(startMenuDir, ShortcutFileName), exePath, installDir);
            }

            RegisterUninstall(installDir, exePath);
            return exePath;
        }

        private static void ExtractPayload(string destDir)
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var stream = asm.GetManifestResourceStream(PayloadResourceName))
            {
                if (stream == null) throw new Exception("Payload resource not found: " + PayloadResourceName);
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    foreach (var entry in archive.Entries)
                    {
                        string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                        if (string.IsNullOrEmpty(entry.Name) && relative.EndsWith(Path.DirectorySeparatorChar.ToString()))
                        {
                            Directory.CreateDirectory(Path.Combine(destDir, relative));
                            continue;
                        }
                        string destPath = Path.Combine(destDir, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                        entry.ExtractToFile(destPath, true);
                    }
                }
            }
        }

        private static void CreateShortcut(string shortcutPath, string targetExe, string workingDir)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
            Type scType = shortcut.GetType();
            scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetExe });
            scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDir });
            scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { targetExe + ",0" });
            scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "Filio - automatic file organizer" });
            scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }

        // HKLM because installs are per-machine (Program Files) — matches the same decision
        // OptiGuard/AutoProcessTwin made portfolio-wide. The uninstall command points back at
        // THIS installer exe (copied into the install dir as Filio-Setup.exe) with a /uninstall
        // switch, so there is no separate unins000.exe binary to ship or keep in sync.
        private static void RegisterUninstall(string installDir, string exePath)
        {
            string setupExeInInstallDir = Path.Combine(installDir, "Filio-Setup.exe");
            try
            {
                string runningExe = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(runningExe) && File.Exists(runningExe))
                    File.Copy(runningExe, setupExeInInstallDir, true);
            }
            catch { }

            using (var key = Registry.LocalMachine.CreateSubKey(UninstallKeyPath))
            {
                key.SetValue("DisplayName", AppName);
                key.SetValue("Publisher", "Ofir Shudari");
                key.SetValue("DisplayVersion", AppVersion);
                key.SetValue("InstallLocation", installDir);
                key.SetValue("DisplayIcon", exePath);
                key.SetValue("UninstallString", "\"" + setupExeInInstallDir + "\" --uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", 153600, RegistryValueKind.DWord); // ~150MB, see SPEC.md system requirements
            }
        }

        // Driven by Program.Main's --uninstall path. Mirrors the CurUninstallStepChanged logic
        // that used to live in setup.iss's [Code] section: ask before deleting %APPDATA%\Filio
        // (settings + activity log), default answer "No" so data is never lost by accident.
        public static void RunUninstall()
        {
            string installDir = DefaultInstallDir();
            string version = AppVersion;
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(UninstallKeyPath))
                {
                    if (key != null)
                    {
                        var dir = key.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(dir)) installDir = dir;
                        var v = key.GetValue("DisplayVersion") as string;
                        if (!string.IsNullOrEmpty(v)) version = v;
                    }
                }
            }
            catch { }

            var confirm = MessageBox.Show(Loc.F("uninstall_confirm_body", version), Loc.S("uninstall_confirm_title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;

            while (IsFilioRunning())
            {
                var result = MessageBox.Show(Loc.S("app_running_msg"), Loc.S("app_running_title"), MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning);
                if (result != DialogResult.Retry) return;
            }

            try { File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutFileName)); } catch { }
            try { File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), ShortcutFileName)); } catch { }

            try
            {
                using (var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (runKey != null) runKey.DeleteValue("Filio", false);
                }
            }
            catch { }

            try { Registry.LocalMachine.DeleteSubKeyTree(UninstallKeyPath, false); } catch { }

            string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Filio");
            if (Directory.Exists(appDataPath))
            {
                var response = MessageBox.Show(Loc.S("uninstall_delete_data_body"), Loc.S("uninstall_delete_data_title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (response == DialogResult.Yes)
                {
                    try { Directory.Delete(appDataPath, true); } catch { }
                }
            }

            // Delete everything except this running Filio-Setup.exe copy (can't delete an
            // in-use file on Windows); schedule that one for deletion on next reboot instead.
            try
            {
                if (Directory.Exists(installDir))
                {
                    string selfPath = Assembly.GetExecutingAssembly().Location;
                    foreach (var file in Directory.GetFiles(installDir, "*", SearchOption.AllDirectories))
                    {
                        if (string.Equals(file, selfPath, StringComparison.OrdinalIgnoreCase)) continue;
                        try { File.Delete(file); } catch { }
                    }
                    foreach (var dir in Directory.GetDirectories(installDir))
                    {
                        try { Directory.Delete(dir, true); } catch { }
                    }
                    MoveFileEx(selfPath, null, MOVEFILE_DELAY_UNTIL_REBOOT);
                }
            }
            catch { }

            MessageBox.Show(Loc.S("uninstall_done_body"), Loc.S("uninstall_done_title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool MoveFileEx(string lpExistingFileName, string lpNewFileName, uint dwFlags);
        private const uint MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;
    }

    static class Program
    {
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);
        private const int ATTACH_PARENT_PROCESS = -1;

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Contains("--selftest"))
            {
                AttachConsole(ATTACH_PARENT_PROCESS);
                Environment.Exit(RunSelfTest());
                return;
            }

            // /SILENT and /VERYSILENT: the unattended path UpdateService.LaunchSilentInstall
            // uses for in-place self-update. Checked before --uninstall/the GUI wizard, and
            // routed through a wait-for-exit elevation (RelaunchElevatedAndWait) instead of the
            // fire-and-forget RelaunchElevated below, because a script/caller waiting on this
            // process's exit code needs the REAL result, not "0 because we returned immediately
            // after spawning a UAC prompt".
            var silentOptions = SilentInstallOptions.Parse(args);
            if (silentOptions.Silent)
            {
                if (!IsAdmin())
                {
                    Environment.Exit(RelaunchElevatedAndWait(args));
                    return;
                }
                Environment.Exit(RunSilentInstall(silentOptions));
                return;
            }

            if (args.Contains("--uninstall"))
            {
                if (!IsAdmin())
                {
                    RelaunchElevated(args);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                SetupForm.RunUninstall();
                return;
            }

            if (!IsAdmin())
            {
                RelaunchElevated(args);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }

        /// <summary>Runs the entire install with zero dialogs: installs to the existing location
        /// when Filio is already present (or the default Program Files location for a fresh
        /// install), preserves user settings/data (PerformInstall never touches %APPDATA%\Filio -
        /// only the install dir, shortcuts and registry), and preserves the existing autostart
        /// choice on an update rather than silently resetting it. Never shows a MessageBox or
        /// window; every failure is reported only via the process exit code and (for the cases a
        /// caller who already exited can't see, like the app-still-running guard) the
        /// update-failed marker file that Filio's own UpdateService reads back on next launch.</summary>
        private static int RunSilentInstall(SilentInstallOptions opt)
        {
            Loc.Lang = opt.Lang;
            try
            {
                var (alreadyInstalled, installDir, _) = SetupForm.DetectExistingInstallStatic();

                if (SetupForm.IsAppRunningStatic())
                {
                    bool closed = opt.CloseApplications && SetupForm.TryCloseRunningApp(TimeSpan.FromSeconds(20));
                    if (!closed)
                    {
                        WriteUpdateFailedMarker(opt.CloseApplications
                            ? "Filio was running and did not close in time for the silent update."
                            : "Filio was running; silent install requires /CLOSEAPPLICATIONS.");
                        return SetupExitCodes.AppStillRunning;
                    }
                }

                // Preserve the existing autostart choice on an update; default ON for a genuinely
                // fresh silent install (matches the GUI wizard's default-checked checkbox), since
                // there is no user present to ask either way.
                bool autostart = alreadyInstalled ? SetupForm.GetAutostartEnabled() : true;

                string exePath = SetupForm.PerformInstall(installDir, desktopShortcut: true, startMenuShortcut: true);
                SetupForm.SetAutostart(autostart, exePath);

                if (opt.RestartApplications)
                {
                    try { Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true }); }
                    catch { /* best-effort relaunch - install itself already succeeded */ }
                }

                return SetupExitCodes.Success;
            }
            catch (Exception ex)
            {
                WriteUpdateFailedMarker("Silent install failed: " + ex.GetType().Name + ": " + ex.Message);
                return SetupExitCodes.GenericError;
            }
        }

        /// <summary>Same path UpdateService.GetUpdateFailedMarkerPath() reads on Filio's next
        /// launch - kept as a literal path (not a shared constant) because Setup.cs is a
        /// separate project (build\installer\Setup.csproj) that intentionally has no reference
        /// to Filio.App, to keep the installer a single self-contained exe.</summary>
        private static void WriteUpdateFailedMarker(string message)
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Filio");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "update-failed.txt"), DateTime.UtcNow.ToString("u") + " " + message);
            }
            catch { /* best-effort - a missing marker just means the user isn't told WHY */ }
        }

        /// <summary>Elevation for the silent path: unlike RelaunchElevated (fire-and-forget, used
        /// by the interactive GUI/--uninstall paths), this BLOCKS until the elevated copy exits
        /// and propagates its real exit code, because a caller invoking Filio-Setup.exe /SILENT
        /// and checking the exit code needs the actual result. Returns ElevationDeclined if the
        /// user cancels the UAC prompt (Win32Exception 1223/ERROR_CANCELLED).</summary>
        private static int RelaunchElevatedAndWait(string[] args)
        {
            var psi = new ProcessStartInfo(Application.ExecutablePath) { Verb = "runas", UseShellExecute = true };
            if (args.Length > 0) psi.Arguments = string.Join(" ", args.Select(a => "\"" + a + "\""));
            try
            {
                using (var proc = Process.Start(psi))
                {
                    if (proc == null) return SetupExitCodes.GenericError;
                    proc.WaitForExit();
                    return proc.ExitCode;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return SetupExitCodes.ElevationDeclined;
            }
        }

        private static void RelaunchElevated(string[] args)
        {
            var psi = new ProcessStartInfo(Application.ExecutablePath) { Verb = "runas" };
            if (args.Length > 0) psi.Arguments = string.Join(" ", args.Select(a => "\"" + a + "\""));
            try { Process.Start(psi); } catch { /* user cancelled the UAC prompt */ }
        }

        private static bool IsAdmin()
        {
            using (var id = WindowsIdentity.GetCurrent())
            {
                var p = new WindowsPrincipal(id);
                return p.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        // Headless smoke test — no UAC, no window, no filesystem writes. Verifies the pieces
        // that would otherwise only be checkable by eye in a live GUI session: the payload/EULA
        // resources are embedded and well-formed, the brand icon resolves, and every wizard page
        // (including the localized ones) builds without throwing. This is NOT a substitute for
        // an interactive visual check — see the CHANGELOG/report note on that — but it does catch
        // "forgot to embed a resource" / "NullReferenceException while building page N" class
        // failures without a human present.
        private static int RunSelfTest()
        {
            int failures = 0;
            void Check(string name, Action action)
            {
                try { action(); Console.WriteLine("PASS  " + name); }
                catch (Exception ex) { Console.WriteLine("FAIL  " + name + "  -> " + ex.GetType().Name + ": " + ex.Message); failures++; }
            }

            Console.WriteLine("Filio-Setup self-test (build " + typeof(Program).Assembly.GetName().Version + ")");

            Check("payload.zip resource is embedded and openable", () =>
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream("FilioSetup.payload.zip"))
                {
                    if (stream == null) throw new Exception("resource missing");
                    using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                    {
                        if (!archive.Entries.Any(e => e.FullName.Equals("Filio.exe", StringComparison.OrdinalIgnoreCase)))
                            throw new Exception("payload.zip does not contain Filio.exe");
                    }
                }
            });

            Check("EULA_en.txt resource is embedded and non-empty", () =>
            {
                if (string.IsNullOrWhiteSpace(LoadEulaResource("FilioSetup.EULA_en.txt"))) throw new Exception("empty");
            });

            Check("EULA_he.txt resource is embedded and non-empty", () =>
            {
                if (string.IsNullOrWhiteSpace(LoadEulaResource("FilioSetup.EULA_he.txt"))) throw new Exception("empty");
            });

            Check("brand icon resolves from the running exe", () =>
            {
                var icon = UiHelpers.GetBrandIcon();
                if (icon == null) throw new Exception("null icon");
            });

            Check("wizard builds (English) without throwing", () =>
            {
                using (var form = new SetupForm())
                {
                    if (form.Controls.Count < 3) throw new Exception("form looks empty");
                }
            });

            Check("wizard builds (Hebrew/RTL) without throwing", () =>
            {
                SetupForm.InitialLanguageOverrideForTests = "he";
                try
                {
                    using (var form = new SetupForm())
                    {
                        if (form.Controls.Count < 3) throw new Exception("form looks empty");
                        if (form.RightToLeft != RightToLeft.Yes) throw new Exception("RTL not applied");
                    }
                }
                finally
                {
                    SetupForm.InitialLanguageOverrideForTests = null;
                    Loc.Lang = "en";
                }
            });

            Check("silent install args parse correctly (/VERYSILENT + /LANG=hebrew + flags)", () =>
            {
                var opt = SilentInstallOptions.Parse(new[]
                {
                    "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/LANG=hebrew", "/CLOSEAPPLICATIONS", "/RESTARTAPPLICATIONS"
                });
                if (!opt.Silent || !opt.VerySilent) throw new Exception("VERYSILENT should set Silent and VerySilent");
                if (opt.Lang != "he") throw new Exception("expected /LANG=hebrew to parse to \"he\", got \"" + opt.Lang + "\"");
                if (!opt.SuppressMsgBoxes || !opt.NoRestart || !opt.CloseApplications || !opt.RestartApplications)
                    throw new Exception("one or more flags were not parsed");
            });

            Check("silent install args default to English and plain /SILENT != /VERYSILENT", () =>
            {
                var opt = SilentInstallOptions.Parse(new[] { "/SILENT" });
                if (!opt.Silent) throw new Exception("expected Silent=true");
                if (opt.VerySilent) throw new Exception("/SILENT alone must not set VerySilent");
                if (opt.Lang != "en") throw new Exception("expected default language \"en\" when /LANG is omitted");
                if (opt.CloseApplications || opt.RestartApplications) throw new Exception("unset flags must default to false");
            });

            Check("non-silent args (e.g. GUI launch with no switches) leave Silent=false", () =>
            {
                var opt = SilentInstallOptions.Parse(Array.Empty<string>());
                if (opt.Silent || opt.VerySilent) throw new Exception("no args should never imply silent mode");
            });

            Console.WriteLine(failures == 0 ? "SELFTEST OK" : "SELFTEST FAILED (" + failures + " check(s))");
            return failures == 0 ? 0 : 1;
        }

        private static string LoadEulaResource(string name)
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var stream = asm.GetManifestResourceStream(name))
            {
                if (stream == null) return null;
                using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
            }
        }
    }
}
