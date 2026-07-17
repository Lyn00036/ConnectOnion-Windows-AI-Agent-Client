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

    [Fact]
    public void ThinkingActivity_UsesThoughtProcessTimelinePresentation()
    {
        var message = new ChatMessage
        {
            Role = ChatRole.Event,
            EventKind = "activity",
            EventTitle = "Thinking",
            EventDetail = "Checking the request before replying.",
            Status = EventStatus.Done,
        };

        Assert.Equal("Thought process", message.ActivityHeaderTitle);
        Assert.Equal("Checking the request before replying.", message.ActivityTimelineText);
        Assert.Equal("Done", message.ActivityStatusLabel);
        Assert.False(message.IsActivityExpanded);

        message.ToggleActivityExpanded();

        Assert.True(message.IsActivityExpanded);
        Assert.Contains("Collapse", message.ActivityAccessibilityName);
    }

    [Fact]
    public void ThinkingActivity_WhileRunning_ShowsLoadingHeaderAndStaysCollapsed()
    {
        var message = new ChatMessage
        {
            Role = ChatRole.Event,
            EventKind = "activity",
            EventTitle = "Thinking",
            Status = EventStatus.Running,
        };

        Assert.True(message.IsThinkingRunning);
        Assert.Equal("Thinking...", message.ActivityHeaderTitle);
        Assert.False(message.IsActivityExpanded);

        message.Status = EventStatus.Done;

        Assert.False(message.IsThinkingRunning);
        Assert.Equal("Thought process", message.ActivityHeaderTitle);
    }

    [Fact]
    public void UsageActivity_UsesTurnUsageHeaderAndKeepsMetrics()
    {
        var message = new ChatMessage
        {
            Role = ChatRole.Event,
            EventKind = "activity",
            EventKey = "turn_usage",
            EventTitle = "model-a",
            EventMeta = "120→30 tok",
            Status = EventStatus.Done,
        };

        Assert.Equal("Turn usage", message.ActivityHeaderTitle);
        Assert.Equal("model-a", message.ActivityTimelineText);
        Assert.True(message.HasActivityTimelineMeta);
        Assert.Equal("model-a · 120→30 tok", message.TurnUsageSummary);
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
