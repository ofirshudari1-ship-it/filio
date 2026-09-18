using System.Diagnostics;
using System.IO;

namespace Filio.Services;

public enum DefenderScanResult
{
    /// <summary>Defender בדק את הקובץ ולא מצא בו איום.</summary>
    Clean,

    /// <summary>Defender מזהה את הקובץ כאיום - אסור לתייק אותו.</summary>
    ThreatFound,

    /// <summary>לא ניתן היה להריץ סריקה (Defender לא מותקן/כבוי, MpCmdRun.exe לא נמצא,
    /// תהליך הסריקה נכשל). לא אומר שהקובץ בטוח - רק שלא הצלחנו לבדוק.</summary>
    Unavailable
}

/// <summary>
/// Filio לא בונה מנוע אנטי-וירוס משלו - זה היה תובע אמינות שאין לנו. במקום זה, לפני
/// שקובץ מתויק, השירות הזה שואל את Windows Defender (האנטי-וירוס המובנה והחינמי שכבר
/// מותקן ורץ בכל Windows 10/11) מה הוא כבר יודע על הקובץ, באמצעות סריקה חד-פעמית ממוקדת
/// דרך כלי שורת הפקודה שלו (MpCmdRun.exe) - לא היוריסטיקה מומצאת, לא קריאה לענן.
/// </summary>
public static class WindowsDefenderScanService
{
    private const int ScanTimeoutMs = 60_000;

    public static DefenderScanResult ScanFile(string filePath)
    {
        try
        {
            var mpCmdRunPath = FindMpCmdRun();
            if (mpCmdRunPath == null || !File.Exists(filePath))
                return DefenderScanResult.Unavailable;

            // ScanType 3 = custom scan של נתיב ספציפי. DisableRemediation מוודא שהסריקה
            // החד-פעמית הזו רק *מדווחת* על איום ולא מנסה לנקות/למחוק בעצמה - הגנת הזמן-אמת
            // הרגילה של Defender (אם פעילה) עדיין תטפל בקובץ בדרכה שלה; Filio רק מחליט,
            // על סמך התוצאה, אם מותר לו לגעת בקובץ ולהזיז אותו.
            var psi = new ProcessStartInfo(mpCmdRunPath,
                $"-Scan -ScanType 3 -File \"{filePath}\" -DisableRemediation")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return DefenderScanResult.Unavailable;

            if (!process.WaitForExit(ScanTimeoutMs))
            {
                TryKill(process);
                return DefenderScanResult.Unavailable;
            }

            // קודי היציאה התיעודיים של MpCmdRun: 0 = לא נמצא איום, 2 = נמצא איום.
            // כל קוד אחר (למשל שגיאת הרשאות) - לא ידוע לנו, מתייחסים כ"לא ניתן לבדוק".
            return process.ExitCode switch
            {
                0 => DefenderScanResult.Clean,
                2 => DefenderScanResult.ThreatFound,
                _ => DefenderScanResult.Unavailable
            };
        }
        catch
        {
            return DefenderScanResult.Unavailable;
        }
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
    }

    private static string? FindMpCmdRun()
    {
        // מיקום ההתקנה הרגיל (Windows 10/11 עם Defender כברירת מחדל).
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var standardPath = Path.Combine(programFiles, "Windows Defender", "MpCmdRun.exe");
        if (File.Exists(standardPath))
            return standardPath;

        // התקנות עם עדכוני "Platform" (הנפוץ יותר במכונות מעודכנות): הקובץ יושב תחת
        // תיקיית גרסה בתוך ProgramData\Microsoft\Windows Defender\Platform\<version>\.
        try
        {
            var platformRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Microsoft", "Windows Defender", "Platform");

            if (!Directory.Exists(platformRoot))
                return null;

            var newestVersionDir = new DirectoryInfo(platformRoot).GetDirectories()
                .OrderByDescending(d => Version.TryParse(d.Name, out var v) ? v : new Version(0, 0))
                .FirstOrDefault();

            if (newestVersionDir == null)
                return null;

            var candidate = Path.Combine(newestVersionDir.FullName, "MpCmdRun.exe");
            return File.Exists(candidate) ? candidate : null;
        }
        catch
        {
            return null;
        }
    }
}
