using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services.Runtime;

namespace ConnectOnion.WinUIClient.UnitTests.Runtime;

public sealed class ToolActivityExpansionTests
{
    [Theory]
    [InlineData(ToolActivityStatus.Failed)]
    [InlineData(ToolActivityStatus.Cancelled)]
    [InlineData(ToolActivityStatus.PartialSuccess)]
    [InlineData(ToolActivityStatus.Success)]
    [InlineData(ToolActivityStatus.Running)]
    [InlineData(ToolActivityStatus.WaitingForConfirmation)]
    [InlineData(ToolActivityStatus.WaitingForPermission)]
    public void ToggleExpanded_DrivesTimelineVisibility_ForEveryStatus(ToolActivityStatus status)
    {
        var activity = new ToolActivityViewModel { Status = status, IsExpanded = true };

        activity.ToggleExpanded();
        Assert.False(activity.IsTimelineVisible);

        activity.ToggleExpanded();
        Assert.True(activity.IsTimelineVisible);
    }

    [Fact]
    public void NewActivity_StartsCollapsed()
    {
        var activity = new ToolActivityViewModel();

        Assert.False(activity.IsExpanded);
        Assert.False(activity.IsTimelineVisible);
    }

    /// <summary>The headline behaviour: a running turn shows its timeline, a finished one folds
    /// it away so the agent's actual reply is not pushed down the page.</summary>
    [Fact]
    public void RunningTools_OpenTheCard_AndCompletionFoldsItAgain()
    {
        var activity = new ToolActivityViewModel();
        var projector = new ToolActivityProjector(activity, isLiveView: true);

        projector.ApplyCall(Event("{\"tool_id\":\"one\",\"name\":\"read_file\",\"args\":{\"path\":\"a.txt\"}}"));
        Assert.True(activity.IsExpanded);

        projector.Complete();
        Assert.False(activity.IsExpanded);
    }

    /// <summary>
    /// The guard that makes expand-while-running liveable. Folding a noisy card mid-turn has to
    /// stick — without the override, the next tool_call throws it back open and the card fights
    /// the user once per tool.
    /// </summary>
    [Fact]
    public void UserCollapseMidRun_IsNotUndoneByTheNextTool()
    {
        var activity = new ToolActivityViewModel();
        var projector = new ToolActivityProjector(activity, isLiveView: true);

        projector.ApplyCall(Event("{\"tool_id\":\"one\",\"name\":\"read_file\",\"args\":{}}"));
        Assert.True(activity.IsExpanded);

        activity.ToggleExpanded();
        Assert.False(activity.IsExpanded);

        projector.ApplyCall(Event("{\"tool_id\":\"two\",\"name\":\"search_web\",\"args\":{\"query\":\"x\"}}"));

        Assert.False(activity.IsExpanded);
        Assert.Equal(2, activity.Steps.Count);   // still recorded, just not shown
    }

    /// <summary>The reverse override: a user who opens a card keeps it open past completion.</summary>
    [Fact]
    public void UserExpandedCard_StaysOpenAfterCompletion()
    {
        var activity = new ToolActivityViewModel();
        var projector = new ToolActivityProjector(activity, isLiveView: true);
        projector.ApplyCall(Event("{\"tool_id\":\"one\",\"name\":\"read_file\",\"args\":{}}"));

        activity.ToggleExpanded();   // closed
        activity.ToggleExpanded();   // and deliberately reopened

        projector.Complete();

        Assert.True(activity.IsExpanded);
    }

    /// <summary>Persisted history must never be written expanded, or reopening an old
    /// conversation replays every past run at full height.</summary>
    [Fact]
    public void RunningTools_StayCollapsedOnTheHeadlessPass()
    {
        var activity = new ToolActivityViewModel();
        var projector = new ToolActivityProjector(activity);   // isLiveView: false

        projector.ApplyCall(Event("{\"tool_id\":\"one\",\"name\":\"read_file\",\"args\":{}}"));

        Assert.False(activity.IsExpanded);
    }

    [Fact]
    public void SuccessfulRun_StaysCollapsedOnCompletion()
    {
        var activity = new ToolActivityViewModel();
        var projector = new ToolActivityProjector(activity);
        projector.ApplyCall(Event("{\"tool_id\":\"one\",\"name\":\"read_file\",\"args\":{}}"));

        projector.Complete();

        Assert.False(activity.IsExpanded);
    }

    [Fact]
    public void BlockedTool_OpensTheCard_OnlyOnALiveView()
    {
        var live = new ToolActivityViewModel();
        new ToolActivityProjector(live, isLiveView: true)
            .AddBlocked(Event("{\"tool\":\"run_command\",\"message\":\"blocked by policy\"}"));
        Assert.True(live.IsExpanded);

        // The persistence pass writes the same card collapsed — reopening the conversation later
        // must not replay the failure at full height.
        var persisted = new ToolActivityViewModel();
        new ToolActivityProjector(persisted)
            .AddBlocked(Event("{\"tool\":\"run_command\",\"message\":\"blocked by policy\"}"));
        Assert.False(persisted.IsExpanded);
    }

    [Fact]
    public void WaitingOnTheUser_OpensTheCard_OnlyOnALiveView()
    {
        // The turn cannot proceed until the user acts, and what the tool was about to do is what
        // the decision hinges on — so an approval opens the card the same way a failure does.
        var live = new ToolActivityViewModel();
        new ToolActivityProjector(live, isLiveView: true).WaitForConfirmation();
        Assert.True(live.IsExpanded);

        var persisted = new ToolActivityViewModel();
        new ToolActivityProjector(persisted).WaitForConfirmation();
        Assert.False(persisted.IsExpanded);
    }

    [Fact]
    public void AnsweredApproval_CollapsesAgainOnASuccessfulCompletion()
    {
        var activity = new ToolActivityViewModel();
        var projector = new ToolActivityProjector(activity, isLiveView: true);
        projector.ApplyCall(Event("{\"tool_id\":\"one\",\"name\":\"send_email\",\"args\":{}}"));
        projector.WaitForConfirmation();
        Assert.True(activity.IsExpanded);

        projector.Complete();

        // The decision is made and the tool succeeded; the card has nothing left to demand.
        Assert.False(activity.IsExpanded);
    }

    [Fact]
    public void DetailedDisplayMode_StillExpands()
    {
        var activity = new ToolActivityViewModel { DisplayMode = ToolDisplayMode.Detailed };

        Assert.True(activity.IsExpanded);
    }

    [Fact]
    public void CollapsedErrorLine_TakesTheLastFailedStep_AndHidesWhenExpanded()
    {
        var activity = new ToolActivityViewModel();
        var projector = new ToolActivityProjector(activity);
        projector.ApplyCall(Event("{\"tool_id\":\"one\",\"name\":\"send_email\",\"args\":{}}"));
        projector.ApplyResult(Result(
            "{\"tool_id\":\"one\",\"status\":\"error\",\"result\":\"smtp refused: 550 relay denied\\nat send()\"}"));

        // First line only — a traceback's remaining lines are one click away in the step's block.
        Assert.Equal("smtp refused: 550 relay denied", activity.LastErrorMessage);
        Assert.True(activity.ShowCollapsedError);

        activity.IsExpanded = true;

        Assert.False(activity.ShowCollapsedError);
    }

    [Fact]
    public void CollapsedErrorLine_IsAbsentWhenNothingFailed()
    {
        var activity = new ToolActivityViewModel();
        var projector = new ToolActivityProjector(activity);
        projector.ApplyCall(Event("{\"tool_id\":\"one\",\"name\":\"read_file\",\"args\":{}}"));
        projector.ApplyResult(Result("{\"tool_id\":\"one\",\"status\":\"success\",\"result\":\"42 rows\"}"));

        Assert.Null(activity.LastErrorMessage);
        Assert.False(activity.ShowCollapsedError);
    }

    private static ConnectOnion.Protocol.AgentStreamEvent Result(string json)
        => new("tool_result", "tool_result", null, json);

    [Fact]
    public void FailedStep_ShowsOneDetailBlockLabelledError()
    {
        // The projector writes the same text into Result and Error on failure, which is why the
        // view renders a single block rather than one card per field.
        var step = new ToolStepViewModel { Result = "connection refused", Error = "connection refused" };

        Assert.Equal("connection refused", step.DetailText);
        Assert.Equal("Error", step.DetailLabel);
        Assert.True(step.HasDetail);
    }

    [Fact]
    public void SucceededStep_ShowsTheResultLabelledResult()
    {
        var step = new ToolStepViewModel { Result = "42 rows" };

        Assert.Equal("42 rows", step.DetailText);
        Assert.Equal("Result", step.DetailLabel);
        Assert.True(step.HasDetail);
    }

    [Fact]
    public void StepWithNoOutput_RendersNoDetailBlock()
    {
        Assert.False(new ToolStepViewModel().HasDetail);
        Assert.False(new ToolStepViewModel { Result = "   " }.HasDetail);
    }

    private static ConnectOnion.Protocol.AgentStreamEvent Event(string json)
        => new("tool_call", "tool_call", null, json);

    [Fact]
    public void FailedRun_OnAPersistencePass_StaysCollapsed()
    {
        var activity = new ToolActivityViewModel();

        new ToolActivityProjector(activity).Complete(ToolActivityStatus.Failed, "Server closed connection");

        Assert.False(activity.IsExpanded);
    }

    [Fact]
    public void FailedStep_OnALiveView_OpensTheCard()
    {
        var activity = new ToolActivityViewModel();
        var projector = new ToolActivityProjector(activity, isLiveView: true);
        projector.ApplyCall(Event("{\"tool_id\":\"one\",\"name\":\"send_email\",\"args\":{}}"));
        projector.ApplyResult(new ConnectOnion.Protocol.AgentStreamEvent(
            "tool_result", "tool_result", null,
            "{\"tool_id\":\"one\",\"name\":\"send_email\",\"status\":\"error\",\"error\":\"smtp refused\"}"));

        projector.Complete();

        // Complete() rolls a single bad step up to PartialSuccess, not Failed — the expansion
        // rule has to look at the steps, or a failed tool would silently stay folded.
        Assert.True(activity.IsExpanded);
    }

    [Fact]
    public void FailedRun_StartsExpandedButStaysCollapsible()
    {
        var activity = new ToolActivityViewModel();
        var projector = new ToolActivityProjector(activity, isLiveView: true);

        // A dropped socket ("Server closed connection") completes the turn as Failed.
        projector.Complete(ToolActivityStatus.Failed, "Server closed connection");

        Assert.True(activity.IsExpanded);
        Assert.True(activity.IsTimelineVisible);

        activity.ToggleExpanded();

        Assert.False(activity.IsExpanded);
        Assert.False(activity.IsTimelineVisible);
        Assert.True(activity.IsVisible); // the failed bubble itself stays on screen
    }
}
