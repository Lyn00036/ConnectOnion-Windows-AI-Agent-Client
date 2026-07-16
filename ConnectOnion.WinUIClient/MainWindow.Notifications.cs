using System.Linq;
using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Data;
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
    private WindowPresenceService? _windowPresence;
    private int _notificationPresenceDetached;

    /// <summary>Called from the constructor: register for per-window presence tracking.</summary>
    private void InitializeNotificationPresence()
    {
        _windowPresence = AppServices.WindowPresence;
        _windowPresence.Register(this);
        Activated += OnActivatedForPresence;
        IdentityStore.IdentityReset += OnIdentityReset;
    }

    private void DetachNotificationPresence()
    {
        if (System.Threading.Interlocked.Exchange(ref _notificationPresenceDetached, 1) != 0) return;

        Activated -= OnActivatedForPresence;
        IdentityStore.IdentityReset -= OnIdentityReset;
        _windowPresence?.Unregister(this);
        _windowPresence = null;
    }

    /// <summary>
    /// Losing the stored identity is silent by nature — the app just carries on under a new
    /// address, and every authorization the old one had is gone. Say so. The identity is often
    /// first needed before this window exists, so both the latched flag (checked once the shell
    /// loads) and the live event route here.
    /// </summary>
    private void ReportIdentityResetIfAny()
    {
        if (!IdentityStore.WasReset || IdentityStore.ResetReason is not { } reason) return;
        OnIdentityReset(reason);
    }

    private void OnIdentityReset(string reason)
        => DispatcherQueue.TryEnqueue(() => ShowInAppToast(new InAppToastModel(
            "New identity generated",
            $"Your previous ConnectOnion identity could not be recovered — {reason}. "
                + "Agents that authorized the old address will need to authorize this one again.",
            NotificationType.Error,
            AgentId: null,
            ConversationId: null,
            ActionId: null)));

    private void OnActivatedForPresence(object sender, WindowActivatedEventArgs e)
        => _windowPresence?.SetActive(
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
            NotificationLog.Info($"ShowConversationAsync: no session matched agentId={agentId} conversationId={conversationId} — navigating Home");
            NavigateTo(typeof(HomePage), forceReload: true);
            return;
        }

        NotificationLog.Info($"ShowConversationAsync: matched session — opening chat for agentId={agentId} conversationId={conversationId}");

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
