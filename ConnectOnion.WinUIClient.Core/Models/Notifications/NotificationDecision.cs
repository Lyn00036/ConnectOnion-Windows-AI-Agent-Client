namespace ConnectOnion.WinUIClient.Models.Notifications;

/// <summary>Where a notification should be shown (if anywhere).</summary>
public enum NotificationChannel
{
    /// <summary>Suppressed — e.g. the user is already looking at the target.</summary>
    None,
    /// <summary>A lightweight in-app toast in the foreground window.</summary>
    InApp,
    /// <summary>A Windows App Notification (toast) via the OS.</summary>
    System,
}

/// <summary>The <c>NotificationPolicy</c>'s verdict for one request.</summary>
public sealed record NotificationDecision(
    NotificationChannel Channel,
    string Reason,
    bool PlaySound = false,
    string? Body = null)
{
    public static NotificationDecision Suppress(string reason)
        => new(NotificationChannel.None, reason);
}
