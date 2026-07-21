using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ConnectOnion.WinUIClient.Services.Notifications;

/// <summary>Minimal, never-throwing log sink for the notification subsystem.
/// Notification failures are diagnostics only — they must not disturb chat.
/// Also appended to a file so activation flow can be traced without a debugger
/// attached (a toast click launches a short-lived second process whose Debug
/// output is otherwise invisible).</summary>
public static class NotificationLog
{
    private static readonly Action<ILogger, string, Exception?> LogWarning =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(1, nameof(Warn)), "{NotificationMessage}");
    private static readonly Action<ILogger, string, Exception?> LogInformation =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(2, nameof(Info)), "{NotificationMessage}");
    private static ILogger? _logger;

    public static void Configure(ILoggerFactory loggerFactory)
        => _logger = loggerFactory.CreateLogger("ConnectOnion.Notifications");

    public static void Warn(string message, Exception? ex = null)
    {
        Debug.WriteLine($"[Notify] WARN {message}: {ex}");
        if (_logger is { } logger)
        {
            LogWarning(logger, message, ex);
        }
    }

    public static void Info(string message)
    {
        Debug.WriteLine($"[Notify] INFO {message}");
        if (_logger is { } logger)
        {
            LogInformation(logger, message, null);
        }
    }
}
