namespace ConnectOnion.WinUIClient.UnitTests.Architecture;

public sealed class SidebarSelectionRefreshContractTests
{
    [Fact]
    public void SamePageAgentNavigation_PaintsSelectionBeforeWaitingForPageReload()
    {
        var source = ReadAppSource("MainWindow.xaml.cs");
        var samePageStart = source.IndexOf("if (forceReload", StringComparison.Ordinal);
        var nextNavigationBranch = source.IndexOf(
            "if (forceReload || ContentFrame.CurrentSourcePageType != page)",
            samePageStart,
            StringComparison.Ordinal);
        var samePageBranch = source[samePageStart..nextNavigationBranch];

        var sidebarRefresh = samePageBranch.IndexOf(
            "await ShellSidebar.RefreshAsync()",
            StringComparison.Ordinal);
        var pageReload = samePageBranch.IndexOf(
            "await reloadable.ReloadAsync()",
            StringComparison.Ordinal);

        Assert.True(sidebarRefresh >= 0, "The same-page path must refresh sidebar selection.");
        Assert.True(pageReload >= 0, "The same-page path must reload the current page.");
        Assert.True(
            sidebarRefresh < pageReload,
            "Sidebar selection must render before AgentDetailPage waits for /info.");
    }

    [Fact]
    public void SidebarRefresh_DropsResultsFromSupersededAttempts()
    {
        var source = ReadAppSource("Controls", "Shell", "ShellSidebar.xaml.cs");

        Assert.Contains("Interlocked.Increment(ref _refreshGeneration)", source, StringComparison.Ordinal);
        Assert.Contains("IsRefreshSuperseded(refreshGeneration)", source, StringComparison.Ordinal);

        var finalAwait = source.IndexOf(
            "await AppServices.SidebarState.LoadAsync()",
            StringComparison.Ordinal);
        var revisionCommit = source.IndexOf("_lastLoadedRevision = revision", StringComparison.Ordinal);
        Assert.True(finalAwait >= 0 && revisionCommit > finalAwait);
    }

    [Fact]
    public void SidebarRenderSignature_IncludesUnreadAndAttentionBadges()
    {
        var source = ReadAppSource("Controls", "Shell", "ShellSidebar.xaml.cs");
        var addSessionsStart = source.IndexOf(
            "void AddSessions(IReadOnlyList<SessionSummary> sessions)",
            StringComparison.Ordinal);
        Assert.True(addSessionsStart >= 0, "The session render-signature helper was not found.");

        var addSessions = source[addSessionsStart..];
        Assert.Contains("hash.Add(session.UnreadCount)", addSessions, StringComparison.Ordinal);
        Assert.Contains("hash.Add(session.RequiresAttention)", addSessions, StringComparison.Ordinal);
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
