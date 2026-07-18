using ConnectOnion.WinUIClient.Models.Notifications;

namespace ConnectOnion.WinUIClient.Services.Notifications;

/// <summary>
/// Pure decision logic: given a request, the current window state, and settings,
/// decide whether to show nothing, an in-app toast, or a system toast. No UI, no
/// side effects — this is the single place notification-visibility rules live and
/// the main unit-test surface.
/// </summary>
public sealed class NotificationPolicy
{
    public NotificationDecision Decide(
        NotificationRequest request,
        IWindowPresence presence,
        NotificationSettings settings)
    {
        if (!settings.EnableNotifications)
            return NotificationDecision.Suppress("notifications disabled");

        if (!IsTypeEnabled(request.Type, settings))
            return NotificationDecision.Suppress("type disabled");

        // The user is already looking at the exact conversation (approval bubbles
        // render inline there too) — no need to interrupt them.
        if (request.Type == NotificationType.ApprovalRequired)
        {
            if (presence.IsViewingApproval(request.ConversationId))
                return NotificationDecision.Suppress("viewing approval");
        }
        else if (presence.IsViewingConversation(request.ConversationId))
        {
            return NotificationDecision.Suppress("viewing target conversation");
        }

        var body = EffectiveBody(request, settings);
        var play = settings.PlayNotificationSound;

        // App has a focused, visible window but the user is elsewhere → in-app toast.
        if (presence.IsForeground)
            return new NotificationDecision(NotificationChannel.InApp, "foreground, other view", play, body);

        // No visible/active window (minimized or hidden to tray) → OS toast.
        return new NotificationDecision(NotificationChannel.System, "background", play, body);
    }

    private static bool IsTypeEnabled(NotificationType type, NotificationSettings s) => type switch
    {
        NotificationType.AgentReply => s.NotifyAgentReplies,
        NotificationType.TaskCompleted => s.NotifyTaskCompletion,
        NotificationType.ApprovalRequired => s.NotifyApprovalRequests,
        NotificationType.ConnectionLost => s.NotifyConnectionProblems,
        // Errors ride on the connection-problems switch rather than getting their own: in
        // practice nearly every error the app raises is a transport failure, and a second
        // near-identical toggle would only make the settings page harder to read.
        NotificationType.Error => s.NotifyConnectionProblems,
        // Unknown/future types default to *shown*. A notification nobody asked to silence is
        // recoverable; one silently dropped because an enum member was added is not.
        _ => true,
    };

    // Message content is hidden when preview is off; non-content types (connection)
    // keep their already-generic body.
    private static string EffectiveBody(NotificationRequest request, NotificationSettings s)
    {
        if (s.ShowMessagePreview) return request.Body;

        return request.Type switch
        {
            NotificationType.AgentReply
                or NotificationType.TaskCompleted
                or NotificationType.ApprovalRequired
                => NotificationText.GenericBody(request.Type),
            _ => request.Body,
        };
    }
}
