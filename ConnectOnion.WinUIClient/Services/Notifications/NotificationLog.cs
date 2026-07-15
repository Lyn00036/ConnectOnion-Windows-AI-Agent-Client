using System;
using System.Diagnostics;
using System.IO;

namespace ConnectOnion.WinUIClient.Services.Notifications;

/// <summary>Minimal, never-throwing log sink for the notification subsystem.
/// Notification failures are diagnostics only — they must not disturb chat.
/// Also appended to a file so activation flow can be traced without a debugger
/// attached (a toast click launches a short-lived second process whose Debug
/// output is otherwise invisible).</summary>
public static class NotificationLog
{
    private static readonly object Gate = new();

    private static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ConnectOnion", "notifications.log");

    public static void Warn(string message, Exception? ex = null)
        => Write("WARN", $"{message}{(ex is null ? "" : $": {ex.Message}")}");

    public static void Info(string message) => Write("INFO", message);

    private static void Write(string level, string message)
    {
        var line = $"[Notify] {level} {message}";
        Debug.WriteLine(line);
        try
        {
            var stamped = $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}";
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, stamped);
            }
        }
        catch
        {
            // Logging is best-effort; never throw from the notification path.
        }
    }
}
