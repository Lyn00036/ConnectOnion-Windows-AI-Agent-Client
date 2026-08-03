namespace ConnectOnion.WinUIClient.UnitTests.Architecture;

public sealed class AgentDeletionNavigationContractTests
{
    [Fact]
    public void DeletingTheCurrentAgent_ResetsToTheLibraryInsteadOfOpeningAnEmptyChat()
    {
        var sidebar = ReadAppSource("Controls", "Shell", "ShellSidebar.Events.cs");
        var window = ReadAppSource("MainWindow.xaml.cs");
        var deleteStart = sidebar.IndexOf(
            "internal async System.Threading.Tasks.Task<bool> DeleteAgentAsync",
            StringComparison.Ordinal);
        var deleteEnd = sidebar.IndexOf(
            "private async void Session_Click",
            deleteStart,
            StringComparison.Ordinal);
        Assert.True(deleteStart >= 0 && deleteEnd > deleteStart);
        var deleteMethod = sidebar[deleteStart..deleteEnd];

        Assert.Contains("var deletingCurrentSurface =", deleteMethod, StringComparison.Ordinal);
        Assert.Contains(
            "RequestNavigationReset(typeof(HomePage))",
            deleteMethod,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "RequestNavigation(typeof(ChatPage)",
            deleteMethod,
            StringComparison.Ordinal);

        Assert.Contains(
            "ShellSidebar.NavigationResetRequested += ResetNavigationTo",
            window,
            StringComparison.Ordinal);
        Assert.Contains(
            "ShellSidebar.NavigationResetRequested -= ResetNavigationTo",
            window,
            StringComparison.Ordinal);
        Assert.Contains("ContentFrame.BackStack.Clear()", window, StringComparison.Ordinal);
        Assert.Contains("ContentFrame.ForwardStack.Clear()", window, StringComparison.Ordinal);
    }

    private static string ReadAppSource(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var root = Path.Combine(directory.FullName, "ConnectOnion.WinUIClient");
            if (Directory.Exists(root))
                return File.ReadAllText(Path.Combine([root, .. relativeParts]));
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the WinUI app source directory.");
    }
}
