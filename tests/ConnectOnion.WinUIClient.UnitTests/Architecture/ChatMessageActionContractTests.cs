namespace ConnectOnion.WinUIClient.UnitTests.Architecture;

public sealed class ChatMessageActionContractTests
{
    [Fact]
    public void MessageMetadataAndCommands_StayVisibleAndGainEmphasisOnHoverOrFocus()
    {
        var xaml = ReadRepositoryFile("ConnectOnion.WinUIClient", "Views", "ChatPage.xaml");
        var model = ReadRepositoryFile("ConnectOnion.WinUIClient.Core", "Models", "ChatMessage.cs");
        const string opacityBinding = "Opacity=\"{x:Bind UserActionsOpacity, Mode=OneWay}\"";

        Assert.Equal(2, xaml.Split(opacityBinding, StringSplitOptions.None).Length - 1);
        Assert.Contains("ShowUserActions ? 1d : 0.62d", model, StringComparison.Ordinal);
    }

    [Fact]
    public void RetryStaysInCurrentSession_WhileEditBranches()
    {
        var actions = ReadRepositoryFile(
            "ConnectOnion.WinUIClient", "Views", "ChatPage.MessageActions.cs");
        var conversations = ReadRepositoryFile(
            "ConnectOnion.WinUIClient", "ViewModels", "ChatViewModel.Conversation.cs");

        Assert.Contains("Vm.RetryUserMessageAsync(message)", actions, StringComparison.Ordinal);
        Assert.Contains("Vm.RetryFromAgentMessageAsync(message)", actions, StringComparison.Ordinal);
        Assert.Contains("Vm.BranchFromMessageAsync(message, replacement)", actions, StringComparison.Ordinal);
        Assert.Contains("await SendAsync(source.Content);", conversations, StringComparison.Ordinal);
        Assert.Contains("return RetryUserMessageAsync(source);", conversations, StringComparison.Ordinal);
        Assert.Contains("_session = SessionSummary.NewConversation", conversations, StringComparison.Ordinal);
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
