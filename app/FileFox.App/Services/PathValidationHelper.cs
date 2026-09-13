using System.IO;

namespace FileFox.Services;

/// <summary>
/// בדיקות תקינות למסלולי תיקיות לפני שמירת הגדרות. תיקיית יעד שהיא אותה תיקייה כמו תיקיית
/// המעקב (או נמצאת בתוכה) עלולה לגרום ללולאה או לבלגן - עדיף לחסום כבר במסך ההגדרות.
/// </summary>
public static class PathValidationHelper
{
    public static bool IsSameOrNestedFolder(string watchFolder, string outputFolder)
    {
        if (string.IsNullOrWhiteSpace(watchFolder) || string.IsNullOrWhiteSpace(outputFolder))
            return false;

        var normalizedWatch = NormalizePath(watchFolder);
        var normalizedOutput = NormalizePath(outputFolder);

        if (string.Equals(normalizedWatch, normalizedOutput, StringComparison.OrdinalIgnoreCase))
            return true;

        var watchWithSeparator = normalizedWatch.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return normalizedOutput.StartsWith(watchWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>גרסה לרשימת תיקיות מעקב - מחזירה את התיקייה הראשונה שמתנגשת עם תיקיית היעד, אם יש כזו.</summary>
    public static string? FindConflictingWatchFolder(IEnumerable<string> watchFolders, string outputFolder) =>
        watchFolders.FirstOrDefault(folder => IsSameOrNestedFolder(folder, outputFolder));

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
