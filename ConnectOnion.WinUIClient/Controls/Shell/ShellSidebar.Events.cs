using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace ConnectOnion.WinUIClient.Controls;

// User-interaction entry points for the sidebar: row hover/focus handlers and the
// routed Click handlers for navigation, icon changes, add-agent, pin, delete, and
// settings. Ordinary navigation updates existing row selection in place; only real
// data changes call RefreshAsync and rebuild the collections.
public sealed partial class ShellSidebar
{
    private bool _isChangingAgentIcon;

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
        if ((sender as FrameworkElement)?.Tag is not string agentId)
        {
            return;
        }

        var agentsState = await AppServices.Agents.LoadAsync();
        agentsState.SelectedAgentId = agentId;
        await AppServices.Agents.SaveAsync(agentsState);

        _selectedAgentId = agentId;
        _currentPageType = typeof(AgentDetailPage);

        FocusAgentInSidebar(agentId);
        UpdateNewChatTargetText(agentsState);
        ApplySelectionState();

        RequestNavigation(
            typeof(AgentDetailPage),
            forceReload: true);
    }

    /// <summary>
    /// Lets the user choose a replacement custom icon for the Agent represented
    /// by the sidebar row that opened the More actions menu.
    /// </summary>
    private async void ChangeAgentIcon_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isChangingAgentIcon)
        {
            return;
        }

        if ((sender as FrameworkElement)?.Tag is not string agentId)
        {
            return;
        }

        _isChangingAgentIcon = true;

        string? temporaryPath = null;
        string? committedPath = null;
        string? previousIconPath = null;
        var databaseUpdated = false;

        try
        {
            var agentsState = await AppServices.Agents.LoadAsync();

            var agent = agentsState.Agents.FirstOrDefault(
                entry => string.Equals(
                    entry.Id,
                    agentId,
                    StringComparison.Ordinal));

            if (agent is null)
            {
                return;
            }

            previousIconPath = agent.IconPath;

            temporaryPath =
                await AppServices.AgentIcons.PickTemporaryIconAsync(
                    CancellationToken.None);

            // Closing the picker without choosing a file is not an error.
            if (string.IsNullOrWhiteSpace(temporaryPath))
            {
                return;
            }

            committedPath =
                await AppServices.AgentIcons.CommitTemporaryIconAsync(
                    agentId,
                    temporaryPath,
                    CancellationToken.None);

            if (string.IsNullOrWhiteSpace(committedPath))
            {
                throw new InvalidOperationException(
                    "The selected icon could not be saved.");
            }

            agent.IconPath = committedPath;
            await AppServices.Agents.SaveAsync(agentsState);
            databaseUpdated = true;

            /*
             * Delete the previous icon only when the committed relative path
             * differs. Some implementations intentionally overwrite the same
             * per-Agent PNG path; deleting an equal path here would remove the
             * newly committed image.
             */
            if (!string.IsNullOrWhiteSpace(previousIconPath) &&
                !string.Equals(
                    previousIconPath,
                    committedPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await AppServices.AgentIcons.DeleteIconAsync(
                        previousIconPath,
                        CancellationToken.None);
                }
                catch (Exception deleteException)
                {
                    // The new icon and its database reference are already valid.
                    // Old-file cleanup is best-effort only.
                    Debug.WriteLine(
                        $"Could not delete previous Agent icon " +
                        $"'{previousIconPath}': {deleteException}");
                }
            }

            // RefreshAsync rebuilds ShellAgentItem objects and their AgentAvatar
            // controls, so the newly persisted ImagePath is loaded immediately.
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Could not change icon for Agent '{agentId}': {exception}");

            /*
             * A uniquely named committed file can become orphaned when the
             * database save fails. Do not delete it when it equals the previous
             * path, because in that case it may have replaced the active file.
             */
            if (!databaseUpdated &&
                !string.IsNullOrWhiteSpace(committedPath) &&
                !string.Equals(
                    committedPath,
                    previousIconPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await AppServices.AgentIcons.DeleteIconAsync(
                        committedPath,
                        CancellationToken.None);
                }
                catch
                {
                    // Best-effort rollback only.
                }
            }

            await ShowAgentIconErrorAsync(
                $"The Agent icon could not be changed.\n\n{exception.Message}");
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(temporaryPath))
            {
                try
                {
                    await AppServices.AgentIcons.DeleteTemporaryIconAsync(
                        temporaryPath,
                        CancellationToken.None);
                }
                catch
                {
                    // The commit operation may already have moved or deleted it.
                }
            }

            _isChangingAgentIcon = false;
        }
    }

    /// <summary>
    /// Removes the current Agent's custom icon and restores the deterministic
    /// initial-and-palette avatar.
    /// </summary>
    private async void RemoveAgentIcon_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isChangingAgentIcon)
        {
            return;
        }

        if ((sender as FrameworkElement)?.Tag is not string agentId)
        {
            return;
        }

        _isChangingAgentIcon = true;

        try
        {
            var agentsState = await AppServices.Agents.LoadAsync();

            var agent = agentsState.Agents.FirstOrDefault(
                entry => string.Equals(
                    entry.Id,
                    agentId,
                    StringComparison.Ordinal));

            if (agent is null)
            {
                return;
            }

            var previousIconPath = agent.IconPath;

            // There is no custom icon to remove.
            if (string.IsNullOrWhiteSpace(previousIconPath))
            {
                return;
            }

            // Remove the database reference before deleting the managed file.
            agent.IconPath = null;
            await AppServices.Agents.SaveAsync(agentsState);

            try
            {
                await AppServices.AgentIcons.DeleteIconAsync(
                    previousIconPath,
                    CancellationToken.None);
            }
            catch (Exception deleteException)
            {
                // The database no longer references this path. A leftover file
                // must not make the visible remove operation fail.
                Debug.WriteLine(
                    $"Could not delete Agent icon " +
                    $"'{previousIconPath}': {deleteException}");
            }

            await RefreshAsync();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Could not remove icon for Agent '{agentId}': {exception}");

            await ShowAgentIconErrorAsync(
                $"The Agent icon could not be removed.\n\n{exception.Message}");
        }
        finally
        {
            _isChangingAgentIcon = false;
        }
    }

    /// <summary>
    /// Displays an icon-operation error without navigating away from the
    /// current conversation or Agent page.
    /// </summary>
    private async System.Threading.Tasks.Task ShowAgentIconErrorAsync(
        string message)
    {
        if (XamlRoot is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Agent icon",
            Content = message,
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close,
        };

        await dialog.ShowAsync();
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

            // Keep the managed icon path before removing the Agent from storage.
            // The database reference is cleared first; file deletion is best-effort afterwards.
            var iconPath = agent.IconPath;

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

            // The Agent no longer references the icon in SQLite, so its managed
            // file can now be removed without risking a broken database reference.
            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                try
                {
                    await AppServices.AgentIcons.DeleteIconAsync(
                        iconPath,
                        CancellationToken.None);
                }
                catch
                {
                    // The Agent deletion itself succeeded. An orphaned icon file
                    // must not make the whole delete operation appear to fail.
                }
            }

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

    private void AddAgent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement opener)
        {
            AddAgentRequested?.Invoke(opener);
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement opener)
        {
            SettingsRequested?.Invoke(opener);
        }
    }

    private void BottomAddAgent_Click(object sender, RoutedEventArgs e)
    {
        AddAgentRequested?.Invoke(BottomAccountButton);
    }

    private void BottomSettings_Click(object sender, RoutedEventArgs e)
    {
        CloseAccountFlyout();
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
