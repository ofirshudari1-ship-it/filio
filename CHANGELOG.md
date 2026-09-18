# Filio — Changelog

**[English](#english) | [עברית](#עברית)**

---

## English

Full changelog in Keep a Changelog format (Added/Changed/Fixed/Removed). This is a condensed English summary of major releases — see the [Hebrew section](#עברית) below for complete per-change detail in the original language.

### 2.9.1 — 2026-09-18 — Project published to GitHub, automatic update notifications
- **Added**: Filio's source is now hosted at `github.com/ofirshudari1-ship-it/filio`, with
  tagged releases carrying the installer as a binary asset.
- **Added**: `UpdateService.CheckGitHubReleaseAsync` — a lightweight, notify-only check
  against the public GitHub Releases API (`GET /repos/.../releases/latest`), wired into
  `App.xaml.cs` to run once per session, a few seconds after startup, alongside the existing
  `UpdateFeedUrl` manifest flow. It never downloads or installs anything by itself — it only
  shows a tray notification (and, from Settings → Updates, an "open release page" button)
  when a newer tagged version is published. Fails silently on any network/DNS error so a
  flaky connection can never block or slow down startup.
- **Changed**: `website/index.html` — added a bilingual "Automatic updates" card to the
  Security & Privacy section explaining the once-per-session, notify-only GitHub check.

### 2.9.0 — 2026-09-17 — Installer engine replaced: Inno Setup → custom WinForms wizard
The 2.8.1 audit confirmed the Inno Setup installer was well-*branded* (gradient wizard bitmaps,
modern style), but branding bitmaps around Inno's wizard pages was never going to be enough: Inno
Setup pages always render their Next/Back/Cancel buttons, checkboxes and progress bar with native
Windows chrome, no matter what images surround them. That no longer meets the bar for this
portfolio's installers (see OptiGuard's and AutoProcessTwin's installers, which set the standard).
So the installer technology itself changed, not just its skin:
- **Removed**: `build/installer/setup.iss` is no longer built by `build.ps1` (left in place as
  unused legacy alongside `wizard_large.bmp`/`wizard_small.bmp`/`EULA_*.txt` — the EULA text files
  are still used, now embedded directly into the new installer instead of referenced by Inno).
- **Added**: `build/installer/Setup.cs` + `Setup.csproj` — a hand-built C#/WinForms installer
  (net48, ~79MB self-contained payload embedded as a single zip resource) with every control
  explicitly colored from `assets/BRAND.md`'s dark palette (`#0D1A18` background, `#1E7F72`
  primary, `#DB8A1F` accent) — flat buttons with explicit hover/pressed/disabled states, and
  owner-drawn `ThemedCheckBox`/`ThemedProgressBar` classes so the checkboxes and progress bar
  don't silently fall back to native OS chrome the way a plain `CheckBox`/`ProgressBar` would
  (WinForms never cascades a form's background color down into those two controls' own painting -
  the same bug class just found and fixed in SnapCap's installer).
- **Added**: a genuine 6-page wizard flow — language picker (English/Hebrew) → welcome page with
  real marketing copy about what Filio does (client/year/type auto-filing, local OCR, duplicate
  detection, pre-filing Defender scan) → a dedicated license/EULA page with a scrollable license
  text and a required "I accept" checkbox → install-location page (Program Files default, Browse
  button, Desktop/Start Menu/autostart checkboxes) → progress → finish, all mirrored for Hebrew
  RTL the same way OptiGuard/AutoProcessTwin's installers do it (`RightToLeftLayout`, not manual
  per-control repositioning).
- **Added**: the installer doubles as its own uninstaller. `RegisterUninstall` copies itself into
  the install directory as `Filio-Setup.exe` and points the registry `UninstallString` at
  `Filio-Setup.exe --uninstall`, so there's no separate `unins000.exe` binary to ship or keep in
  sync — `SetupForm.RunUninstall()` removes shortcuts, the autostart Run-key entry, the Uninstall
  registry key and the install directory, asking first (default "No") before deleting
  `%APPDATA%\Filio` settings/activity-log data, same as `setup.iss`'s old `CurUninstallStepChanged`
  did.
- **Added**: `Filio-Setup.exe --selftest` — a headless, non-elevated smoke test (no UAC, no
  filesystem writes) that verifies the embedded payload zip contains `Filio.exe`, both EULA
  resources are present and non-empty, the brand icon resolves, and both the English and
  Hebrew/RTL wizard page sets build without throwing. `build.ps1` now runs this automatically as
  the last build step and fails the build if it doesn't pass. This is real but partial coverage:
  it catches "forgot to embed a resource" / "exception building page N" failures, but it is **not**
  a substitute for an interactive visual check of button colors, contrast, hover states and RTL
  mirroring on a live Windows GUI session — that has not been done for this release; see the
  wrap-up report for what specifically still needs eyes-on verification.
- **Ported**: the same single-instance check the old `AppMutex=Global\Filio-SingleInstance-7C2A9E1D`
  Inno directive did — `EnsureAppNotRunning`/`IsFilioRunning` check both the `Filio` process name
  and `Mutex.TryOpenExisting` on that exact mutex name (same constant as
  `App.xaml.cs`'s `SingleInstanceMutexName`) before install, update or uninstall, and prompt
  Retry/Cancel if Filio is still running instead of failing partway through on a locked exe.
- **Changed**: `build.ps1`'s step 3 now runs `dotnet build build\installer\Setup.csproj` instead
  of invoking Inno Setup's `ISCC.exe` — no external tool install required anymore, and step 4 runs
  the new `--selftest` smoke check before declaring the build done.
- No app code changes in this release — `app/Filio.App` is unaffected, all 99 existing tests still
  pass (`dotnet test`, unchanged).

### 2.8.1 — 2026-09-17 — Installer visual polish audit
No app code changes. Audited the Inno Setup installer against 2026 "real, professionally-published
installer" expectations and confirmed it already meets them from an earlier pass:
- Confirmed `WizardStyle=modern` (Inno Setup 6 flat wizard look).
- Confirmed the wizard bitmaps (`build/installer/wizard_large.bmp` 164×314, `wizard_small.bmp`
  55×58) are genuinely brand-composited — sampled pixels match the documented `#0F5348` → `#1E7F72`
  gradient with the `#DB8A1F` logo accent, not generic/default Inno Setup graphics.
- Confirmed language selector (English default, Hebrew), destination-folder picker with Browse
  (no `DisableDirPage`), Desktop/Start-Menu shortcut tasks, proper uninstaller registration
  (`AppId`, `UninstallDisplayIcon`), and update-awareness (silent `/VERYSILENT` self-update path
  plus `AppMutex` running-instance check) are all present and unchanged.
- Version bump only, to package this audit and rebuild the installer artifact.

### 2.8.0 — 2026-09-15 — Competitor-research feature pass + UI polish
Looked at what Hazel (Mac), DropIt, File Juggler, Belvedere and TIDY do well in 2026 and picked a
small number of realistic additions for a lightweight local tray app — no cloud dependency added.
- Added: **"ask before filing when no client is recognized"** (off by default) — inspired directly by
  Hazel's rule-preview workflow. When on, a file that Filio can't match to *any* known client is left
  exactly where it is instead of silently landing under "Unknown Client"; it shows up in the Activity
  tab as "Needs review" and waits there.
- Added: **"Fix / File…"** action in the Activity tab — a small dialog to pick the right client and
  document type, usable both on a "Needs review" entry (files it for the first time) and on an
  already-filed entry that landed in the wrong place (moves it to the correct folder). This is the one
  new user-facing screen in this release.
- Added: **learning from corrections** — the "Fix / File…" dialog has a "remember this for next time"
  toggle. When checked, Filio picks one distinctive, non-generic word out of the file name and adds it
  as an alias to the chosen client's keyword list, so a similar file is recognized automatically next
  time instead of needing another manual fix. Fully local, no new dependency — reuses the existing
  client-alias matching that already powers `DocumentClassifier`.
- Fixed/hardened: `ImageTextService` had no upper bound on image size before handing a file to the
  (single, shared, non-thread-safe) OCR engine — an unusually large photo could tie up the shared
  engine lock for a long time and stall every other file waiting on OCR across all watch folders, not
  just itself. Files over 15MB now skip OCR and fall back to filename-only classification instead.
- Changed: subtle WPF hover transitions (150ms, matching `design-tokens.json`'s `motion.fast`) on
  buttons, and a 250ms fade-in on the main window's content when it loads — small, low-risk visual
  polish on top of the existing teal/orange brand palette (`assets/BRAND.md`), nothing structural
  changed in the settings dialog, onboarding wizard, tray menu or installer wizard layout.
- Note: undo-last-move and content-hash duplicate detection — two other common asks in this space —
  already existed in Filio before this release (2.2.0 and earlier); this pass added what was actually
  missing rather than re-implementing them.

### 2.7.1 — 2026-09-15 — STANDARDS.md compliance sweep
- Fixed: Filio could be launched twice, causing two file watchers to race on the same Downloads folder (risk of double-filing the same file). A named Mutex now makes a second launch exit immediately.
- Fixed: the installer picked its language automatically from the Windows UI language, contradicting the rule that English is always the default. The installer now always defaults to English regardless of OS locale (a Hebrew installer is still available via `/LANG=hebrew`).
- Fixed: the splash screen's version number was a hardcoded literal (`"2.7.0"`) instead of reading the real build version — it would have silently gone stale on every future release. Now reads the assembly version (set from `version.json` at publish time), same source as the in-app About screen.
- Fixed: the installer's output filename (`FilioSetup.exe`) didn't follow the required `<ToolName>-Setup-<version>.exe` convention (STANDARDS.md §1/§11.1 — a bare/generic name is also the target of a known Windows DLL-injection compatibility-shim bug for literally-named `setup.exe`, though `FilioSetup.exe` wasn't literally that). Now builds as `Filio-Setup-<version>.exe`, so a stale installer can never silently sit next to newer source without the mismatch being obvious.
- Added a short bilingual EULA (`build/installer/EULA_en.txt` / `EULA_he.txt`), shown as an optional license page in the installer wizard — cheap insurance given the installer isn't code-signed.
- Changed: moved `installer/setup.iss` + wizard images to `build/installer/` — STANDARDS.md's required folder layout keeps installer *scripts* under `build/`, reserving the project root for the final installer executable and top-level docs only.
- Fixed: `README.md`/`CHANGELOG.md` both linked to `docs/מסמך-אפיון.html`, a file deleted in the 2026-09-14 SPEC.md consolidation (see DELETIONS.md) — both now point to `SPEC.md`, the file that actually replaced it.
- Removed the root `FilioSetup.exe` (built under the old name/version, v2.7.0) — see DELETIONS.md. **Rebuilding a fresh `Filio-Setup-2.7.1.exe` requires Inno Setup 6, which is not installed in this sandbox; run `build.ps1` on a machine that has it before distributing this version.**

### 2.7.0 — Installer file sorting + Windows Defender scan
- Added optional installer file (exe/msi) sorting into its own `_Installers\Year` folder, off by default — installer files don't go through the document classifier since they have no meaningful "client" or "document type".
- Added a pre-filing Windows Defender scan (on by default) — every file is checked against Windows Defender (via `MpCmdRun.exe`) before filing. A file flagged as a threat stays in place and is never filed, and is marked clearly in the activity log. If Defender is unavailable, the file is filed as usual with a note that it wasn't scanned.
- Both settings are configurable from the onboarding wizard and from the General tab at any time.
- Test suite grew to 93 tests (from 85), including an end-to-end check using the industry-standard EICAR antivirus test file.

### Earlier versions
See the Hebrew section below for the full per-version history — every entry from the beginning of the project, including local OCR (2.6.0), the Windows auto-start self-healing fix (2.6.1), an additional integrity pass (2.6.2), and everything before.

---

## עברית

כל שינוי משמעותי בתוכנה מתועד כאן, מהגרסה החדשה ביותר לישנה ביותר. לתיאור כללי של
המוצר ראו [README.md](README.md); למסמך האפיון המלא ראו [SPEC.md](SPEC.md).

## 2.9.0 — 2026-09-17 — החלפת מנוע ההתקנה: Inno Setup ← אשף WinForms מותאם אישית

ביקורת 2.8.1 אישרה שהאשף (Inno Setup) היה ממותג היטב (תמונות גרדיאנט, סגנון modern), אבל מיתוג
בתמונות סביב עמודי האשף של Inno אף פעם לא היה מספיק: העמודים תמיד מציגים את כפתורי
Next/Back/Cancel, תיבות הסימון ופס ההתקדמות בעיצוב Windows גנרי, לא משנה אילו תמונות מקיפות
אותם. זה כבר לא עומד ברף של הפורטפוליו הזה (ראו את אשפי ההתקנה של OptiGuard ו-AutoProcessTwin,
שקבעו את הרף). אז טכנולוגיית ההתקנה עצמה השתנתה, לא רק העטיפה שלה:

- **הוסר**: `build/installer/setup.iss` כבר לא נבנה על-ידי `build.ps1` (נשאר במקום כ-legacy לא
  בשימוש לצד `wizard_large.bmp`/`wizard_small.bmp`/`EULA_*.txt` - קובצי ה-EULA עדיין בשימוש,
  עכשיו מוטבעים ישירות באשף החדש במקום הפניה מ-Inno).
- **נוסף**: `build/installer/Setup.cs` + `Setup.csproj` - אשף התקנה בנוי-ידנית ב-C#/WinForms
  (net48, פיילוד self-contained של כ-79MB מוטבע כמשאב zip יחיד) עם כל פקד צבוע במפורש מהפלטה
  הכהה של `assets/BRAND.md` (רקע `#0D1A18`, primary `#1E7F72`, accent `#DB8A1F`) - כפתורים
  שטוחים עם מצבי hover/pressed/disabled מפורשים, ומחלקות `ThemedCheckBox`/`ThemedProgressBar`
  מצוירות-עצמאית כדי שתיבות הסימון ופס ההתקדמות לא ייפלו בשקט חזרה לעיצוב Windows גנרי כפי
  שהיה קורה ל-`CheckBox`/`ProgressBar` רגילים (WinForms אף פעם לא מעביר את צבע הרקע של הטופס
  לציור העצמי של שני הפקדים האלה - אותה מחלקת באג שזוהתה ותוקנה בדיוק עכשיו באשף של SnapCap).
- **נוסף**: זרימת אשף אמיתית בת 6 עמודים - בחירת שפה (אנגלית/עברית) ← עמוד ברוכים הבאים עם תוכן
  שיווקי אמיתי על מה ש-Filio עושה (תיוק אוטומטי לפי לקוח/שנה/סוג, OCR מקומי, זיהוי כפילויות,
  סריקת Defender לפני תיוק) ← עמוד רישיון/EULA ייעודי עם טקסט רישיון גלילי ותיבת "אני מסכים/ה"
  חובה ← עמוד מיקום התקנה (ברירת מחדל Program Files, כפתור Browse, תיבות שולחן
  עבודה/Start Menu/הפעלה אוטומטית) ← התקדמות ← סיום, הכול משתקף לעברית RTL באותה שיטה
  שאשפי OptiGuard/AutoProcessTwin משתמשים בה (`RightToLeftLayout`, לא מיקום מחדש ידני של כל
  פקד).
- **נוסף**: האשף משמש גם כתוכנת ההסרה שלו. `RegisterUninstall` מעתיק את עצמו לתיקיית ההתקנה
  בשם `Filio-Setup.exe` ומצביע את `UninstallString` ברישום אל `Filio-Setup.exe --uninstall`,
  כך שאין קובץ `unins000.exe` נפרד שצריך להפיץ או לשמור מסונכרן - `SetupForm.RunUninstall()`
  מסיר קיצורי דרך, את ערך ה-Run של הפעלה אוטומטית, את מפתח ה-Uninstall ברישום ואת תיקיית
  ההתקנה, ושואל קודם (ברירת מחדל "לא") לפני מחיקת נתוני ההגדרות/יומן הפעילות ב-
  `%APPDATA%\Filio`, בדיוק כמו ש-`CurUninstallStepChanged` הישן ב-`setup.iss` עשה.
- **נוסף**: `Filio-Setup.exe --selftest` - בדיקת עשן ללא ממשק, ללא הרשאות מנהל (בלי UAC, בלי
  כתיבה לדיסק) שמוודאת שקובץ ה-zip המוטבע מכיל את `Filio.exe`, ששני משאבי ה-EULA קיימים
  ולא ריקים, שהאייקון של המותג נטען, ושבניית עמודי האשף באנגלית ובעברית/RTL לא זורקת שגיאה.
  `build.ps1` מריץ את הבדיקה הזו אוטומטית כצעד הבנייה האחרון ונכשל אם היא לא עוברת. זו כיסוי
  אמיתי אך חלקי: היא תופסת כשלים מסוג "שכחתי להטביע משאב" / "חריגה בבניית עמוד N", אבל היא
  **אינה** תחליף לבדיקה ויזואלית אינטראקטיבית של צבעי כפתורים, ניגודיות, מצבי hover ומיראור
  RTL על סשן Windows GUI חי - זה לא נעשה בגרסה הזו; ראו את דוח הסיכום למה בדיוק עוד דורש בדיקה
  בעיניים.
- **הועבר**: אותה בדיקת מופע-יחיד ש-`AppMutex=Global\Filio-SingleInstance-7C2A9E1D` הישן של
  Inno עשה - `EnsureAppNotRunning`/`IsFilioRunning` בודקים גם את שם התהליך `Filio` וגם
  `Mutex.TryOpenExisting` על אותו שם Mutex בדיוק (אותו קבוע כמו `SingleInstanceMutexName` ב-
  `App.xaml.cs`) לפני התקנה, עדכון או הסרה, ומציגים Retry/Cancel אם Filio עדיין רצה במקום
  להיכשל באמצע על exe נעול.
- **שונה**: שלב 3 ב-`build.ps1` מריץ עכשיו `dotnet build build\installer\Setup.csproj` במקום
  להפעיל את `ISCC.exe` של Inno Setup - אין יותר צורך בהתקנת כלי חיצוני, ושלב 4 מריץ את בדיקת
  ה-`--selftest` החדשה לפני שההתקנה נחשבת מוכנה.
- אין שינויי קוד באפליקציה בגרסה הזו - `app/Filio.App` לא הושפע, כל 99 הבדיקות הקיימות עדיין
  עוברות (`dotnet test`, ללא שינוי).

## 2.8.1 — 2026-09-17 — ביקורת עידון ויזואלי לאשף ההתקנה

בלי שינויי קוד באפליקציה. נבדק אשף ההתקנה (Inno Setup) מול הציפיות לאשף "אמיתי, ברמה מקצועית"
של 2026, ואומת שהוא כבר עומד בהן מסבב קודם:

- אושר `WizardStyle=modern` (המראה השטוח של Inno Setup 6).
- אושר שתמונות האשף (`build/installer/wizard_large.bmp` בגודל 164×314 ו-`wizard_small.bmp`
  בגודל 55×58) אכן ממותגות בפועל - צבעי פיקסלים שנדגמו תואמים לגרדיאנט המתועד
  `#0F5348` ← `#1E7F72` עם מבטא הלוגו `#DB8A1F`, ולא גרפיקת ברירת מחדל גנרית של Inno Setup.
- אושרו: בחירת שפה (אנגלית כברירת מחדל, עברית), בחירת תיקיית יעד עם כפתור Browse
  (אין `DisableDirPage`), משימות קיצור דרך לשולחן העבודה/תפריט התחל, רישום תקין של הסרת ההתקנה
  (`AppId`, `UninstallDisplayIcon`), ומודעות לעדכונים (נתיב עדכון עצמי שקט `/VERYSILENT` יחד עם
  בדיקת `AppMutex` למופע רץ) - כולם קיימים וללא שינוי.
- העלאת גרסה בלבד, כדי לארוז את הביקורת הזו ולבנות מחדש את קובץ ההתקנה.

## 2.8.0 — 2026-09-15 — סבב תכונות ממחקר תחרותי + עידון UI

מחקר קצר על מה שכלים מתחרים (Hazel ב-Mac, DropIt, File Juggler, Belvedere, TIDY) עושים טוב
ב-2026, ובחירת מספר קטן של תוספות ריאליות לכלי מגש קליל - בלי להוסיף תלות בענן:

**נוסף:**
- **"לשאול לפני תיוק כשלא זוהה שום לקוח"** (כבוי כברירת מחדל) - בהשראה ישירה מהיכולת של
  Hazel לתת "תצוגה מקדימה" לכלל לפני שהוא רץ. כשמופעל, קובץ שהמנוע לא הצליח להתאים לאף לקוח
  מוכר בכלל נשאר בדיוק במקומו במקום להיכנס בשקט תחת "לקוח לא ידוע" - הוא מופיע בטאב הפעילות
  בתור "ממתין לבדיקה" וממתין שם.
- **"תקן / תייק…"** - פעולה חדשה בטאב הפעילות: חלון קטן לבחירת הלקוח והסוג הנכונים, שמשמש גם
  לתיוג ראשוני של רשומת "ממתין לבדיקה" וגם להעברת קובץ שכבר תויק בטעות למקום הנכון. זהו מסך
  המשתמש החדש היחיד בגרסה הזו.
- **למידה מתיקוני משתמש** - לחלון "תקן / תייק…" יש מתג "לזכור את זה לפעם הבאה". כשמסומן,
  Filio בוחר מילה אחת בולטת ולא-גנרית משם הקובץ ומוסיף אותה ככינוי לרשימת מילות המפתח של
  הלקוח שנבחר, כך שקובץ דומה יזוהה אוטומטית בפעם הבאה בלי תיקון חוזר. לגמרי מקומי, בלי תלות
  חדשה - משתמש במנגנון התאמת הכינויים הקיים כבר ב-`DocumentClassifier`.
- **תוקן/חוזק**: ל-`ImageTextService` לא הייתה הגבלה על גודל תמונה לפני שהיא נמסרת למנוע ה-OCR
  (משותף, נעילה יחידה, לא Thread-Safe) - תמונה גדולה באופן חריג יכלה לתפוס את הנעילה המשותפת
  לזמן ארוך ולתקוע בפועל כל קובץ אחר שממתין ל-OCR בכל תיקיות המעקב, לא רק את עצמה. קבצים מעל
  15MB עכשיו מדלגים על OCR ונופלים חזרה לסיווג לפי שם קובץ בלבד.
- **שונה**: מעברים עדינים ב-WPF (150ms, תואם ל-`motion.fast` ב-`design-tokens.json`) על ריחוף
  כפתורים, ו-fade-in של 250ms לתוכן החלון הראשי בעלייה - עידון ויזואלי קטן וזהיר מעל פלטת
  המיתוג הקיימת (טיל/כתום, `assets/BRAND.md`), בלי שינוי מבני במסך ההגדרות, אשף ההתקנה
  הראשוני, תפריט ה-tray או אשף ההתקנה (Inno Setup).
- **הערה**: ביטול פעולה אחרונה וזיהוי כפילויות לפי hash תוכן - שתי בקשות נפוצות נוספות בתחום
  הזה - כבר היו קיימות ב-Filio לפני הגרסה הזו (2.2.0 ומוקדם יותר); הסבב הזה הוסיף את מה
  שבאמת חסר במקום לממש מחדש דברים שכבר קיימים.
- אומת: `dotnet build`+`dotnet test` על `app/Filio.sln` עוברים נקי (0 שגיאות, 99/99 בדיקות
  עוברות, עלייה מ-93 - 6 בדיקות חדשות ל-`Reclassify` ול-הצעת כינוי נלמד משם קובץ);
  `build.ps1` רץ מקצה לקצה ובנה `Filio-Setup-2.8.0.exe` מעודכן.

## 2.7.1 — 2026-09-15 — ביקורת תאימות מלאה מול STANDARDS.md

ביקורת מקיפה (build, שורש, גרסאות, RTL/i18n, splash/onboarding, installer, זיכרון,
UX) גילתה כמה פערים אמיתיים, כולם תוקנו:

**תוקן:**
- נוסף `Mutex` בשם קבוע (`Global\Filio-SingleInstance-7C2A9E1D`) ב-`App.xaml.cs` — הפעלה שנייה יוצאת מיד במקום לפתוח מופע כפול, שהיה גורם לשני `FileWatcherService` לצפות על אותה תיקיית הורדות במקביל (סיכון לתיוג כפול של אותו קובץ).
- נוסף `AppMutex` תואם ל-`build/installer/setup.iss` — Inno Setup מזהה כעת שהתוכנה פתוחה **גם לפני התקנה/עדכון במתקין עצמו**, לא רק ברמת האפליקציה, ומבקש לסגור אותה לפני החלפת קבצים (זה גם מתקן את `/CLOSEAPPLICATIONS` בעדכון השקט, שלא הייתה לו קודם דרך לדעת מה לסגור).
- מסך בחירת שפה במתקין (`ShowLanguageDialog`) הופעל מחדש (`yes`, היה כבוי בטעות קודם) יחד עם `LanguageDetectionMethod=none` — כך שהמשתמש **תמיד** רואה בורר שפה אמיתי, וברירת המחדל היא תמיד אנגלית (הרשומה הראשונה ב-`[Languages]`) ללא תלות בשפת Windows — לא "בורר קיים אך מוסתר" כמו קודם.
- תוקן: מספר הגרסה במסך הפתיחה (Splash) היה מחרוזת קשיחה `"2.7.0"` ב-`App.xaml.cs` במקום קריאה מהגרסה האמיתית — היה נשאר תקוע ולא מתעדכן בכל גרסה עתידית. עכשיו קורא מ-`UpdateService.CurrentVersion` (אותו מקור כמו מסך ה-About), הנטען מ-`version.json` בזמן ה-publish.
- שם קובץ ההתקנה שונה מ-`FilioSetup.exe` ל-`Filio-Setup-<גרסה>.exe`, כנדרש ב-STANDARDS.md §1/§11.1 (לעולם לא שם גנרי; תמיד עם מספר גרסה בשם הקובץ עצמו כדי שקובץ ישן לעולם לא ייראה תואם בטעות לקוד חדש).
- נוסף EULA קצר דו-לשוני (`build/installer/EULA_en.txt`/`EULA_he.txt`) כמסך רישיון אופציונלי באשף ההתקנה — ביטוח זול בהתחשב בכך שההתקנה לא חתומה דיגיטלית.
- הועברו `installer/setup.iss` + תמונות האשף ל-`build/installer/` — לפי מבנה התיקייה הנדרש ב-STANDARDS.md §1, סקריפטי ההתקנה שייכים תחת `build/`, השורש נשאר לקובץ ההתקנה הסופי בלבד.
- תוקנו קישורים שבורים ב-`README.md`/`CHANGELOG.md` ל-`docs/מסמך-אפיון.html` שנמחק בסבב 2026-09-14 (הוחלף ב-`SPEC.md`) — הקישורים לא עודכנו אז.
- הוסר קובץ ההתקנה הישן `FilioSetup.exe` מהשורש (נבנה תחת גרסה/שם ישנים) — **אין Inno Setup מותקן בסביבת העבודה שביצעה את הביקורת הזו, כך שלא ניתן היה לבנות מחדש קובץ התקנה עדכני; יש להריץ `build.ps1` על מחשב עם Inno Setup 6 לפני הפצת הגרסה הזו** (ראו DELETIONS.md).
- אומת: `dotnet build`+`dotnet test` על `app/Filio.sln` עוברים נקי (0 שגיאות, 93/93 בדיקות עוברות) אחרי כל השינויים; `build.ps1 -PublishOnly` רץ בהצלחה מקצה לקצה.

## 2.7.0 — קובצי התקנה + בדיקת Windows Defender

שני הרחבות משמעותיות, שתיהן כבויות-אלא-אם-מופעלות בכוונה כדי לא לחזור על הטעות של "לתייק
הכל" מגרסה מוקדמת יותר:

- **מיון קובצי התקנה** — אפשר להפעיל תיוק של קובצי exe/msi (כבוי כברירת מחדל). הם לא
  עוברים דרך מנוע הסיווג של מסמכים (אין להם "לקוח" או "סוג מסמך" משמעותיים) - הם מקבלים
  תיקייה נפרדת משלהם: `_Installers\שנה`. כפילויות (אותו קובץ התקנה שהורד שוב) מנותבות
  ל-`_Possible Duplicates\_Installers` בדיוק כמו מסמכים רגילים.
- **בדיקת Windows Defender לפני תיוק** (מופעל כברירת מחדל) — כל קובץ נבדק מול Windows
  Defender (האנטי-וירוס המובנה והחינמי של Windows) לפני שהוא מתויק. Filio לא בונה מנוע
  אבטחה משלו - הוא שואל את Defender מה הוא כבר יודע, דרך כלי שורת הפקודה שלו (MpCmdRun.exe).
  קובץ שמזוהה כאיום **נשאר במקומו המקורי ולעולם לא מתויק**, ומופיע ביומן הפעילות עם סימון
  אדום ברור. אם Defender לא זמין, הקובץ מתויק כרגיל עם הערה שהוא לא נבדק - לא נטען בטעות
  ש"בדקנו ומצאנו שהוא נקי" כשלא בוצעה בדיקה בפועל.
- שתי ההגדרות ניתנות לקביעה גם באשף ההתקנה הראשוני וגם בטאב כללי בכל עת.
- 93 בדיקות בסך הכל (עלייה מ-85) - כולל בדיקה אמיתית עם קובץ הבדיקה התקני של תעשיית
  האנטי-וירוס (EICAR) שמוודאת מקצה לקצה שקובץ "נגוע" (מלאכותית, לצורכי בדיקה) לעולם לא
  מתויק ומדווח כראוי.

## 2.6.2 — סבב בדיקת תקינות נוסף (בעקבות דיווח על התנהגות לא-צפויה)

בדיקת קוד יזומה נוספת, ממוקדת בקוד שנוסף/שונה לאחרונה (OCR, סנכרון הפעלה אוטומטית):

- **תיקון קריטי**: `StartupService.SetEnabled` (הקריאה שמסנכרנת את רישום ההפעלה האוטומטית
  בכל הפעלה, שנוספה ב-2.6.1) לא הייתה עטופה ב-try/catch. במחשב עם מדיניות ארגונית שנועלת
  את מפתח ה-Run של Windows, זה יכול היה לגרום ל-Filio **לקרוס בכל הפעלה**, לא רק להיכשל
  בשקט בהגדרה אחת. נמצא ותוקן לפני שהגיע למשתמשי קצה.
- **בדיקת עדכונים** לא הייתה אמורה להיחסם על כתובת http:// ישנה שנשארה ב-manifest אם ממילא
  אין גרסה חדשה יותר להציע - עכשיו הבדיקה הזו קורית רק אחרי שכבר ידוע שיש עדכון רלוונטי.
- **הורדת עדכון שנקטעת** (רשת נופלת באמצע קובץ גדול) הייתה משאירה קובץ .exe חלקי לצמיתות
  ב-Temp; עכשיו מנוקה בכל כשל, לא רק באי-התאמת חתימה.
- **OCR שנכשל פעם אחת** (למשל תיקיית tessdata נעולה רגעית) היה נשאר כבוי לכל שאר ההפעלה;
  עכשיו כל קריאה מנסה שוב במקום לוותר לצמיתות.
- **בדיקת הבריאות התקופתית** של תיקיות המעקב הייתה בונה ומשמידה אובייקט watcher חדש כל 30
  שניות גם לתיקיות שכבר עבדו מצוין - עכשיו נוגעת רק בתיקיות שבאמת לא היו תקינות.

## 2.6.1 — תיקון: "הפעלה אוטומטית עם Windows" לא תמיד עבדה בפועל

באג אמיתי שנתפס בבדיקת מכונה חיה: מסך ההגדרות והקובץ השמור יכלו להצהיר
`StartWithWindows: true`, בזמן שרישום ההפעלה האוטומטית בפועל ב-Windows היה ריק - כלומר
Filio לא באמת היה עולה בהפעלה הבאה של המחשב, למרות שההגדרה אמרה שכן. קרה כי הרישום
נכתב רק בסיום אשף ההתקנה או בלחיצת "שמירה" במסך הגדרות - לא נבדק/נכתב מחדש בכל הפעלה
רגילה של התוכנה, אז הוא יכול "להתבדר" מההגדרה השמורה (למשל אחרי הסרת התקנה ידנית או
הרצה ישירה של קובץ ה-exe בלי לעבור דרך ההתקנה).

- Filio מסנכרן עכשיו את רישום ההפעלה האוטומטית עם ההגדרה השמורה **בכל הפעלה**, לא רק
  בשמירת הגדרות - עצמי-מריפא, כמו מנגנון ההתאוששות של מעקב התיקיות.
- מסך ההגדרות מציג את המצב **האמיתי** ברישום של Windows במקום להציג עיוורת את ההגדרה
  השמורה, כדי שלא יהיה פער בין מה שכתוב על המסך למה שבאמת יקרה בהפעלה הבאה.

## 2.6.0 — זיהוי טקסט בתמונות (OCR מקומי) + הרחבת סוגי קבצים

שדרוג ה"AI" המקומי כדי שיתייק לא רק PDF אלא גם צילומים וסריקות של מסמכים:

- **OCR מקומי אמיתי** — מנוע Tesseract (אנגלית + עברית) מובנה שקורא טקסט מתוך קובצי jpg/png
  ומריץ עליו את אותו מנוע סיווג בדיוק כמו PDF - סוג מסמך, לקוח ותאריך. לגמרי על המחשב,
  שום תמונה לא נשלחת לשום שרת. ניתן לכבות בטאב כללי ← הגדרות מתקדמות למי שרוצה מהירות.
- **רשימת הסיומות הנצפות כברירת מחדל הורחבה** — נוספו jpg/jpeg/png ו-ppt/pptx (מסמכי
  PowerPoint מזוהים לפי שם קובץ, כמו Word/Excel כבר קודם). ארכיונים, קבצי התקנה וקבצי
  מערכת עדיין לא נגעים בהם - ההרחבה נעשתה בזהירות, לא "לתייק הכל".
- 85 בדיקות בסך הכל (עלייה מ-76) - כולל בדיקה שמצלמת טקסט אמיתי בתוך תמונה שנוצרת בזמן
  הבדיקה ומוודאת שה-OCR בפועל מזהה אותו נכון מקצה לקצה, לא בדיקה מדומה.

## 2.5.1 — סבב תיקוני יציבות ותאימות (בדיקת קוד מלאה)

עברה על כל הקוד בדיקת תקינות ממוקדת (5 "זוויות" בדיקה שונות: לוגיקה שורה-אחר-שורה, מקרי-קצה,
עקביות בין קבצים, שכפול קוד, ויעילות/ביצועים). נמצאו ותוקנו 10 בעיות אמיתיות:

- **מרוץ תזמון (race condition) ביומן הפעילות** — כשכמה קבצים מטופלים כמעט בו-זמנית
  (שתי תיקיות מעקב מקבלות קובץ באותה שנייה, למשל), הכתיבה ליומן הפעילות הייתה יכולה
  לאבד בשקט רשומה של קובץ אחד מהם. נוסף נעילה (`lock`) סביב קריאה-שינוי-כתיבה של הקובץ.
- **מרוץ תזמון באינדקס זיהוי הכפילויות** — אותה בעיה, על מבנה הנתונים הפנימי שמזהה
  קבצים כפולים לפי תוכן; יכלה לגרום לשגיאה או לשחיתות נתונים בעומס. תוקן באותה צורה.
- **הורדת עדכון שנקטעת אחרי 15 שניות** — קובץ התקנה אמיתי (עשרות MB) לוקח יותר מ-15
  שניות על כל חיבור שאינו מהיר במיוחד, וההורדה נקטעה כמעט תמיד. ניתן לה חלון זמן נפרד
  וארוך בהרבה (10 דקות) מבדיקת גרסה קטנה.
- **קיפאון אפשרי במסך ההגדרות** — אם יש כמה תיקיות מעקב וחלקן על כונן רשת לא זמין,
  לחיצה על "שמירה" הייתה יכולה לקפוא לכמה שניות *לכל תיקייה בנפרד* (במקום לכולן ביחד).
  עכשיו כל התיקיות מותנעות במקביל, אז הקיפאון המקסימלי מוגבל תמיד לכ-3 שניות בסך הכל.
- **ייצוא הגדרות שיכול היה להפיל את כל התוכנה** — ייצוא לתיקייה לקריאה-בלבד או לכונן
  מלא זרק חריגה לא-מטופלת שסגרה את כל האפליקציה. עכשיו מוצגת הודעת שגיאה ברורה במקום.
  זו הייתה אחת מהתקלות הכלליות ("קורס") שדווחו.
- **ספירה שגויה בסריקה החכמה** — אם לא הוגדר חוק "ברירת מחדל" בכלל, האשף הראשוני היה
  סופר קבצים לא-מזוהים בטעות כ"מזוהים", ומציג תמונת מצב אופטימית-מדי בהגדרה הראשונית.
- **זיהוי-יתר של תיקיות ענן** — תיקייה מקומית רגילה בשם כמו `OneDriveBackup` הייתה
  מזוהה בטעות כתיקיית OneDrive אמיתית (בדיקת ההתאמה בדקה התחלה של שם, לא שם מדויק).
- **הגדרות עדכונים שחוזרות לערך הישן** — אחרי ייבוא גיבוי הגדרות עם כתובת עדכונים
  חדשה, טאב Updates המשיך להציג את הכתובת הישנה על המסך; שמירה משם הייתה דורסת בחזרה
  את הכתובת שזה עתה יובאה.
- **מבנה תיקיות לא-עקבי לכפילויות** — קובץ שזוהה ככפילות תמיד נכנס למבנה קבוע (לקוח/סוג),
  גם אם המשתמש הגדיר סדר תיקיות אחר (שנה/לקוח/סוג וכו') לשאר הקבצים. עכשיו זהה לכולם.
- **watcher "יתום"** — במקרה נדיר של שינוי הגדרות פעמיים ברצף בזמן שתיקיית רשת איטית
  עדיין בתהליך התחברות, watcher ישן היה יכול "לחזור לחיים" אחרי שכבר הוחלף. תוקן עם
  מספר-מחזור (generation counter) שמשליך תוצאות של מחזור ישן.

כל התיקונים מכוסים בבדיקות אוטומטיות חדשות (76 בדיקות בסך הכל, עלייה מ-72).

## 2.5.0 — שדרוג עיצוב ו-UX משמעותי

ממוקד במי שלא טכני:

- **מסך ההגדרות בנוי מ"כרטיסים" ויזואליים** עם אייקון וכותרת ברורה לכל קבוצת הגדרות
  (מאיפה לאסוף קבצים, לאן לתייק, איך Filio מתנהג, שפה) במקום רשימת שדות שטוחה וטכנית.
- **מתגי הפעלה/כיבוי גרפיים** (כמו באפליקציית מובייל) במקום תיבות סימון רגילות.
- **"הגדרות מתקדמות" מוסתרות כברירת מחדל** מאחורי חלונית שנפתחת בלחיצה - סיומות קבצים,
  רשימת התעלמות וסדר תיקיות לא מציפים משתמש חדש, אבל נגישים בלחיצה אחת למי שרוצה.
- **כותרות טאבים עם אייקון** וניסוח מחדש של כמעט כל טקסטי המסך לעברית פשוטה ויומיומית
  יותר, כולל דוגמה קונקרטית בראש טאבי לקוחות וחוקי סיווג.
- הודעות הצלחה קיבלו קצת "כיף" — סימן ✅ ואימוג'ים במקום טקסט יבש.

## 2.4.0 — בהירות, UX ואבטחה

- **"AI" מסביר את עצמו** — כל שורה ביומן הפעילות מקבלת tooltip שמסביר בשפה פשוטה למה
  הקובץ סווג כפי שסווג: איזו מילה/תבנית נמצאה, בתוכן או בשם הקובץ, ומאיפה הגיע התאריך.
- **הסברים צמודים לכל הגדרה** — tooltips על סדר התיקיות, רשימת ההתעלמות, סוגי הקבצים
  למעקב וזיהוי כפילויות בטאב כללי.
- **אבטחת מנגנון העדכון** — כתובת העדכונים וכתובת קובץ ההתקנה חייבות להיות `https://`,
  וכאשר קובץ ה-manifest מפרסם גיבוב `sha256`, הקובץ שהתקבל מאומת מולו לפני שמריצים אותו.
- **ייבוא הגדרות מוקשח** — קובץ גיבוי חיצוני נדחה בשקט אם הוא חורג מגודל סביר או מכיל
  כמות קבצים/לקוחות/חוקים לא סבירה.

## 2.3.0 — סבב שדרוגים לפי משוב על נוחות תפעול

- **סדר תיקיות מותאם אישית** — לקוח/שנה/סוג, שנה/לקוח/סוג או סוג/לקוח/שנה.
- **רשימת התעלמות** — שמות/מזהי קבצים ספציפיים שלעולם לא יתויקו אוטומטית.
- **כפתור נסה שוב** בטאב Activity לעיבוד מחדש של קובץ שנכשל, בלי לגרור אותו שוב.
- **גיבוי/שחזור הגדרות** — ייצוא/ייבוא כל ההגדרות לקובץ JSON אחד.
- **התראה לחיצה** — לחיצה על הודעת המערכת פותחת את Explorer עם הקובץ המסומן.

## 2.2.1 — שני תיקוני יציבות אמיתיים שנתפסו בשימוש בפועל

- Filio היה מתייק **כל קובץ** שנוחת בתיקיית המעקב — כולל תמונות, HTML ומסמכי עבודה
  כלליים — במקום להתעלם מהם. נוספה רשימת סיומות "נצפות" מוגדרת וניתנת לעריכה.
- תוקן מרוץ תזמון שיכול היה לגרום לקובץ הראשון שנוחת מיד אחרי הפעלה להתפספס.

## 2.2.0 — סבב פיצ'רים מבוסס מחקר תחרותי

כמה תיקיות מעקב במקביל, מעקב עצמי-מחלים אחרי תקלות, זיהוי קבצים כפולים לפי תוכן עם
ניתוב שקוף ל-`_Possible Duplicates`, וזיהוי תיקיות/קבצי OneDrive-Dropbox-Google Drive.

## 2.1.0 — אשף "ברוכים הבאים" והסריקה החכמה

אשף ממותג בהפעלה ראשונה (5 צעדים, כולל בחירת שפה חיה), ו"סריקה חכמה" מקומית שמציעה
לקוחות מתוך קבצים קיימים. ניתן להריץ את האשף מחדש בכל עת מטאב Help.

## 2.0.0 — מיתוג מחדש מלא

שם ולוגו חדשים (Filio), ברירת מחדל באנגלית עם מעבר חי לעברית, מנגנון עדכון-במקום,
טאב Updates וטאב Help חדשים, מסך התקנה מעוצב, ותיקון באג אמיתי בדיאלוג בחירת שפה
שתקע התקנות שקטות.

## 1.1.0

עיצוב מחודש למסך ההגדרות, אימות תיקיות, חוסן גבוה יותר במעקב אחר קבצים, חבילת בדיקות
אוטומטיות ראשונה.

## 1.0.0

גרסה ראשונה (בשם "תייקן").
