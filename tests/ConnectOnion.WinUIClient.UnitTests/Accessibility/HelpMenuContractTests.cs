using System.Text.RegularExpressions;

namespace ConnectOnion.WinUIClient.UnitTests.Accessibility;

public sealed class HelpMenuContractTests
{
    [Fact]
    public void MainWindowXaml_HelpMenu_ContainsRequiredCommandsInOrder()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "ConnectOnion.WinUIClient", "MainWindow.xaml"));
        var shortcuts = xaml.IndexOf("Text=\"Keyboard shortcuts\"", StringComparison.Ordinal);
        var docs = xaml.IndexOf("Text=\"ConnectOnion Docs\"", StringComparison.Ordinal);
        var about = xaml.IndexOf("Text=\"About ConnectOnion\"", StringComparison.Ordinal);

        Assert.True(shortcuts >= 0);
        Assert.True(docs > shortcuts);
        Assert.True(about > docs);
    }

    [Fact]
    public void HelpMenuCode_DocsCommand_UsesCanonicalUrlAndIndependentHandlers()
    {
        var root = FindRepositoryRoot();
        var code = File.ReadAllText(Path.Combine(root, "ConnectOnion.WinUIClient", "MainWindow.HelpMenu.cs"));

        Assert.Contains("https://docs.connectonion.com/", code, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"ConnectOnionDocs_Click[\s\S]*?UriLauncher\.LaunchAsync\(new Uri\(DocsUrl\)\)", RegexOptions.CultureInvariant), code);
        Assert.Contains("AboutConnectOnion_Click", code, StringComparison.Ordinal);
        Assert.Contains("ShowAboutOverlay", code, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "ConnectOnion.WinUIClient"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
