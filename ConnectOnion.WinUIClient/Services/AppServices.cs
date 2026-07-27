using System;
using System.Net.Http;
using ConnectOnion.WinUIClient.Data;
using ConnectOnion.WinUIClient.Services.Notifications;
using ConnectOnion.WinUIClient.Services.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace ConnectOnion.WinUIClient.Services;

/// <summary>
/// Typed accessor over the application's DI container, which is built by the
/// Generic Host in <c>App</c>. Registrations live in
/// <see cref="ServiceRegistration"/>.
///
/// This accessor exists because WinUI instantiates windows, pages, and user
/// controls itself through their parameterless constructors rather than
/// through the dependency injection container.
///
/// Framework-created code-behind that needs a shared service resolves it here.
/// View models, which the container creates, should continue to receive their
/// dependencies through constructor injection.
///
/// Every member below returns the same registered singleton held by the
/// container.
/// </summary>
public static class AppServices
{
    private static IServiceProvider Provider => App.Services;

    // ---- SQLite repositories ----

    public static AgentRepository Agents
        => Provider.GetRequiredService<AgentRepository>();

    public static SessionRepository Sessions
        => Provider.GetRequiredService<SessionRepository>();

    public static SidebarStateRepository SidebarState
        => Provider.GetRequiredService<SidebarStateRepository>();

    public static PreferencesRepository Preferences
        => Provider.GetRequiredService<PreferencesRepository>();

    public static ConversationRepository Conversations
        => Provider.GetRequiredService<ConversationRepository>();

    public static WindowPlacementStore WindowPlacement
        => Provider.GetRequiredService<WindowPlacementStore>();

    /// <summary>
    /// Token-usage ledger.
    ///
    /// Deliberately independent of conversation and Agent lifetime:
    /// deleting a chat never erases the record that its tokens were spent.
    /// </summary>
    public static UsageRepository Usage
        => Provider.GetRequiredService<UsageRepository>();

    // ---- Agent services ----

    /// <summary>
    /// Session-lived cache of each Agent's online status.
    /// </summary>
    public static AgentPresenceService Presence
        => Provider.GetRequiredService<AgentPresenceService>();

    /// <summary>
    /// Selects, processes, temporarily stores, commits, and deletes
    /// user-selected Agent icons.
    ///
    /// Framework-created controls such as <c>AddAgentForm</c> may use this
    /// accessor when constructor injection is unavailable.
    /// </summary>
    public static IAgentIconService AgentIcons
        => Provider.GetRequiredService<IAgentIconService>();

    /// <summary>
    /// Process-memory OpenOnion authentication and account balance.
    /// </summary>
    public static OpenOnionAccountService OpenOnionAccount
        => Provider.GetRequiredService<OpenOnionAccountService>();

    // ---- Keyboard ----

    /// <summary>
    /// The live shortcut bindings: catalog defaults overlaid with the user's
    /// custom bindings.
    ///
    /// Window key handlers query this service rather than testing hard-coded
    /// key values.
    /// </summary>
    public static KeyboardShortcutService Shortcuts
        => Provider.GetRequiredService<KeyboardShortcutService>();

    // ---- Network and links ----

    /// <summary>
    /// Single shared HttpClient for health probes, endpoint resolution,
    /// Agent-info requests, and image downloads.
    /// </summary>
    public static HttpClient Http
        => Provider.GetRequiredService<HttpClient>();

    // Opens external links. Tests can substitute a fake by assigning the
    // override. The underlying Launcher is a static WinRT type and cannot be
    // mocked directly. When no override is set, the registered IUriLauncher
    // singleton is returned.
    private static IUriLauncher? _uriLauncherOverride;

    public static IUriLauncher UriLauncher
    {
        get => _uriLauncherOverride
            ?? Provider.GetRequiredService<IUriLauncher>();

        set => _uriLauncherOverride = value;
    }

    // ---- Runtime ----

    /// <summary>
    /// App-level owner of Agent connections and in-flight turns.
    ///
    /// Pages and view models submit sends here and subscribe to run updates
    /// rather than owning a connection or background task. This allows a reply
    /// to keep streaming and persisting across page switches, tray minimization,
    /// and conversation reopening.
    /// </summary>
    public static AgentSessionManager RunManager
        => Provider.GetRequiredService<AgentSessionManager>();

    // ---- Notifications ----

    /// <summary>
    /// Per-window focus, visibility, and currently displayed conversation.
    /// </summary>
    public static WindowPresenceService WindowPresence
        => Provider.GetRequiredService<WindowPresenceService>();

    /// <summary>
    /// Persisted notification preferences and notification-policy settings.
    /// </summary>
    public static NotificationSettingsStore NotificationSettings
        => Provider.GetRequiredService<NotificationSettingsStore>();

    /// <summary>
    /// The central service that decides whether and how to notify.
    /// </summary>
    public static NotificationCoordinator Notifications
        => Provider.GetRequiredService<NotificationCoordinator>();

    /// <summary>
    /// Routes a selected notification to the appropriate window and
    /// conversation.
    /// </summary>
    public static ConversationNavigationService Navigation
        => Provider.GetRequiredService<ConversationNavigationService>();

    /// <summary>
    /// Registers for and dispatches Windows App Notification activations.
    /// </summary>
    public static NotificationActivationRouter NotificationActivation
        => Provider.GetRequiredService<NotificationActivationRouter>();
}
