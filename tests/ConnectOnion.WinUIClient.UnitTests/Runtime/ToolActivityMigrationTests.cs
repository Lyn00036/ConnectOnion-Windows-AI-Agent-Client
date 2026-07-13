using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services.Runtime;

namespace ConnectOnion.WinUIClient.UnitTests.Runtime;

public sealed class ToolActivityMigrationTests
{
    [Fact]
    public void UpgradeLegacyToolMessages_ConsecutiveRows_CollapsesIntoTimeline()
    {
        var source = new[]
        {
            Legacy(1, "read_file", EventStatus.Done),
            Legacy(2, "run_command", EventStatus.Error),
        };

        var upgraded = ToolActivityMigration.UpgradeLegacyToolMessages(source);

        var message = Assert.Single(upgraded);
        Assert.Equal("tool_activity", message.EventKind);
        Assert.Equal(2, message.ToolActivity!.Steps.Count);
        Assert.Equal(ToolStepStatus.Failed, message.ToolActivity.Steps[1].Status);
        Assert.Equal(ToolActivityStatus.PartialSuccess, message.ToolActivity.Status);
    }

    [Fact]
    public void UpgradeLegacyToolMessages_NonToolRows_PreservesOriginalInstanceAndOrder()
    {
        var user = new ChatMessage { Id = 1, Role = ChatRole.User, Content = "hello" };
        var agent = new ChatMessage { Id = 3, Role = ChatRole.Agent, Content = "done" };
        var source = new[] { user, Legacy(2, "read_file", EventStatus.Done), agent };

        var upgraded = ToolActivityMigration.UpgradeLegacyToolMessages(source);

        Assert.Equal(3, upgraded.Count);
        Assert.Same(user, upgraded[0]);
        Assert.Equal("tool_activity", upgraded[1].EventKind);
        Assert.Same(agent, upgraded[2]);
    }

    private static ChatMessage Legacy(long id, string tool, EventStatus status) => new()
    {
        Id = id,
        Role = ChatRole.Event,
        EventKind = "tool",
        EventKey = "legacy-" + id,
        EventTitle = tool,
        EventArgs = "{\"token\":\"should-hide\"}",
        EventResult = status == EventStatus.Error ? "failed" : "ok",
        EventMeta = "1.5s",
        Status = status,
    };
}
