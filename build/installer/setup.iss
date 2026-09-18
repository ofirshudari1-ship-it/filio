; build\installer\setup.iss
; Inno Setup script that builds a single installer file (Filio-Setup-<version>.exe) for Filio.
;
; Prerequisites:
;   1. Inno Setup 6 installed (https://jrsoftware.org/isinfo.php) - free.
;   2. Publish the app first (from app\Filio.App):
;        dotnet publish -c Release -r win-x64 --self-contained true ^
;          -p:PublishSingleFile=true -o ..\publish
;      This produces ..\publish\Filio.exe
;   3. Open this file in the Inno Setup Compiler and click Build
;      (or run ISCC.exe build\installer\setup.iss from the command line).
;   4. The installer is written to build\Filio-Setup-<version>.exe; build.ps1 then moves
;      it to the project root as the final deliverable.
;
; The installer also doubles as the silent auto-updater: Filio's own "Check for
; Updates" feature downloads the newer installer and runs it with
; /VERYSILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS, which closes the running
; app, replaces the files in place (same AppId/install folder), and restarts it -
; no wizard, no reinstall from scratch.

#define MyAppName "Filio"
#ifndef MyAppVersion
  #define MyAppVersion "2.8.0"
#endif
#define MyAppPublisher "Filio"
#define MyAppExeName "Filio.exe"
#define MyPublishDir "..\publish"

[Setup]
AppId={{C077E729-7453-4A6B-B4FA-1F95CB6DA4F6}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Filio
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; build\installer\..  =  build\ itself; build.ps1 moves the finished file from there
; to the project root as the very last step.
OutputDir=..
; STANDARDS.md 11.1: never "setup.exe" (known DLL-injection compat-shim target) and always
; include the version in the filename so an old installer can never sit next to newer source.
OutputBaseFilename=Filio-Setup-{#MyAppVersion}
SetupIconFile=..\..\app\Filio.App\Assets\filio.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
WizardImageFile=wizard_large.bmp
WizardSmallImageFile=wizard_small.bmp
; STANDARDS.md 11.11: short EULA (AS IS, personal-use license), shown as an optional wizard
; page - especially relevant since the installer isn't code-signed (11.2). Per-language file
; is selected below in [Languages].
LicenseFile=EULA_en.txt
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline dialog
ArchitecturesInstallIn64BitMode=x64compatible
; Same name as SingleInstanceMutexName in App.xaml.cs - Inno checks this before
; install/update and prompts the user to close Filio first if it's running,
; instead of silently failing to overwrite the locked exe.
AppMutex=Global\Filio-SingleInstance-7C2A9E1D
; Language selection shown at the start of a normal install.
; For silent auto-updates the app passes /LANG=english, so the dialog is skipped automatically.
ShowLanguageDialog=yes
; Default is English for every tool (2026-09-14 decision), not auto-detected from the
; Windows UI language - LanguageDetectionMethod=none makes Inno always use the first
; [Languages] entry below (english) regardless of OS locale. A user who wants the
; Hebrew installer text can still get it by running "Filio-Setup-<version>.exe /LANG=hebrew";
; the app itself has its own in-app language switch for everyday use either way.
LanguageDetectionMethod=none

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"; LicenseFile: "EULA_en.txt"
Name: "hebrew"; MessagesFile: "compiler:Languages\Hebrew.isl"; LicenseFile: "EULA_he.txt"

[CustomMessages]
english.DesktopIconDesc=Create a desktop shortcut
english.AdditionalIconsGroup=Additional shortcuts:
english.AutoStartDesc=Start automatically with Windows
english.StartupOptionsGroup=Startup options:
english.UninstallShortcut=Uninstall {#MyAppName}
english.LaunchAfterInstall=Launch {#MyAppName} now

hebrew.DesktopIconDesc=יצירת קיצור דרך בשולחן העבודה
hebrew.AdditionalIconsGroup=קיצורי דרך נוספים:
hebrew.AutoStartDesc=הפעלה אוטומטית עם הפעלת המחשב
hebrew.StartupOptionsGroup=אפשרויות הפעלה:
hebrew.UninstallShortcut=הסרת התקנה של {#MyAppName}
hebrew.LaunchAfterInstall=הפעלת {#MyAppName} כעת

english.DeleteSettingsPrompt=Do you also want to delete Filio's settings and activity log? (This cannot be undone.)
hebrew.DeleteSettingsPrompt=למחוק גם את ההגדרות ויומן הפעילות של Filio? (לא ניתן לבטל פעולה זו.)

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIconDesc}"; GroupDescription: "{cm:AdditionalIconsGroup}"
Name: "autostart"; Description: "{cm:AutoStartDesc}"; GroupDescription: "{cm:StartupOptionsGroup}"; Flags: checkedonce

[Files]
Source: "{#MyPublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
; מנוע ה-OCR המקומי (Tesseract) לזיהוי טקסט בתמונות - ספריות native + נתוני שפה
; (אנגלית/עברית). אלה קבצים רגילים לצד ה-exe, לא נכנסים ל-single-file bundle.
Source: "{#MyPublishDir}\x64\*"; DestDir: "{app}\x64"; Flags: ignoreversion recursesubdirs
Source: "{#MyPublishDir}\tessdata\*"; DestDir: "{app}\tessdata"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{group}\{cm:UninstallShortcut}"; Filename: "{uninstallexe}"

[Registry]
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Filio"; \
    ValueData: """{app}\{#MyAppExeName}"" --minimized"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
; No "skipifsilent" here on purpose: this line also fires during a silent
; auto-update run (/VERYSILENT), so Filio relaunches itself automatically
; right after the update finishes.
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchAfterInstall}"; Flags: nowait postinstall

[Code]
// שאלה בעת הסרת התקנה אם למחוק גם את נתוני המשתמש שנשמרים מחוץ לתיקיית ההתקנה
// (%APPDATA%\Filio\ - הגדרות, יומן פעילות, לוגים). ברירת המחדל (הכפתור הנבחר) היא
// "לא" כדי שנתונים לא יימחקו בטעות; רק אם המשתמש בוחר "כן" באופן מפורש הם נמחקים.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppDataPath: String;
  Response: Integer;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    AppDataPath := ExpandConstant('{userappdata}\Filio');
    if DirExists(AppDataPath) then
    begin
      Response := MsgBox(ExpandConstant('{cm:DeleteSettingsPrompt}'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2);
      if Response = IDYES then
        DelTree(AppDataPath, True, True, True);
    end;
  end;
end;
