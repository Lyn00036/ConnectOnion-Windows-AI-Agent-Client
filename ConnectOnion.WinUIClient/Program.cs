using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace ConnectOnion.WinUIClient;

/// <summary>
/// Custom entry point that enforces a single application instance. A second launch
/// redirects its activation arguments to the already-running instance and exits
/// before WinUI starts, so only the first process ever constructs a window. The
/// running instance is woken (not duplicated) through <see cref="AppInstance.Activated"/>.
/// </summary>
public static class Program
{
    /// <summary>Key under which the main instance registers itself.</summary>
    public const string MainInstanceKey = "ConnectOnion.Main";

    [STAThread]
    public static void Main(string[] args)
    {
        // Register as the main instance, or — if one is already running — hand this
        // launch over to it and exit. This must happen before Application.Start so no
        // second window/Application is ever constructed.
        var instance = AppInstance.FindOrRegisterForKey(MainInstanceKey);
        if (!instance.IsCurrent)
        {
            RedirectAndExit(instance);
            return;
        }

        // The main instance receives every subsequent (redirected) launch here. The
        // handler wakes the existing window and, when the launch came from a clicked
        // toast, navigates to its target conversation; it never creates a window.
        instance.Activated += (_, e) => App.OnActivationRedirected(e);

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }

    /// <summary>Forwards this process's activation to the running main instance, then exits.
    /// Redirect failure still exits so a duplicate process is never left running.</summary>
    private static void RedirectAndExit(AppInstance main)
    {
        try
        {
            // Capture THIS process's activation (the toast click carries its arguments
            // here), then hand it to the running instance. Must be GetCurrent(), not
            // main.GetActivatedEventArgs(): the latter reads the primary instance's
            // activation, so the notification's agentId/conversationId never travel and
            // the woken window can't navigate to the message's conversation.
            var activated = AppInstance.GetCurrent().GetActivatedEventArgs();
            // Blocking cannot deadlock here: this runs before Application.Start, so no
            // SynchronizationContext exists yet, and Main must not return until the
            // redirect is done or this process races the instance it just woke.
#pragma warning disable VSTHRD002
            main.RedirectActivationToAsync(activated).AsTask().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
        }
        catch
        {
            // Best-effort: even if the redirect fails, this process must not continue
            // and spawn a second window alongside the existing instance.
        }
    }
}
