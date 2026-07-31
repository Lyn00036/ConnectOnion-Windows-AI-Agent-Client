using ConnectOnion.Protocol;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services.Runtime;

namespace ConnectOnion.WinUIClient.UnitTests.Runtime;

public sealed class ToolActivityProjectorTests
{
    [Fact]
    public void ApplyCall_ThenSuccessfulResult_TransitionsRunningToSuccess()
    {
        var activity = new ToolActivityViewModel();
        var projector = new ToolActivityProjector(activity);
        var step = projector.ApplyCall(Event("tool_call", "{\"tool_id\":\"1\",\"name\":\"read_file\",\"args\":{}}"));

        var result = projector.ApplyResult(Event("tool_result", "{\"tool_id\":\"1\",\"status\":\"success\",\"result\":\"contents\"}"));
        projector.Complete();

        Assert.Same(step, result);
        Assert.Equal(ToolStepStatus.Success, step.Status);
        Assert.Equal(ToolActivityStatus.Success, activity.Status);
    }

    [Fact]
    public void ApplyResult_FailureText_MarksStepFailedAndActivityPartialSuccess()
    {
        var activity = new ToolActivityViewModel();
        var projector = new ToolActivityProjector(activity);
        projector.ApplyCall(Event("tool_call", "{\"tool_id\":\"1\",\"name\":\"open_browser\",\"args\":{}}"));

        var step = projector.ApplyResult(Event("tool_result", "{\"tool_id\":\"1\",\"status\":\"error\",\"error\":\"connection refused\"}"));
        projector.Complete();

        Assert.Equal(ToolStepStatus.Failed, step!.Status);
        Assert.Equal("Connection refused", step.Summary);
        Assert.Equal(ToolActivityStatus.PartialSuccess, activity.Status);
    }

    [Fact]
    public void Complete_Cancelled_CancelsRunningSteps()
    {
        var activity = new ToolActivityViewModel();
        var projector = new ToolActivityProjector(activity);
        var step = projector.ApplyCall(Event("tool_call", "{\"tool_id\":\"1\",\"name\":\"search_web\",\"args\":{}}"));

        projector.Complete(ToolActivityStatus.Cancelled);

        Assert.Equal(ToolStepStatus.Cancelled, step.Status);
        Assert.Equal(ToolActivityStatus.Cancelled, activity.Status);
    }

    [Fact]
    public void CompleteOptimistically_FreezesRunningStepsAsSuccessful()
    {
        var activity = new ToolActivityViewModel();
        var projector = new ToolActivityProjector(activity);
        var step = projector.ApplyCall(
            Event("tool_call", "{\"tool_id\":\"1\",\"name\":\"search_web\",\"args\":{}}"));

        projector.CompleteOptimistically();

        Assert.Equal(ToolStepStatus.Success, step.Status);
        Assert.Equal(ToolActivityStatus.Success, activity.Status);
        Assert.True(activity.IsTerminal);
        Assert.Contains("Completed", activity.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyCall_DuplicateCorrelationId_ReturnsExistingStep()
    {
        var activity = new ToolActivityViewModel();
        var projector = new ToolActivityProjector(activity);
        var frame = Event("tool_call", "{\"tool_id\":\"same\",\"name\":\"read_file\",\"args\":{}} ");

        var first = projector.ApplyCall(frame);
        var duplicate = projector.ApplyCall(frame);

        Assert.Same(first, duplicate);
        Assert.Single(activity.Steps);
    }

    [Fact]
    public void ApplyResult_MissingCorrelationId_DoesNotGuessByToolName()
    {
        var activity = new ToolActivityViewModel();
        var projector = new ToolActivityProjector(activity);
        projector.ApplyCall(Event("tool_call", "{\"tool_id\":\"1\",\"name\":\"read_file\",\"args\":{}}"));

        var result = projector.ApplyResult(Event("tool_result", "{\"name\":\"read_file\",\"result\":\"contents\"}"));

        Assert.Null(result);
        Assert.Equal(ToolStepStatus.Running, activity.Steps[0].Status);
    }

    [Fact]
    public void ApplyCall_SensitiveArguments_RedactsSecretsRecursively()
    {
        var activity = new ToolActivityViewModel();
        var projector = new ToolActivityProjector(activity);

        var step = projector.ApplyCall(Event("tool_call", "{\"tool_id\":\"1\",\"name\":\"send_email\",\"args\":{\"token\":\"secret-value\",\"nested\":{\"password\":\"password-value\"}}}"));

        Assert.DoesNotContain("secret-value", step.Arguments);
        Assert.DoesNotContain("password-value", step.Arguments);
        Assert.Contains("[hidden]", step.Arguments);
        Assert.True(step.IsHighRisk);
    }

    [Fact]
    public void ApplyResult_HugeResult_IsCappedBeforeItEntersTheMessageList()
    {
        var activity = new ToolActivityViewModel();
        var projector = new ToolActivityProjector(activity);
        projector.ApplyCall(Event("tool_call", "{\"tool_id\":\"1\",\"name\":\"get_text\",\"args\":{}}"));

        // A page scrape or a file read is routinely this big, and the step is retained for the
        // whole conversation (message list, cache, and the persisted event_args row).
        var huge = new string('x', 400_000);
        var step = projector.ApplyResult(
            Event("tool_result", $"{{\"tool_id\":\"1\",\"status\":\"success\",\"result\":\"{huge}\"}}"));

        Assert.NotNull(step!.Result);
        Assert.True(step.Result!.Length < 9_000, $"result retained {step.Result.Length} chars");
        Assert.EndsWith("… (truncated)", step.Result);
        Assert.Equal(ToolStepStatus.Success, step.Status);
    }

    [Theory]
    // The real report: a successful Bing navigation whose redirect tracking id happens to
    // contain "403". Every word a user can read says success, so an amber card is unexplainable.
    [InlineData("Navigated to https://www.bing.com/search?q=UNSW+ranking&rdr=1&rdrig=750A8A63BA3A403A8733EAD7629913CE")]
    // Digits that are part of a longer number are not status codes either.
    [InlineData("Downloaded 4040 rows")]
    [InlineData("Navigated to https://example.com/item/14032")]
    public void ApplyResult_DigitsEmbeddedInUrlsOrNumbers_StaysSuccess(string result)
    {
        var activity = new ToolActivityViewModel();
        var projector = new ToolActivityProjector(activity);
        projector.ApplyCall(Event("tool_call", "{\"tool_id\":\"1\",\"name\":\"navigate\",\"args\":{}}"));

        var step = projector.ApplyResult(
            Event("tool_result", $"{{\"tool_id\":\"1\",\"status\":\"success\",\"result\":\"{result}\"}}"));
        projector.Complete();

        Assert.Equal(ToolStepStatus.Success, step!.Status);
        Assert.Equal(ToolActivityStatus.Success, activity.Status);
    }

    [Theory]
    // A real status code still has to register — the boundary fix must not disarm the check.
    // Note none of these say "failed"/"error": those words would make it a Failure instead,
    // which is the documented precedence, not a 404 being ignored.
    [InlineData("Server returned 404 Not Found")]
    [InlineData("GET /missing -> 404")]
    [InlineData("Blocked with status 403")]
    public void ApplyResult_GenuineHttpStatusCode_IsStillAWarning(string result)
    {
        var activity = new ToolActivityViewModel();
        var projector = new ToolActivityProjector(activity);
        projector.ApplyCall(Event("tool_call", "{\"tool_id\":\"1\",\"name\":\"navigate\",\"args\":{}}"));

        var step = projector.ApplyResult(
            Event("tool_result", $"{{\"tool_id\":\"1\",\"status\":\"success\",\"result\":\"{result}\"}}"));

        Assert.Equal(ToolStepStatus.Warning, step!.Status);
    }

    [Fact]
    public void ApplyResult_403WithoutFailureWording_SummarisesAsAccessDenied()
    {
        var activity = new ToolActivityViewModel();
        var projector = new ToolActivityProjector(activity);
        projector.ApplyCall(Event("tool_call", "{\"tool_id\":\"1\",\"name\":\"navigate\",\"args\":{}}"));

        var step = projector.ApplyResult(
            Event("tool_result", "{\"tool_id\":\"1\",\"status\":\"success\",\"result\":\"Blocked with status 403\"}"));

        Assert.Equal("Page access denied", step!.Summary);
    }

    private static AgentStreamEvent Event(string type, string json) => new(type, type, null, json);
}
