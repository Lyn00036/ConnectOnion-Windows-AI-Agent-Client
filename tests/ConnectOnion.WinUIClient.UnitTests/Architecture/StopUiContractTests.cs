namespace ConnectOnion.WinUIClient.UnitTests.Architecture;

public sealed class StopUiContractTests
{
    [Fact]
    public void ApprovalSurface_ExposesFiveProtocolActionsWithoutAFeedbackInput()
    {
        var xaml = ReadRepositoryFile("ConnectOnion.WinUIClient", "Controls", "Chat", "ToolActivityView.xaml");

        Assert.Contains("AllowOnceCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("TrustSessionCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("RejectCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("StopCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ExplainCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("Explain why", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TextBox Header=\"Reason for declining", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Composer_DisablesStopWhileStopping_AndShowsProgress()
    {
        var source = ReadRepositoryFile("ConnectOnion.WinUIClient", "Controls", "Chat", "ChatComposer.xaml.cs");

        Assert.Contains("StopButton.IsEnabled = CanStop && !IsStopping", source, StringComparison.Ordinal);
        Assert.Contains("StopRing.IsActive = IsStopping", source, StringComparison.Ordinal);
        Assert.Contains("Stopping...", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalStop_AlwaysUsesInterrupt_AndInputRemainsGatedByTheActiveRun()
    {
        var manager = ReadRepositoryFile(
            "ConnectOnion.WinUIClient", "Services", "Runtime", "AgentSessionManager.cs");
        var viewModel = ReadRepositoryFile(
            "ConnectOnion.WinUIClient", "ViewModels", "ChatViewModel.cs");

        Assert.Contains("await connection.SendInterruptAsync()", manager, StringComparison.Ordinal);
        Assert.DoesNotContain("RejectApprovalForStopAsync", manager, StringComparison.Ordinal);
        Assert.Contains("!IsProcessing", viewModel, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null)
        {
            var candidate = Path.Combine([root.FullName, .. relativeParts]);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            root = root.Parent;
        }
        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, relativeParts));
    }
}
