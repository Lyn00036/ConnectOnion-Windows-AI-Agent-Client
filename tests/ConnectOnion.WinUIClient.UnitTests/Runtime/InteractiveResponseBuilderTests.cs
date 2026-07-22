using System.Text.Json;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services.Runtime;

namespace ConnectOnion.WinUIClient.UnitTests.Runtime;

public sealed class InteractiveResponseBuilderTests
{
    [Fact]
    public void AskUser_SingleSelect_SubmitsSelectedValue()
    {
        var message = Ask(options: ["Agents", "Logs"]);
        message.AskUserOptionEntries[1].Toggle();
        Assert.Equal("Logs", InteractiveResponseBuilder.BuildAskUserAnswer(message));
    }

    [Fact]
    public void AskUser_MultiSelect_SubmitsAllSelectedValues()
    {
        var message = Ask(true, "Agents", "Logs", "Status");
        message.AskUserOptionEntries[0].Toggle();
        message.AskUserOptionEntries[2].Toggle();
        Assert.Equal(["Agents", "Status"], Assert.IsType<string[]>(
            InteractiveResponseBuilder.BuildAskUserAnswer(message)));
    }

    [Fact]
    public void AskUser_WithOptions_HidesUnspecifiedFreeTextAndOnlyMultiShowsCount()
    {
        var single = Ask(options: ["Yes", "No"]);
        var multi = Ask(true, "A", "B");

        Assert.False(single.ShowAskUserFreeText);
        Assert.False(single.ShowAskUserSelectionSummary);
        Assert.True(multi.ShowAskUserSelectionSummary);
    }

    [Fact]
    public void AskUser_FreeText_TrimsAndSubmitsText()
    {
        var message = Ask();
        message.AskUserFreeText = "  active in the last hour  ";
        Assert.Equal("active in the last hour", InteractiveResponseBuilder.BuildAskUserAnswer(message));
    }

    [Fact]
    public void AskUser_Form_UsesDocumentedJsonObjectAnswer()
    {
        var message = Ask();
        message.AskUserFields.Add(Field(message, "host", "Host", true, " server-1 "));
        message.AskUserFields.Add(Field(message, "note", "Note", false, " blue "));

        using var json = JsonDocument.Parse(Assert.IsType<string>(
            InteractiveResponseBuilder.BuildAskUserAnswer(message)));
        Assert.Equal("server-1", json.RootElement.GetProperty("host").GetString());
        Assert.Equal("blue", json.RootElement.GetProperty("note").GetString());
    }

    [Fact]
    public void AskUser_RequiredField_BlocksSubmissionAndShowsFieldError()
    {
        var message = Ask();
        message.AskUserFields.Add(Field(message, "host", "Host", true, ""));
        Assert.False(message.ValidateAskUser());
        Assert.False(message.CanSubmitAskUser);
        Assert.Equal("Host is required.", message.AskUserFields[0].ValidationError);
    }

    [Fact]
    public void InteractiveSubmit_LocksDuplicateAndBecomesReadOnlyAfterSuccess()
    {
        var message = Ask(options: ["Continue"]);
        message.AskUserOptionEntries[0].Toggle();
        Assert.True(message.TryBeginInteractiveSubmit());
        Assert.False(message.TryBeginInteractiveSubmit());
        message.CompleteInteractiveSubmit("Continue");
        Assert.False(message.IsInteractiveEditable);
        Assert.True(message.ShowInteractiveResult);
        Assert.Equal("Submitted", message.InteractiveStateLabel);
    }

    [Fact]
    public void ConnectionLost_PreservesDraftAndDisablesSubmitUntilRevalidated()
    {
        var message = Ask();
        message.AskUserFreeText = "do not lose this";
        message.MarkInteractiveConnectionLost();
        Assert.Equal("do not lose this", message.AskUserFreeText);
        Assert.False(message.CanSubmitAskUser);
        Assert.Equal("Connection lost", message.InteractiveStateLabel);

        message.RevalidateInteractiveRequest();
        Assert.True(message.CanSubmitAskUser);
        Assert.Equal("do not lose this", message.AskUserFreeText);
    }

    [Fact]
    public void LoadedSkippedQuestion_DoesNotClaimAResponseWasSubmitted()
    {
        var message = new ChatMessage
        {
            EventKind = "ask_user",
            Status = EventStatus.Done,
            EventMeta = "Skipped",
        };

        Assert.Equal("Skipped", message.InteractiveStateLabel);
        Assert.Equal("Question skipped", message.AskUserResultTitle);
        Assert.Equal("No response was submitted.", message.InteractiveStateDescription);
        Assert.False(message.HasInteractiveAnswer);
    }

    [Theory]
    [InlineData(PlanReviewAction.Approve, "", "", false)]
    [InlineData(PlanReviewAction.RequestChanges, "Use port 8080", "Use port 8080", false)]
    [InlineData(PlanReviewAction.Reject, "Too risky", "rejected: Too risky", true)]
    public void PlanReview_UsesExistingMessageOnlySchema(
        PlanReviewAction action, string feedback, string expectedMessage, bool rejected)
    {
        var response = InteractiveResponseBuilder.BuildPlanReviewResponse(action, feedback);
        Assert.NotNull(response);
        Assert.Equal(expectedMessage, response.Message);
        Assert.Equal(rejected, response.Rejected);
    }

    [Fact]
    public void PlanReview_RequestChangesRequiresFeedback()
        => Assert.Null(InteractiveResponseBuilder.BuildPlanReviewResponse(PlanReviewAction.RequestChanges, "  "));

    private static ChatMessage Ask(bool multi = false, params string[] options)
    {
        var message = new ChatMessage
        {
            EventKind = "ask_user",
            Status = EventStatus.Running,
            AskUserMultiSelect = multi,
        };
        foreach (var option in options)
            message.AskUserOptionEntries.Add(new AskUserOptionEntry { Text = option, Owner = message });
        return message;
    }

    private static AskUserFieldEntry Field(
        ChatMessage owner, string name, string label, bool required, string value)
        => new() { Owner = owner, Name = name, Label = label, Required = required, Value = value };
}
