using ConnectOnion.WinUIClient.Models;

namespace ConnectOnion.WinUIClient.UnitTests.Models;

public sealed class ChatMessageTests
{
    [Fact]
    public void InteractiveOutcome_RunningCard_ReturnsWaitingState()
    {
        var message = InteractiveMessage(EventStatus.Running);

        Assert.True(message.IsInteractiveExpanded);
        Assert.Equal("Waiting for you", message.InteractiveOutcome);
    }

    [Theory]
    [InlineData("Approved once")]
    [InlineData("Rejected")]
    [InlineData("Changes requested")]
    public void InteractiveOutcome_ResolvedCard_PreservesAnswerAndCollapses(string outcome)
    {
        var message = InteractiveMessage(EventStatus.Running);
        message.EventMeta = outcome;

        message.Status = EventStatus.Done;

        Assert.True(message.IsInteractiveCollapsed);
        Assert.Equal(outcome, message.InteractiveOutcome);
        Assert.Contains(outcome, message.InteractiveAccessibilityName);
    }

    [Fact]
    public void InteractiveOutcome_ResolvedWithoutAnswer_ReturnsNoSelection()
    {
        var message = InteractiveMessage(EventStatus.Done);

        Assert.Equal("No selection", message.InteractiveOutcome);
        Assert.False(message.ShowAskUserFreeText);
    }

    [Fact]
    public void ToggleInteractiveExpanded_ResolvedCard_AllowsHistoryInspection()
    {
        var message = InteractiveMessage(EventStatus.Done);

        message.ToggleInteractiveExpanded();

        Assert.True(message.IsInteractiveExpanded);
        Assert.Contains("Collapse", message.InteractiveAccessibilityName);
    }

    private static ChatMessage InteractiveMessage(EventStatus status) => new()
    {
        Role = ChatRole.Event,
        EventKind = "approval",
        EventEyebrow = "Approval needed",
        EventTitle = "Run command",
        Status = status,
    };
}
