using System;
using System.Threading;

namespace ConnectOnion.WinUIClient.Services.Notifications;

/// <summary>Real <see cref="INotificationScheduler"/> backed by a one-shot timer.</summary>
public sealed class ThreadingScheduler : INotificationScheduler
{
    public IDisposable Schedule(TimeSpan delay, Action callback)
        => new Timer(_ =>
        {
            try { callback(); }
            catch (Exception ex) { NotificationLog.Warn("scheduled callback failed", ex); }
        }, null, delay, Timeout.InfiniteTimeSpan);
}
