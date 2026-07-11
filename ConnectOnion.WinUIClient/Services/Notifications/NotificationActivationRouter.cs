using System;
using System.Collections.Generic;
using Microsoft.Windows.AppNotifications;

namespace ConnectOnion.WinUIClient.Services.Notifications;

/// <summary>
/// Registers for App Notification activation and turns a clicked toast's
/// arguments into a navigation. Buffers a click that arrives before the UI is
/// ready (cold start) and replays it once <see cref="MarkReady"/> is called.
/// </summary>
public sealed class NotificationActivationRouter
{
    private readonly ConversationNavigationService _navigation;
    private readonly object _gate = new();
    private (string? AgentId, string? ConversationId)? _pending;
    private bool _ready;

    public NotificationActivationRouter(ConversationNavigationService navigation)
        => _navigation = navigation;

    /// <summary>Registers the app for notifications and subscribes to activation.
    /// Safe to call once at startup for both packaged and unpackaged apps.</summary>
    public void RegisterAndListen()
    {
        try
        {
            var manager = AppNotificationManager.Default;
            manager.NotificationInvoked += OnNotificationInvoked;
            manager.Register();
        }
        catch (Exception ex)
        {
            NotificationLog.Warn("notification registration failed", ex);
        }
    }

    public void Unregister()
    {
        try { AppNotificationManager.Default.Unregister(); }
        catch (Exception ex) { NotificationLog.Warn("notification unregister failed", ex); }
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        try { HandleArguments(args.Arguments); }
        catch (Exception ex) { NotificationLog.Warn("activation handling failed", ex); }
    }

    /// <summary>Handles a cold-start activation (from <c>AppInstance.GetActivatedEventArgs</c>).</summary>
    public void HandleColdStart(IDictionary<string, string> arguments)
    {
        try { HandleArguments(arguments); }
        catch (Exception ex) { NotificationLog.Warn("cold-start activation failed", ex); }
    }

    private void HandleArguments(IDictionary<string, string> arguments)
    {
        if (arguments is null || !arguments.TryGetValue("action", out var action)) return;
        if (action is not ("openConversation" or "openApproval")) return;

        arguments.TryGetValue("agentId", out var agentId);
        arguments.TryGetValue("conversationId", out var conversationId);
        Route(agentId, conversationId);
    }

    private void Route(string? agentId, string? conversationId)
    {
        lock (_gate)
        {
            if (!_ready)
            {
                // UI/navigation not ready yet — remember the latest click and replay later.
                _pending = (agentId, conversationId);
                return;
            }
        }

        _navigation.OpenConversation(agentId, conversationId);
    }

    /// <summary>Signals the UI is ready; replays any buffered cold-start click.</summary>
    public void MarkReady()
    {
        (string? AgentId, string? ConversationId)? pending;
        lock (_gate)
        {
            _ready = true;
            pending = _pending;
            _pending = null;
        }

        if (pending is { } p) _navigation.OpenConversation(p.AgentId, p.ConversationId);
    }
}
