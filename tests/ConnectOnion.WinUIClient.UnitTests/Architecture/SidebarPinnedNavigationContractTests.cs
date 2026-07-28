namespace ConnectOnion.WinUIClient.UnitTests.Architecture;

public sealed class SidebarPinnedNavigationContractTests
{
    [Fact]
    public void PinnedRows_CarryTheActiveConversationSelection()
    {
        var source = ReadAppSource("Controls", "Shell", "ShellSidebar.xaml.cs");

        Assert.Contains(
            "IsSelected = IsSessionSelected(session.Id, activeSessionId)",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("!isPinnedShortcut", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PinnedNavigation_RevealsItsAgentWithoutCollapsingOtherBranches()
    {
        var source = ReadAppSource("Controls", "Shell", "ShellSidebar.xaml.cs");
        var events = ReadAppSource("Controls", "Shell", "ShellSidebar.Events.cs");

        Assert.Contains("RevealAgentInSidebar(session.AgentId)", source, StringComparison.Ordinal);
        Assert.Contains("_expandedAgentIds.Add(agentId)", source, StringComparison.Ordinal);
        Assert.Contains("agent.IsExpanded = true", source, StringComparison.Ordinal);
        Assert.Contains("preserveOtherExpandedAgents: true", events, StringComparison.Ordinal);
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
