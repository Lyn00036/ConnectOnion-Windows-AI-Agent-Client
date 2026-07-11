using System.Net.Http;
using ConnectOnion.WinUIClient.Data;
using ConnectOnion.WinUIClient.Services.Notifications;

namespace ConnectOnion.WinUIClient.Services;

/// <summary>
/// Process-wide singletons. WinUI has no built-in DI container in the blank
/// template, so this simple locator hands the repositories and shared HttpClient
/// to pages and view models. Initialized once at app startup.
/// </summary>
public static class AppServices
{
    public static AgentRepository Agents { get; } = new();
    public static SessionRepository Sessions { get; } = new();
    public static PreferencesRepository Preferences { get; } = new();
    public static ConversationRepository Conversations { get; } = new();

    /// <summary>Session-lived cache of each agent's online status.</summary>
    public static AgentPresenceService Presence { get; } = new();

    /// <summary>Single shared HttpClient for health probes and endpoint resolution.</summary>
    public static HttpClient Http { get; } = new();

    // ---- Notification subsystem (composition root) ----
    // Declaration order matters: the coordinator's dependencies must exist first.

    /// <summary>Per-window focus/visibility + which conversation each window shows.</summary>
    public static WindowPresenceService WindowPresence { get; } = new();

    /// <summary>Persisted notification preferences (also the policy's settings source).</summary>
    public static NotificationSettingsStore NotificationSettings { get; } = new();

    private static readonly InAppNotificationService InAppChannel = new(WindowPresence);
    private static readonly WindowsNotificationService SystemChannel = new();

    /// <summary>The one place that decides whether/how to notify.</summary>
    public static NotificationCoordinator Notifications { get; } = new(
        SystemChannel, InAppChannel, WindowPresence, NotificationSettings,
        new ThreadingScheduler(), onError: NotificationLog.Warn);

    /// <summary>Routes a clicked notification to the right window/conversation.</summary>
    public static ConversationNavigationService Navigation { get; } = new(WindowPresence);

    /// <summary>Registers for and dispatches App Notification activations.</summary>
    public static NotificationActivationRouter NotificationActivation { get; } = new(Navigation);

    private static bool _notificationsInitialized;

    /// <summary>One-time startup wiring for notifications: load settings, register for
    /// OS activation, and cancel pending connection-lost notices when an agent recovers.</summary>
    public static void InitializeNotifications()
    {
        if (_notificationsInitialized) return;
        _notificationsInitialized = true;

        _ = NotificationSettings.LoadAsync();
        Presence.PresenceChanged += OnPresenceChangedForNotifications;
        NotificationActivation.RegisterAndListen();
    }

    private static void OnPresenceChangedForNotifications(string agentId)
    {
        // Recovery cancels a pending "connection lost" (grace period) or re-arms after one.
        if (Presence.GetPresence(agentId) == AgentPresence.Online)
            Notifications.NotifyConnectionRestored(agentId);
    }
}
