using System.IO;
using Filio.Services;
using Xunit;

namespace Filio.Tests;

public class DuplicateDetectionServiceTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "FilioDupTests_" + Guid.NewGuid().ToString("N"));
    private readonly string _indexPath;

    public DuplicateDetectionServiceTests()
    {
        Directory.CreateDirectory(_sandbox);
        _indexPath = Path.Combine(_sandbox, "index.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_sandbox))
            Directory.Delete(_sandbox, recursive: true);
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_sandbox, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void FindExistingDuplicate_ReturnsNullWhenNothingIndexedYet()
    {
        var service = new DuplicateDetectionService(_indexPath);
        var file = WriteFile("a.pdf", "some content");

        Assert.Null(service.FindExistingDuplicate(file));
    }

    [Fact]
    public void RememberThenFind_DetectsIdenticalContentUnderADifferentName()
    {
        var service = new DuplicateDetectionService(_indexPath);
        var original = WriteFile("invoice.pdf", "identical bytes");
        service.RememberFiledFile(original);

        var laterCopy = WriteFile("invoice (downloaded again).pdf", "identical bytes");

        Assert.Equal(original, service.FindExistingDuplicate(laterCopy));
    }

    [Fact]
    public void FindExistingDuplicate_ReturnsNullForDifferentContent()
    {
        var service = new DuplicateDetectionService(_indexPath);
        service.RememberFiledFile(WriteFile("a.pdf", "content A"));

        var different = WriteFile("b.pdf", "content B");

        Assert.Null(service.FindExistingDuplicate(different));
    }

    [Fact]
    public void Index_PersistsAcrossServiceInstances()
    {
        var original = WriteFile("invoice.pdf", "persisted content");
        new DuplicateDetectionService(_indexPath).RememberFiledFile(original);

        // מדמה הפעלה חדשה של Filio - צריך לטעון את האינדקס השמור מהדיסק
        var reloaded = new DuplicateDetectionService(_indexPath);
        var laterCopy = WriteFile("invoice-copy.pdf", "persisted content");

        Assert.Equal(original, reloaded.FindExistingDuplicate(laterCopy));
    }

    [Fact]
    public void FindExistingDuplicate_ReturnsNullIfIndexedFileWasSinceDeleted()
    {
        var service = new DuplicateDetectionService(_indexPath);
        var original = WriteFile("temp.pdf", "will be deleted");
        service.RememberFiledFile(original);
        File.Delete(original);

        var laterCopy = WriteFile("temp-copy.pdf", "will be deleted");

        Assert.Null(service.FindExistingDuplicate(laterCopy));
    }
}
