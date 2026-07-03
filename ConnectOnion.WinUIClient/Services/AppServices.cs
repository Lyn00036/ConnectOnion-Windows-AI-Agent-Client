using System.Net.Http;

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

    /// <summary>Single shared HttpClient for health probes and endpoint resolution.</summary>
    public static HttpClient Http { get; } = new();
}
