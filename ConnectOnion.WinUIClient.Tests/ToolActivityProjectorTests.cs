using ConnectOnion.Protocol;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services.Runtime;
using System.Text.Json;

namespace ConnectOnion.WinUIClient.Tests;

public class ToolActivityProjectorTests
{
    private static AgentStreamEvent Event(string type, string json, string? id = null) => new(type, type, id, json);
    private static ToolActivityProjector Create() => new(new ToolActivityViewModel());

    [Fact]
    public void SingleToolCall_CreatesRunningStep()
    {
        var projector = Create();
        projector.ApplyCall(Event("tool_call", """{"tool_id":"a","name":"open_browser","args":{}}"""));
        Assert.Single(projector.Activity.Steps);
        Assert.Equal("Open browser", projector.Activity.Steps[0].DisplayName);
        Assert.Equal(ToolStepStatus.Running, projector.Activity.Steps[0].Status);
    }

    [Fact]
    public void MultipleCalls_AreOneActivityAndKeepOrder()
    {
        var projector = Create();
        projector.ApplyCall(Event("tool_call", """{"tool_id":"a","name":"open_browser","args":{}}"""));
        projector.ApplyCall(Event("tool_call", """{"tool_id":"b","name":"get_text","args":{}}"""));
        Assert.Equal(2, projector.Activity.Steps.Count);
        Assert.Equal(new[] { 1, 2 }, projector.Activity.Steps.Select(step => step.Sequence));
    }

    [Fact]
    public void SameToolName_DoesNotMergeDifferentIds()
    {
        var projector = Create();
        projector.ApplyCall(Event("tool_call", """{"tool_id":"a","name":"get_text","args":{}}"""));
        projector.ApplyCall(Event("tool_call", """{"tool_id":"b","name":"get_text","args":{}}"""));
        Assert.Equal(2, projector.Activity.Steps.Count);
    }

    [Fact]
    public void Result_UpdatesOnlyMatchingCallId()
    {
        var projector = Create();
        projector.ApplyCall(Event("tool_call", """{"tool_id":"a","name":"get_text","args":{}}"""));
        projector.ApplyCall(Event("tool_call", """{"tool_id":"b","name":"get_text","args":{}}"""));
        projector.ApplyResult(Event("tool_result", """{"tool_id":"b","status":"ok","result":"eight lines","timing_ms":400}"""));
        Assert.Equal(ToolStepStatus.Running, projector.Activity.Steps[0].Status);
        Assert.Equal(ToolStepStatus.Success, projector.Activity.Steps[1].Status);
    }

    [Fact]
    public void MissingResultId_DoesNotGuessByToolName()
    {
        var projector = Create();
        projector.ApplyCall(Event("tool_call", """{"tool_id":"a","name":"get_text","args":{}}"""));
        projector.ApplyResult(Event("tool_result", """{"name":"get_text","status":"ok","result":"text"}"""));
        Assert.Equal(ToolStepStatus.Running, projector.Activity.Steps[0].Status);
    }

    [Fact]
    public void DuplicateCallFrame_DoesNotCreateDuplicateStep()
    {
        var projector = Create();
        var call = Event("tool_call", """{"tool_id":"a","name":"get_text","args":{}}""");
        projector.ApplyCall(call);
        projector.ApplyCall(call);
        Assert.Single(projector.Activity.Steps);
    }

    [Fact]
    public void AccessDenied_IsWarningNotSuccess()
    {
        var projector = Create();
        projector.ApplyCall(Event("tool_call", """{"tool_id":"a","name":"get_text","args":{}}"""));
        projector.ApplyResult(Event("tool_result", """{"tool_id":"a","status":"ok","result":"Access Denied","timing_ms":300}"""));
        Assert.Equal(ToolStepStatus.Warning, projector.Activity.Steps[0].Status);
        Assert.Equal("Page access denied", projector.Activity.Steps[0].Summary);
    }

    [Fact]
    public void LongUrl_ShowsDomainOnly()
    {
        var projector = Create();
        projector.ApplyCall(Event("tool_call", """{"tool_id":"a","name":"go_to","args":{"url":"https://www.weather.com/zh-CN/weather/today/1/CHXX0046:1:CH"}}"""));
        Assert.Equal("www.weather.com", projector.Activity.Steps[0].DisplayTarget);
    }

    [Fact]
    public void SearchAndFileArguments_HaveCompactTargets()
    {
        var projector = Create();
        projector.ApplyCall(Event("tool_call", """{"tool_id":"a","name":"search_web","args":{"query":"harbin weather forecast"}}"""));
        projector.ApplyCall(Event("tool_call", """{"tool_id":"b","name":"read_file","args":{"path":"C:\\secret\\report.txt"}}"""));
        Assert.Equal("Search: harbin weather forecast", projector.Activity.Steps[0].DisplayTarget);
        Assert.Equal("report.txt", projector.Activity.Steps[1].DisplayTarget);
    }

    [Fact]
    public void UnknownToolName_IsHumanized()
    {
        Assert.Equal("Do Useful Work", ToolActivityProjector.DisplayName("do_useful_work"));
    }

    [Fact]
    public void SensitiveArguments_AreRedacted()
    {
        var projector = Create();
        projector.ApplyCall(Event("tool_call", """{"tool_id":"a","name":"go_to","args":{"token":"secret-value","url":"https://example.test"}}"""));
        Assert.DoesNotContain("secret-value", projector.Activity.Steps[0].Arguments);
    }

    [Fact]
    public void Completion_AutoCollapsesSuccess()
    {
        var projector = Create();
        projector.ApplyCall(Event("tool_call", """{"tool_id":"a","name":"open_browser","args":{}}"""));
        projector.ApplyResult(Event("tool_result", """{"tool_id":"a","status":"ok","result":"opened"}"""));
        projector.Complete();
        Assert.Equal(ToolActivityStatus.Success, projector.Activity.Status);
        Assert.False(projector.Activity.IsExpanded);
    }

    [Fact]
    public void CompletedSuccess_ToggleExpandedRevealsTimeline()
    {
        var projector = Create();
        projector.ApplyCall(Event("tool_call", """{"tool_id":"a","name":"open_browser","args":{}}"""));
        projector.ApplyResult(Event("tool_result", """{"tool_id":"a","status":"ok","result":"opened"}"""));
        projector.Complete();
        Assert.False(projector.Activity.IsTimelineVisible);

        projector.Activity.ToggleExpanded();
        Assert.True(projector.Activity.IsExpanded);
        Assert.True(projector.Activity.IsTimelineVisible);
    }

    [Fact]
    public void Failure_DefaultsExpanded()
    {
        var projector = Create();
        projector.ApplyCall(Event("tool_call", """{"tool_id":"a","name":"open_browser","args":{}}"""));
        projector.Complete(ToolActivityStatus.Failed, "Connection refused");
        Assert.Equal(ToolActivityStatus.Failed, projector.Activity.Status);
        Assert.True(projector.Activity.IsExpanded);
    }

    [Fact]
    public void TransportFailure_IsFailedAndDefaultsExpanded()
    {
        var projector = Create();
        projector.ApplyCall(Event("tool_call", """{"tool_id":"a","name":"go_to","args":{}}"""));
        projector.ApplyResult(Event("tool_result", """{"tool_id":"a","status":"error","result":"Connection refused"}"""));
        projector.Complete();
        Assert.Equal(ToolStepStatus.Failed, projector.Activity.Steps[0].Status);
        Assert.Equal(ToolActivityStatus.PartialSuccess, projector.Activity.Status);
    }

    [Fact]
    public void PartialSuccess_CollapsesButKeepsWarningGlyph()
    {
        var projector = Create();
        projector.ApplyCall(Event("tool_call", """{"tool_id":"a","name":"get_text","args":{}}"""));
        projector.ApplyResult(Event("tool_result", """{"tool_id":"a","status":"ok","result":"No content"}"""));
        projector.Complete();
        Assert.Equal(ToolActivityStatus.PartialSuccess, projector.Activity.Status);
        Assert.False(projector.Activity.IsExpanded);
        Assert.Equal("⚠", projector.Activity.StatusGlyph);
    }

    [Fact]
    public void WaitingForConfirmation_StaysExpanded()
    {
        var projector = Create();
        projector.ApplyCall(Event("tool_call", """{"tool_id":"a","name":"write_file","args":{}}"""));
        projector.WaitForConfirmation();
        Assert.Equal(ToolActivityStatus.WaitingForConfirmation, projector.Activity.Status);
        Assert.True(projector.Activity.IsExpanded);
    }

    [Fact]
    public void HighRiskStep_RemainsVisibleInHiddenMode()
    {
        var projector = Create();
        projector.Activity.DisplayMode = ToolDisplayMode.Hidden;
        projector.ApplyCall(Event("tool_call", """{"tool_id":"a","name":"send_email","args":{}}"""));
        projector.ApplyResult(Event("tool_result", """{"tool_id":"a","status":"ok","result":"sent"}"""));
        projector.Complete();
        Assert.True(projector.Activity.Steps[0].IsHighRisk);
        Assert.True(projector.Activity.IsVisible);
    }

    [Fact]
    public void CancelledTurn_UsesCancelledStates()
    {
        var projector = Create();
        projector.ApplyCall(Event("tool_call", """{"tool_id":"a","name":"go_to","args":{}}"""));
        projector.Complete(ToolActivityStatus.Cancelled);
        Assert.Equal(ToolActivityStatus.Cancelled, projector.Activity.Status);
        Assert.Equal(ToolStepStatus.Cancelled, projector.Activity.Steps[0].Status);
    }

    [Fact]
    public void Activity_SerializesForSessionRestoration()
    {
        var projector = Create();
        projector.ApplyCall(Event("tool_call", """{"tool_id":"a","name":"open_browser","args":{}}"""));
        projector.ApplyResult(Event("tool_result", """{"tool_id":"a","status":"ok","result":"opened"}"""));
        projector.Complete();
        var restored = JsonSerializer.Deserialize<ToolActivityViewModel>(JsonSerializer.Serialize(projector.Activity));
        Assert.NotNull(restored);
        Assert.Single(restored!.Steps);
        Assert.Equal(ToolActivityStatus.Success, restored.Status);
    }

    [Theory]
    [InlineData(ToolDisplayMode.Hidden, false)]
    [InlineData(ToolDisplayMode.Compact, true)]
    [InlineData(ToolDisplayMode.Detailed, true)]
    public void DisplayModes_RespectCompletedSuccessVisibility(ToolDisplayMode mode, bool expectedVisible)
    {
        var projector = Create();
        projector.Activity.DisplayMode = mode;
        projector.ApplyCall(Event("tool_call", """{"tool_id":"a","name":"open_browser","args":{}}"""));
        projector.ApplyResult(Event("tool_result", """{"tool_id":"a","status":"ok","result":"opened"}"""));
        projector.Complete();
        Assert.Equal(expectedVisible, projector.Activity.IsVisible);
    }
}
