using System.Linq;
using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Models.Notifications;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.Services.Notifications;
using ConnectOnion.WinUIClient.Views;
using Microsoft.UI.Xaml;

namespace ConnectOnion.WinUIClient;

/// <summary>
/// Notification integration for the shell window: registers the window with
/// <see cref="WindowPresenceService"/>, reports focus/visibility, and implements
/// <see cref="IChatWindow"/> so a clicked notification (or in-app toast) can
/// restore this window and open the target conversation.
/// </summary>
public sealed partial class MainWindow : IChatWindow
{
    /// <summary>Called from the constructor: register for per-window presence tracking.</summary>
    private void InitializeNotificationPresence()
    {
        AppServices.WindowPresence.Register(this);
        Activated += OnActivatedForPresence;
        Closed += (_, _) =>
        {
            Activated -= OnActivatedForPresence;
            AppServices.WindowPresence.Unregister(this);
        };
    }

    private void OnActivatedForPresence(object sender, WindowActivatedEventArgs e)
        => AppServices.WindowPresence.SetActive(
            this, e.WindowActivationState != WindowActivationState.Deactivated);

    // ---- IChatWindow ----------------------------------------------------

    public void RestoreAndActivate() => RestoreFromTray();

    /// <summary>Selects the agent + conversation and navigates this window's frame to
    /// the chat. Fails safe (navigates Home) when the conversation was deleted.</summary>
    public async Task ShowConversationAsync(string agentId, string conversationId)
    {
        var sessions = await AppServices.Sessions.LoadAsync();
        var session = sessions.Sessions.FirstOrDefault(s => s.Id == conversationId && s.AgentId == agentId);
        if (session is null)
        {
            NavigateTo(typeof(HomePage), forceReload: true);
            return;
        }

        var agents = await AppServices.Agents.LoadAsync();
        agents.SelectedAgentId = agentId;
        await AppServices.Agents.SaveAsync(agents);

        sessions.ActiveSessionId = conversationId;
        await AppServices.Sessions.SaveAsync(sessions);

        NavigateTo(typeof(ChatPage), forceReload: true);
        try { await ShellSidebar.RefreshAsync(); }
        catch { /* sidebar refresh is best-effort */ }
    }

    public void ShowInAppToast(InAppToastModel toast) => InAppNotifications.ShowToast(toast);

    /// <summary>Finds the shell window hosting a given XamlRoot (so a page can report
    /// which conversation it is showing). Returns null if not found. Single-window app:
    /// the only candidate is <see cref="App.MainWindow"/>.</summary>
    internal static MainWindow? FromXamlRoot(XamlRoot? root)
    {
        if (root is null) return null;
        return App.MainWindow is MainWindow mainWindow
            && ReferenceEquals(mainWindow.Content?.XamlRoot, root)
            ? mainWindow
            : null;
    }
}
