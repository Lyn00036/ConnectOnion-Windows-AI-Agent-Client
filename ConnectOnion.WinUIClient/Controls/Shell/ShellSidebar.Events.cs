using System;
using System.Linq;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace ConnectOnion.WinUIClient.Controls;

// User-interaction entry points for the sidebar: row hover/focus handlers and the
// routed Click handlers for add-chat / add-agent / pin / delete / settings. Each
// delegates to the state/refresh logic in ShellSidebar.xaml.cs (RefreshAsync,
// SelectSessionAsync, ConfirmDeleteAsync, RequestNavigation);
// split out so the main file holds the data/presence model, not the wiring.
public sealed partial class ShellSidebar
{
    // Agent and session rows share one interaction model (see RowStateItem):
    // pointer hover and keyboard focus both reveal the row's action buttons and
    // paint its hover background. Both row kinds bind to these same handlers.
    private void Row_PointerEntered(object sender, PointerRoutedEventArgs e) => SetRowInteractive(sender, true);
    private void Row_PointerExited(object sender, PointerRoutedEventArgs e) => SetRowInteractive(sender, false);
    private void Row_GotFocus(object sender, RoutedEventArgs e) => SetRowInteractive(sender, true);
    private void Row_LostFocus(object sender, RoutedEventArgs e) => SetRowInteractive(sender, false);

    private static void SetRowInteractive(object sender, bool active)
    {
        if ((sender as FrameworkElement)?.DataContext is RowStateItem row)
        {
            row.SetInteractive(active);
        }
    }

    private void NewChatButton_Click(object sender, RoutedEventArgs e)
        => NewChatRequested?.Invoke();

    /// <summary>
    /// Opens the agent's start-a-chat surface instead of creating a conversation outright.
    ///
    /// The session is created by <see cref="Views.AgentDetailPage"/> when the user actually sends
    /// their first message, so a click here that the user then thinks better of leaves nothing
    /// behind. Creating the row up front put an empty conversation in the sidebar for every
    /// stray click, and those had to be deleted by hand.
    /// </summary>
    private async void AddChat_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string agentId) return;

        var agentsState = await AppServices.Agents.LoadAsync();
        agentsState.SelectedAgentId = agentId;
        await AppServices.Agents.SaveAsync(agentsState);
        FocusAgentInSidebar(agentId);
        await RefreshAsync();
        RequestNavigation(typeof(Views.AgentDetailPage), forceReload: true);
    }

    private async void DeleteAgent_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string agentId) return;
        await DeleteAgentAsync(agentId);
    }

    internal async System.Threading.Tasks.Task<bool> DeleteAgentAsync(string agentId)
    {
        try
        {
            var agentsState = await AppServices.Agents.LoadAsync();
        var agent = agentsState.Agents.FirstOrDefault(entry => entry.Id == agentId);
        if (agent is null) return false;

        if (!await ConfirmDeleteAsync($"Delete {agent.DisplayName}?", "This agent and all of its local sessions will be removed.")) return false;

        var sessionsState = await AppServices.Sessions.LoadAsync();
        // Tear each of this agent's conversations down completely before its sessions rows go,
        // and remove the agent only after that transaction succeeds. Deleting the agent first
        // used to leave invisible orphan sessions whenever a later child cleanup failed.
        // Releasing the socket/run leaves no live WebSocket behind; deleting the child rows
        // (messages, attachments, executions, traces) is what actually makes the removal work:
        // the session FKs declare no ON DELETE cascade, so SessionRepository.SaveAsync's
        // "DELETE FROM sessions" raises a constraint violation for any conversation that still
        // has message/execution rows. That throw lands before RefreshAsync below and is swallowed
        // by the catch, leaving the agent gone from storage but the sidebar never refreshed — the
        // "delete doesn't update the sidebar" bug. This mirrors DeleteSession_Click's cleanup.
        foreach (var removed in sessionsState.Sessions.Where(session => session.AgentId == agentId).ToList())
        {
            ConversationCache.Invalidate(removed.Id);
            await AppServices.RunManager.ReleaseConversationAsync(removed.Id);
            await AppServices.Conversations.DeleteMessagesAsync(removed.Id);
            await AppServices.Conversations.DeleteExecutionsAndTracesAsync(removed.Id);
        }
        sessionsState.Sessions = sessionsState.Sessions
            .Where(session => session.AgentId != agentId)
            .ToList();
        if (sessionsState.ActiveSessionId is not null &&
            sessionsState.Sessions.All(session => session.Id != sessionsState.ActiveSessionId))
        {
            sessionsState.ActiveSessionId = sessionsState.Sessions.FirstOrDefault()?.Id;
        }
        await AppServices.Sessions.SaveAsync(sessionsState);

        agentsState.Agents.Remove(agent);
        if (agentsState.SelectedAgentId == agentId)
        {
            agentsState.SelectedAgentId = agentsState.Agents.FirstOrDefault()?.Id;
        }
        await AppServices.Agents.SaveAsync(agentsState);

        AppServices.RunManager.ForgetAgent(agentId);
        AppServices.Presence.Forget(agentId);
        AppServices.Notifications.NotifyConnectionRestored(agentId);

        await RefreshAsync();
        RequestNavigation(typeof(ChatPage), forceReload: true);
        return true;
        }
        catch { return false; }
    }

    /// <summary>A conversation row inside an agent's branch. Focuses that agent, since the user
    /// clicked within its subtree and expects the tree to follow.</summary>
    private async void Session_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string sessionId)
        {
            await SelectSessionAsync(sessionId);
        }
    }

    /// <summary>A conversation row in the pinned section. Same navigation, but the tree below is
    /// left as the user arranged it — see <see cref="SelectSessionAsync"/>. Separate handler
    /// rather than a flag on the row, because the two call sites are already distinct elements
    /// in the XAML and this keeps the difference visible where the click is wired.</summary>
    private async void PinnedSession_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string sessionId)
        {
            await SelectSessionAsync(sessionId, preserveSidebarState: true);
        }
    }

    private async void TogglePinSession_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string sessionId) return;

        var sessionsState = await AppServices.Sessions.LoadAsync();
        var session = sessionsState.Sessions.FirstOrDefault(entry => entry.Id == sessionId);
        if (session is null) return;

        session.IsPinned = !session.IsPinned;
        await AppServices.Sessions.SaveAsync(sessionsState);
        await RefreshAsync();
    }

    private async void DeleteSession_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string sessionId) return;

        try
        {
            var sessionsState = await AppServices.Sessions.LoadAsync();
        var session = sessionsState.Sessions.FirstOrDefault(entry => entry.Id == sessionId);
        if (session is null) return;

        if (!await ConfirmDeleteAsync($"Delete {session.Title}?", "This local conversation will be removed.")) return;

        sessionsState.Sessions.Remove(session);
        ConversationCache.Invalidate(sessionId);
        await AppServices.RunManager.ReleaseConversationAsync(sessionId);
        await AppServices.Conversations.DeleteMessagesAsync(sessionId);
        await AppServices.Conversations.DeleteExecutionsAndTracesAsync(sessionId);

        // No conversations left for this agent — go back to the agent page
        // instead of silently starting a fresh conversation, so deleting the
        // last chat doesn't just spawn another one in its place.
        var hasRemainingForAgent = sessionsState.Sessions.Any(entry => entry.AgentId == session.AgentId);
        if (!hasRemainingForAgent)
        {
            if (sessionsState.ActiveSessionId == sessionId) sessionsState.ActiveSessionId = null;
            await AppServices.Sessions.SaveAsync(sessionsState);

            var agentsState = await AppServices.Agents.LoadAsync();
            agentsState.SelectedAgentId = session.AgentId;
            await AppServices.Agents.SaveAsync(agentsState);

            await RefreshAsync();
            RequestNavigation(typeof(AgentDetailPage), forceReload: true);
            return;
        }

        if (sessionsState.ActiveSessionId == sessionId)
        {
            sessionsState.ActiveSessionId = sessionsState.Sessions
                .Where(entry => entry.AgentId == session.AgentId)
                .OrderByDescending(entry => entry.UpdatedAt, StringComparer.Ordinal)
                .FirstOrDefault()?.Id
                ?? sessionsState.Sessions
                    .OrderByDescending(entry => entry.UpdatedAt, StringComparer.Ordinal)
                    .FirstOrDefault()?.Id;
        }
        await AppServices.Sessions.SaveAsync(sessionsState);

        await RefreshAsync();
        RequestNavigation(typeof(ChatPage), forceReload: true);
        }
        catch { /* Ignore errors during session deletion. */ }
    }

    private async void AddAgent_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
        if (sender is FrameworkElement opener)
            AddAgentRequested?.Invoke(opener);
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
        if (sender is FrameworkElement opener)
            SettingsRequested?.Invoke(opener);
    }

    private async void BottomAddAgent_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
        AddAgentRequested?.Invoke(BottomAccountButton);
    }

    private async void BottomSettings_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
        SettingsRequested?.Invoke(BottomAccountButton);
    }

    private void BottomKeyboardShortcuts_Click(object sender, RoutedEventArgs e)
        => KeyboardShortcutsRequested?.Invoke(BottomAccountButton);

    private void BottomAbout_Click(object sender, RoutedEventArgs e)
        => AboutRequested?.Invoke(BottomAccountButton);

    private void BottomExit_Click(object sender, RoutedEventArgs e)
        => ExitRequested?.Invoke();

    private async void BottomTheme_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string value
            || !Enum.TryParse<ThemeMode>(value, ignoreCase: true, out var theme))
        {
            return;
        }

        var preferences = await AppServices.Preferences.LoadAsync();
        preferences.Theme = theme;
        await AppServices.Preferences.SaveAsync(preferences);
        ThemeService.Apply(theme);
    }
}
