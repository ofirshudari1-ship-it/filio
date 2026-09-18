using System.IO;
using Filio.Services;
using Xunit;

namespace Filio.Tests;

public class PathValidationHelperTests
{
    [Fact]
    public void IsSameOrNestedFolder_TrueForIdenticalPath()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Downloads");
        Assert.True(PathValidationHelper.IsSameOrNestedFolder(folder, folder));
    }

    [Fact]
    public void IsSameOrNestedFolder_TrueWhenOutputIsInsideWatchFolder()
    {
        var watch = Path.Combine(Path.GetTempPath(), "Downloads");
        var output = Path.Combine(watch, "תיוק אוטומטי");

        Assert.True(PathValidationHelper.IsSameOrNestedFolder(watch, output));
    }

    [Fact]
    public void IsSameOrNestedFolder_FalseForSiblingFolders()
    {
        var watch = Path.Combine(Path.GetTempPath(), "Downloads");
        var output = Path.Combine(Path.GetTempPath(), "Documents", "תיוק אוטומטי");

        Assert.False(PathValidationHelper.IsSameOrNestedFolder(watch, output));
    }

    [Fact]
    public void IsSameOrNestedFolder_DoesNotFalsePositiveOnSimilarPrefix()
    {
        // "Downloads2" מתחיל ב"Downloads" אבל הוא תיקייה נפרדת לגמרי - לא אמור להיחסם
        var watch = Path.Combine(Path.GetTempPath(), "Downloads");
        var output = Path.Combine(Path.GetTempPath(), "Downloads2", "תיוק אוטומטי");

        Assert.False(PathValidationHelper.IsSameOrNestedFolder(watch, output));
    }

    [Fact]
    public void IsSameOrNestedFolder_FalseWhenEitherPathIsEmpty()
    {
        Assert.False(PathValidationHelper.IsSameOrNestedFolder("", @"C:\Somewhere"));
        Assert.False(PathValidationHelper.IsSameOrNestedFolder(@"C:\Somewhere", ""));
    }

    [Fact]
    public void FindConflictingWatchFolder_ReturnsTheConflictingOneAmongSeveral()
    {
        var downloads = Path.Combine(Path.GetTempPath(), "Downloads");
        var desktop = Path.Combine(Path.GetTempPath(), "Desktop");
        var output = Path.Combine(desktop, "Filio"); // מתנגש עם Desktop, לא עם Downloads

        var conflict = PathValidationHelper.FindConflictingWatchFolder(new[] { downloads, desktop }, output);

        Assert.Equal(desktop, conflict);
    }

    [Fact]
    public void FindConflictingWatchFolder_ReturnsNullWhenNoneConflict()
    {
        var downloads = Path.Combine(Path.GetTempPath(), "Downloads");
        var desktop = Path.Combine(Path.GetTempPath(), "Desktop");
        var output = Path.Combine(Path.GetTempPath(), "Documents", "Filio");

        var conflict = PathValidationHelper.FindConflictingWatchFolder(new[] { downloads, desktop }, output);

        Assert.Null(conflict);
    }
}
