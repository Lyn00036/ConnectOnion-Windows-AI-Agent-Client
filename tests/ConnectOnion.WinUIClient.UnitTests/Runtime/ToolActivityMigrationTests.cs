using System.Globalization;
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

    // The legacy writer was `ms < 1000 ? $"{ms:0} ms" : $"{ms / 1000:0.0} s"`, so these two
    // shapes are the only ones real data can contain. "340 ms" used to come back as 340000
    // because the seconds check matched the "s" inside "ms".
    [Theory]
    [InlineData("340 ms", 340d)]
    [InlineData("999 ms", 999d)]
    [InlineData("1.5 s", 1500d)]
    [InlineData("12.0 s", 12000d)]
    public void UpgradeLegacyToolMessages_LegacyDurationMeta_ParsesToMilliseconds(string meta, double expectedMs)
    {
        var row = Legacy(1, "read_file", EventStatus.Done);
        row.EventMeta = meta;

        var upgraded = ToolActivityMigration.UpgradeLegacyToolMessages(new[] { row });

        var step = Assert.Single(upgraded[0].ToolActivity!.Steps);
        Assert.Equal(expectedMs, step.DurationMs);
    }

    [Fact]
    public void UpgradeLegacyToolMessages_DecimalMeta_ParsesUnderACommaDecimalCulture()
    {
        // The meta string always used '.' as the decimal point, but parsing used the ambient
        // culture — so under a comma-decimal locale "1.5 s" parsed as 15 seconds.
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var row = Legacy(1, "read_file", EventStatus.Done);
            row.EventMeta = "1.5 s";

            var upgraded = ToolActivityMigration.UpgradeLegacyToolMessages(new[] { row });

            Assert.Equal(1500d, Assert.Single(upgraded[0].ToolActivity!.Steps).DurationMs);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void UpgradeLegacyToolMessages_UnparseableMeta_LeavesDurationUnset()
    {
        var row = Legacy(1, "read_file", EventStatus.Done);
        row.EventMeta = "unknown";

        var upgraded = ToolActivityMigration.UpgradeLegacyToolMessages(new[] { row });

        Assert.Null(Assert.Single(upgraded[0].ToolActivity!.Steps).DurationMs);
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
