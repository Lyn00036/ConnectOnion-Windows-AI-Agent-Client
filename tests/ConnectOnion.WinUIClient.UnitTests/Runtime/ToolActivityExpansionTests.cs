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
