namespace ConnectOnion.WinUIClient.Services.Notifications;

/// <summary>
/// Routes a "open this conversation" intent (from a clicked notification) to the
/// right window without ever unconditionally spawning a new one: reuse a window
/// already showing it, else the foreground/primary window (restoring it from tray
/// first). Missing conversations fail safe.
/// </summary>
public sealed class ConversationNavigationService
{
    private readonly WindowPresenceService _windows;

    public ConversationNavigationService(WindowPresenceService windows) => _windows = windows;

    public void OpenConversation(string? agentId, string? conversationId)
    {
        // No conversation target — just surface a window.
        if (string.IsNullOrEmpty(conversationId) || string.IsNullOrEmpty(agentId))
        {
            var any = _windows.ForegroundWindow();
            any?.DispatcherQueue.TryEnqueue(any.RestoreAndActivate);
            return;
        }

        var target = _windows.FindViewing(conversationId) ?? _windows.ForegroundWindow();
        if (target is null) return; // no windows registered (app fully closed) — nothing to do

        target.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                target.RestoreAndActivate();
                await target.ShowConversationAsync(agentId, conversationId);
            }
            catch (System.Exception ex)
            {
                // Deleted conversation / window tearing down — don't crash on a click.
                NotificationLog.Warn("navigation failed", ex);
            }
        });
    }
}
