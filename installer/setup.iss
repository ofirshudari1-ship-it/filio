; installer\setup.iss
; Inno Setup script that builds a single installer file (FileFoxSetup.exe) for FileFox.
;
; Prerequisites:
;   1. Inno Setup 6 installed (https://jrsoftware.org/isinfo.php) - free.
;   2. Publish the app first (from app\FileFox.App):
;        dotnet publish -c Release -r win-x64 --self-contained true ^
;          -p:PublishSingleFile=true -o ..\..\build\publish
;      This produces ..\..\build\publish\FileFox.exe
;   3. Open this file in the Inno Setup Compiler and click Build
;      (or run ISCC.exe installer\setup.iss from the command line).
;   4. The installer is written to build\FileFoxSetup.exe
;
; The installer also doubles as the silent auto-updater: FileFox's own "Check for
; Updates" feature downloads a newer FileFoxSetup.exe and runs it with
; /VERYSILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS, which closes the running
; app, replaces the files in place (same AppId/install folder), and restarts it -
; no wizard, no reinstall from scratch.

#define MyAppName "FileFox"
#define MyAppVersion "2.7.0"
#define MyAppPublisher "FileFox"
#define MyAppExeName "FileFox.exe"
#define MyPublishDir "..\build\publish"

[Setup]
AppId={{8C1E7B2A-4D9F-4A3E-9C2B-1F6E7A9B5D34}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\FileFox
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\build
OutputBaseFilename=FileFoxSetup
SetupIconFile=..\app\FileFox.App\Assets\filefox.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
WizardImageFile=wizard_large.bmp
WizardSmallImageFile=wizard_small.bmp
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
; Picks the language from the user's Windows UI language automatically instead of asking -
; also the fix for a real bug found in testing: without this, Inno still shows the language
; picker even under /VERYSILENT, which made the silent auto-update flow hang forever.
ShowLanguageDialog=no
LanguageDetectionMethod=uilanguage

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "hebrew"; MessagesFile: "compiler:Languages\Hebrew.isl"

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
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "FileFox"; \
    ValueData: """{app}\{#MyAppExeName}"" --minimized"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
; No "skipifsilent" here on purpose: this line also fires during a silent
; auto-update run (/VERYSILENT), so FileFox relaunches itself automatically
; right after the update finishes.
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchAfterInstall}"; Flags: nowait postinstall
