using ConnectOnion.WinUIClient.Models.Notifications;
using ConnectOnion.WinUIClient.Services.Notifications;

namespace ConnectOnion.WinUIClient.Tests.Notifications;

public class NotificationPolicyTests
{
    private const string ConvId = "conv-1";

    private static NotificationRequest Reply(string? conv = ConvId) => new(
        Title: "Agent",
        Body: "hello there",
        Type: NotificationType.AgentReply,
        ConversationId: conv);

    private static NotificationRequest Approval(string? conv = ConvId) => new(
        Title: "Approval needed",
        Body: "wants to run rm",
        Type: NotificationType.ApprovalRequired,
        ConversationId: conv);

    [Fact]
    public void MasterSwitchOff_Suppresses()
    {
        var policy = new NotificationPolicy();
        var settings = new NotificationSettings { EnableNotifications = false };

        var d = policy.Decide(Reply(), new FakeWindowPresence { IsForeground = true }, settings);

        Assert.Equal(NotificationChannel.None, d.Channel);
    }

    [Fact]
    public void TypeDisabled_Suppresses()
    {
        var policy = new NotificationPolicy();
        var settings = new NotificationSettings { NotifyAgentReplies = false };

        var d = policy.Decide(Reply(), new FakeWindowPresence { IsForeground = false }, settings);

        Assert.Equal(NotificationChannel.None, d.Channel);
    }

    [Fact]
    public void ViewingTargetConversation_Suppresses()
    {
        var policy = new NotificationPolicy();
        var presence = new FakeWindowPresence { IsForeground = true, ViewingConversationId = ConvId };

        var d = policy.Decide(Reply(), presence, new NotificationSettings());

        Assert.Equal(NotificationChannel.None, d.Channel);
        Assert.Equal("viewing target conversation", d.Reason);
    }

    [Fact]
    public void ForegroundOtherConversation_GoesInApp()
    {
        var policy = new NotificationPolicy();
        var presence = new FakeWindowPresence { IsForeground = true, ViewingConversationId = "other" };

        var d = policy.Decide(Reply(), presence, new NotificationSettings());

        Assert.Equal(NotificationChannel.InApp, d.Channel);
    }

    [Fact]
    public void BackgroundOrMinimized_GoesSystem()
    {
        var policy = new NotificationPolicy();
        var presence = new FakeWindowPresence { IsForeground = false };

        var d = policy.Decide(Reply(), presence, new NotificationSettings());

        Assert.Equal(NotificationChannel.System, d.Channel);
    }

    [Fact]
    public void ViewingApproval_Suppresses()
    {
        var policy = new NotificationPolicy();
        var presence = new FakeWindowPresence { IsForeground = true, ViewingConversationId = ConvId };

        var d = policy.Decide(Approval(), presence, new NotificationSettings());

        Assert.Equal(NotificationChannel.None, d.Channel);
        Assert.Equal("viewing approval", d.Reason);
    }

    [Fact]
    public void ApprovalNotViewing_StillNotifies()
    {
        var policy = new NotificationPolicy();
        var presence = new FakeWindowPresence { IsForeground = false };

        var d = policy.Decide(Approval(), presence, new NotificationSettings());

        Assert.Equal(NotificationChannel.System, d.Channel);
    }

    [Fact]
    public void PreviewOff_HidesBodyForContentTypes()
    {
        var policy = new NotificationPolicy();
        var settings = new NotificationSettings { ShowMessagePreview = false };
        var presence = new FakeWindowPresence { IsForeground = false };

        var d = policy.Decide(Reply(), presence, settings);

        Assert.Equal(NotificationChannel.System, d.Channel);
        Assert.Equal(NotificationText.GenericBody(NotificationType.AgentReply), d.Body);
        Assert.DoesNotContain("hello", d.Body!);
    }

    [Fact]
    public void PreviewOn_KeepsBody()
    {
        var policy = new NotificationPolicy();
        var presence = new FakeWindowPresence { IsForeground = false };

        var d = policy.Decide(Reply(), presence, new NotificationSettings());

        Assert.Equal("hello there", d.Body);
    }

    [Fact]
    public void PlaySoundSetting_FlowsToDecision()
    {
        var policy = new NotificationPolicy();
        var presence = new FakeWindowPresence { IsForeground = false };

        var on = policy.Decide(Reply(), presence, new NotificationSettings { PlayNotificationSound = true });
        var off = policy.Decide(Reply(), presence, new NotificationSettings { PlayNotificationSound = false });

        Assert.True(on.PlaySound);
        Assert.False(off.PlaySound);
    }
}
