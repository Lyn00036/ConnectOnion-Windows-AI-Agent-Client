using System;
using System.Diagnostics;

namespace ConnectOnion.WinUIClient.Services.Notifications;

/// <summary>Minimal, never-throwing log sink for the notification subsystem.
/// Notification failures are diagnostics only — they must not disturb chat.</summary>
public static class NotificationLog
{
    public static void Warn(string message, Exception? ex = null)
        => Debug.WriteLine($"[Notify] {message}{(ex is null ? "" : $": {ex.Message}")}");
}
