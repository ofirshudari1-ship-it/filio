using System.Collections.ObjectModel;
using Filio.Models;
using Xunit;

namespace Filio.Tests;

public class AppSettingsTests
{
    [Fact]
    public void CopyFrom_UpdatesScalarPropertiesInPlace()
    {
        var target = AppSettings.CreateDefault();
        var source = new AppSettings
        {
            RootOutputFolder = @"C:\Somewhere\Else",
            MoveInsteadOfCopy = false,
            DetectDuplicates = false,
            WatchedFileExtensions = ".pdf",
            IgnoredFileNamePatterns = "draft",
            FolderOrder = FolderStructureOrder.YearClientType,
            Language = "he"
        };

        target.CopyFrom(source);

        Assert.Equal(@"C:\Somewhere\Else", target.RootOutputFolder);
        Assert.False(target.MoveInsteadOfCopy);
        Assert.False(target.DetectDuplicates);
        Assert.Equal(".pdf", target.WatchedFileExtensions);
        Assert.Equal("draft", target.IgnoredFileNamePatterns);
        Assert.Equal(FolderStructureOrder.YearClientType, target.FolderOrder);
        Assert.Equal("he", target.Language);
    }

    [Fact]
    public void CopyFrom_ReplacesCollectionContentsWithoutReplacingTheInstance()
    {
        var target = new AppSettings();
        var originalClientsInstance = target.Clients;
        var originalWatchFoldersInstance = target.WatchFolders;

        var source = new AppSettings
        {
            WatchFolders = new ObservableCollection<string> { @"C:\A", @"C:\B" }
        };
        source.Clients.Add(new ClientProfile { Name = "Acme" });
        source.DocTypeRules.Add(new DocTypeRule { TypeName = "Invoices" });

        target.CopyFrom(source);

        // אותו מופע אובייקט בדיוק - כדי ש-DataGrid/ListBox שכבר עשו Bind אליו ימשיכו לעבוד
        Assert.Same(originalClientsInstance, target.Clients);
        Assert.Same(originalWatchFoldersInstance, target.WatchFolders);

        Assert.Equal(new[] { @"C:\A", @"C:\B" }, target.WatchFolders);
        Assert.Single(target.Clients);
        Assert.Equal("Acme", target.Clients[0].Name);
        Assert.Single(target.DocTypeRules);
    }

    [Fact]
    public void CopyFrom_DoesNotTouchHasCompletedOnboarding()
    {
        var target = new AppSettings { HasCompletedOnboarding = true };
        var source = new AppSettings { HasCompletedOnboarding = false };

        target.CopyFrom(source);

        // ייבוא הגדרות ממחשב אחר לא אמור להחזיר את אשף ההגדרה על מכונה שכבר עברה אותו
        Assert.True(target.HasCompletedOnboarding);
    }
}
