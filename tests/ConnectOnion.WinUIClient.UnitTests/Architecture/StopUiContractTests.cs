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
    public void Composer_StopIsImmediate_AndRunningTurnKeepsInputAvailable()
    {
        var source = ReadRepositoryFile("ConnectOnion.WinUIClient", "Controls", "Chat", "ChatComposer.xaml.cs");
        var xaml = ReadRepositoryFile("ConnectOnion.WinUIClient", "Controls", "Chat", "ChatComposer.xaml");
        var page = ReadRepositoryFile("ConnectOnion.WinUIClient", "Views", "ChatPage.xaml");

        Assert.Contains("StopButton.IsEnabled = CanStop", source, StringComparison.Ordinal);
        Assert.Contains("InputBox.IsEnabled = CanSubmit", source, StringComparison.Ordinal);
        Assert.Contains("var canSend = CanSubmit && !isAttachmentsBusy", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Stopping...", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StopRing", xaml, StringComparison.Ordinal);
        Assert.Contains("IsBusy=\"{x:Bind Vm.IsComposerBusy", page, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalStop_AlwaysUsesInterrupt_AndRuntimeInputReusesTheActiveSocket()
    {
        var manager = ReadRepositoryFile(
            "ConnectOnion.WinUIClient", "Services", "Runtime", "AgentSessionManager.cs");
        var viewModel = ReadRepositoryFile(
            "ConnectOnion.WinUIClient", "ViewModels", "ChatViewModel.cs");
        var runViewModel = ReadRepositoryFile(
            "ConnectOnion.WinUIClient", "ViewModels", "ChatViewModel.Run.cs");

        Assert.Contains("await connection.SendInterruptAsync()", manager, StringComparison.Ordinal);
        Assert.DoesNotContain("RejectApprovalForStopAsync", manager, StringComparison.Ordinal);
        Assert.Contains("await connection.SendRuntimeInputAsync(", manager, StringComparison.Ordinal);
        Assert.Contains("var isRuntimeInput = IsProcessing;", runViewModel, StringComparison.Ordinal);
        Assert.Contains("public bool CanSend => HasAgent && _session is not null && IsOnline && !IsConnecting",
            viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void StopButton_IsReachableWhileAQueuedSendCanStillBeCancelled()
    {
        var source = ReadRepositoryFile(
            "ConnectOnion.WinUIClient", "ViewModels", "ChatViewModel.Run.cs");

        Assert.Contains(
            "snapshot.Status is ConversationRunStatus.Queued or ConversationRunStatus.Connecting",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "snapshot.Status == ConversationRunStatus.Running || CanCancelSend",
            source,
            StringComparison.Ordinal);
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
