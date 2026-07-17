using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace ConnectOnion.WinUIClient.Services;

/// <summary>
/// Reads the stored shortcut overrides once, when the host starts. Separate from the notification
/// startup wiring because it has nothing to do with notifications — it just has the same "load
/// persisted state before the window can use it" shape.
///
/// <para>The service answers with catalog defaults until this completes, so a keystroke landing
/// during startup still does the right thing for anyone who never rebound anything.</para>
/// </summary>
public sealed class KeyboardShortcutStartupService : IHostedService
{
    private readonly KeyboardShortcutService _shortcuts;

    public KeyboardShortcutStartupService(KeyboardShortcutService shortcuts) => _shortcuts = shortcuts;

    public Task StartAsync(CancellationToken cancellationToken) => _shortcuts.LoadAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
