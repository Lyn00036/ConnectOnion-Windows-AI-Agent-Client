using System;
using System.Linq;
using System.Threading;
using ConnectOnion.WinUIClient.Common;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Models.Notifications;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.Views;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace ConnectOnion.WinUIClient.Controls;

// User-interaction entry points for the sidebar: row hover/focus handlers and the
// routed Click handlers for add-chat / add-agent / icon / pin / delete / settings. Each
// delegates to the state/refresh logic in ShellSidebar.xaml.cs (RefreshAsync,
// SelectSessionAsync, ConfirmDeleteAsync, RequestNavigation);
// split out so the main file holds the data/presence model, not the wiring.
public sealed partial class ShellSidebar
{
    /// <summary>Guards the icon menu items against re-entry. Both are <c>async void</c> handlers
    /// that await a file picker, so without this a second click while the picker is open would
    /// commit a second file and leave the first orphaned on disk.</summary>
    private int _iconOperationInProgress;

    // Pre-compiled message delegates rather than the ILogger extension methods, which is what
    // CA1848 asks for and what NotificationLog already does.
    private static readonly Action<ILogger, string, Exception?> LogIconCleanupFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(LogIconCleanupFailed)),
            "Could not delete agent icon {IconPath}.");

    private static readonly Action<ILogger, string, Exception?> LogIconOperationFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2, nameof(LogIconOperationFailed)),
            "Agent icon operation failed for {AgentId}.");

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

    /// <summary>
    /// Replaces the agent's avatar with an image the user picks.
    ///
    /// The order — commit the file, then save the row, then delete the old file — is what keeps
    /// the two stores consistent under a failure: a committed file with no row is an orphan the
    /// next sweep can remove, whereas a row pointing at a file that was deleted first is a
    /// permanently broken avatar. Committed names carry a GUID, so the file just written can
    /// never be the one being deleted.
    /// </summary>
    private async void ChangeAgentIcon_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string agentId) return;
        if (Interlocked.Exchange(ref _iconOperationInProgress, 1) != 0) return;

        string? temporaryPath = null;
        try
        {
            temporaryPath = await AppServices.AgentIcons.PickTemporaryIconAsync();
            // Dismissing the picker is not a failure and gets no toast.
            if (temporaryPath is null) return;

            // Loaded after the picker closes, not before: the agent can be deleted from another
            // surface while a modal file dialog is open, and the stale copy would resurrect it.
            var agentsState = await AppServices.Agents.LoadAsync();
            var agent = agentsState.Agents.FirstOrDefault(entry => entry.Id == agentId);
            if (agent is null) return;

            var previousIconPath = agent.IconPath;
            var committedPath = await AppServices.AgentIcons.CommitTemporaryIconAsync(agentId, temporaryPath);
            // The commit moved the file, so the finally block below has nothing left to clean up.
            temporaryPath = null;

            agent.IconPath = committedPath;
            try
            {
                await AppServices.Agents.SaveAsync(agentsState);
            }
            catch
            {
                await DeleteIconQuietlyAsync(committedPath);
                throw;
            }

            await DeleteIconQuietlyAsync(previousIconPath);
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            ReportIconFailure(
                agentId,
                exception,
                "AgentIconChangeFailed",
                "The agent icon could not be changed.");
        }
        finally
        {
            await DeleteTemporaryIconQuietlyAsync(temporaryPath);
            Volatile.Write(ref _iconOperationInProgress, 0);
        }
    }

    /// <summary>Drops the custom icon and restores the name-initial avatar.</summary>
    private async void RemoveAgentIcon_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string agentId) return;
        if (Interlocked.Exchange(ref _iconOperationInProgress, 1) != 0) return;

        try
        {
            var agentsState = await AppServices.Agents.LoadAsync();
            var agent = agentsState.Agents.FirstOrDefault(entry => entry.Id == agentId);
            if (agent is null || string.IsNullOrWhiteSpace(agent.IconPath)) return;

            // Clear the reference before touching the file, for the same reason the change path
            // commits before saving: an orphaned file is recoverable, a dangling row is not.
            var previousIconPath = agent.IconPath;
            agent.IconPath = null;
            await AppServices.Agents.SaveAsync(agentsState);

            await DeleteIconQuietlyAsync(previousIconPath);
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            ReportIconFailure(
                agentId,
                exception,
                "AgentIconRemoveFailed",
                "The agent icon could not be removed.");
        }
        finally
        {
            Volatile.Write(ref _iconOperationInProgress, 0);
        }
    }

    /// <summary>Deletes a committed icon, swallowing failure. Every caller has already put the
    /// database in the state it wants; a file that outlives its row is untidy, not broken.</summary>
    private static async System.Threading.Tasks.Task DeleteIconQuietlyAsync(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return;

        try
        {
            await AppServices.AgentIcons.DeleteIconAsync(relativePath);
        }
        catch (Exception exception)
        {
            LogIconCleanupFailed(
                AppServices.Logging.CreateLogger<ShellSidebar>(), relativePath, exception);
        }
    }

    private static async System.Threading.Tasks.Task DeleteTemporaryIconQuietlyAsync(string? temporaryPath)
    {
        if (string.IsNullOrWhiteSpace(temporaryPath)) return;

        try
        {
            await AppServices.AgentIcons.DeleteTemporaryIconAsync(temporaryPath);
        }
        catch (Exception exception)
        {
            LogIconCleanupFailed(
                AppServices.Logging.CreateLogger<ShellSidebar>(), temporaryPath, exception);
        }
    }

    /// <summary>
    /// Surfaces an icon failure as a toast rather than a dialog. The icon is decoration: a modal
    /// would interrupt whatever conversation is on screen to announce that an avatar stayed the
    /// way it already was.
    /// </summary>
    private void ReportIconFailure(string agentId, Exception exception, string resourceKey, string fallback)
    {
        LogIconOperationFailed(
            AppServices.Logging.CreateLogger<ShellSidebar>(), agentId, exception);

        MainWindow.FromXamlRoot(XamlRoot)?.ShowInAppToast(new InAppToastModel(
            LocalizedStrings.Get("AgentIconToastTitle", "Agent icon"),
            LocalizedStrings.Get(resourceKey, fallback),
            NotificationType.Error,
            AgentId: agentId,
            ConversationId: null,
            ActionId: null));
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

        // Read before the row goes: once the agent is removed from storage nothing points at its
        // icon file any more, and the path would be unrecoverable.
        var iconPath = agent.IconPath;

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

        // Only now: the row is gone, so deleting the file cannot leave a live agent pointing at
        // one that no longer exists.
        await DeleteIconQuietlyAsync(iconPath);

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
        CloseAccountFlyout();
        await RefreshAsync();
        SettingsRequested?.Invoke(BottomAccountButton);
    }

    private void BottomKeyboardShortcuts_Click(object sender, RoutedEventArgs e)
    {
        CloseAccountFlyout();
        KeyboardShortcutsRequested?.Invoke(BottomAccountButton);
    }

    private void BottomAbout_Click(object sender, RoutedEventArgs e)
    {
        CloseAccountFlyout();
        AboutRequested?.Invoke(BottomAccountButton);
    }

    private void BottomExit_Click(object sender, RoutedEventArgs e)
    {
        CloseAccountFlyout();
        ExitRequested?.Invoke();
    }

    private async void BottomTheme_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string value
            || !Enum.TryParse<ThemeMode>(value, ignoreCase: true, out var theme))
        {
            return;
        }

        CloseAccountFlyout();
        var preferences = await AppServices.Preferences.LoadAsync();
        preferences.Theme = theme;
        await AppServices.Preferences.SaveAsync(preferences);
        ThemeService.Apply(theme);
    }

    private void CloseAccountFlyout()
        => BottomAccountButton.Flyout?.Hide();
}
