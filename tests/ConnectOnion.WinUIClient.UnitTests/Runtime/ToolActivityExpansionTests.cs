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

    [Fact]
    public void RunningTools_DoNotForceTheCardOpen()
    {
        var activity = new ToolActivityViewModel();
        var projector = new ToolActivityProjector(activity);

        projector.ApplyCall(Event("{\"tool_id\":\"one\",\"name\":\"read_file\",\"args\":{\"path\":\"a.txt\"}}"));
        Assert.False(activity.IsExpanded);

        // A second call must not spring the card back open either — that is what made a
        // mid-turn collapse fail to stick.
        activity.IsExpanded = true;
        activity.ToggleExpanded();
        projector.ApplyCall(Event("{\"tool_id\":\"two\",\"name\":\"search_web\",\"args\":{\"query\":\"x\"}}"));

        Assert.False(activity.IsExpanded);
        Assert.Equal(2, activity.Steps.Count);   // still recorded, just not shown
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
    public void BlockedTool_ForcesTheCardOpen()
    {
        var activity = new ToolActivityViewModel();
        var projector = new ToolActivityProjector(activity);

        projector.AddBlocked(Event("{\"tool\":\"run_command\",\"message\":\"blocked by policy\"}"));

        Assert.True(activity.IsExpanded);
    }

    [Fact]
    public void WaitingOnTheUser_ForcesTheCardOpen()
    {
        var activity = new ToolActivityViewModel();
        var projector = new ToolActivityProjector(activity);

        projector.WaitForConfirmation();

        Assert.True(activity.IsExpanded);
    }

    [Fact]
    public void DetailedDisplayMode_StillExpands()
    {
        var activity = new ToolActivityViewModel { DisplayMode = ToolDisplayMode.Detailed };

        Assert.True(activity.IsExpanded);
    }

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
    public void FailedRun_StartsExpandedButStaysCollapsible()
    {
        var activity = new ToolActivityViewModel();
        var projector = new ToolActivityProjector(activity);

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
