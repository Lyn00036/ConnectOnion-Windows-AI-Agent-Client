using System;
using System.Diagnostics;
using System.IO;
using Microsoft.UI.Xaml.Media;
using Serilog;

namespace ConnectOnion.WinUIClient.Diagnostics;

/// <summary>
/// Measures how long this process took to put a drawn window on screen, and reports it once.
///
/// The origin is the OS process-start time, not a stopwatch started in managed code: on a cold
/// start most of the wall clock is spent in CLR + Windows App SDK bootstrap *before* the
/// <c>App</c> constructor runs, and a stopwatch started there would report a flattering number
/// that no user experiences. <see cref="StartupProfiler"/> holds the (platform-free) collection
/// logic; this type owns the two pieces that need the framework — the process clock and the
/// compositor's first frame.
///
/// "First frame" comes from <see cref="CompositionTarget.Rendering"/>, which fires on the first
/// composition pass after activation. The handler unsubscribes itself on that first tick: an
/// always-armed per-frame callback is exactly the kind of thing the dispatcher keeps calling
/// after <c>Window.Closed</c> (see CLAUDE.md's shutdown-race note), so this must not outlive
/// the measurement it exists for.
///
/// Cost when not benchmarking is one file-less log line — the JSON report is written only when
/// <see cref="StartupProfiler.OutputPathEnvironmentVariable"/> points somewhere.
/// </summary>
internal static class StartupTelemetry
{
    private static readonly DateTime ProcessStartUtc = ResolveProcessStartUtc();

    private static readonly StartupProfiler Profiler =
        new(() => (DateTime.UtcNow - ProcessStartUtc).TotalMilliseconds);

    /// <summary>Records a launch phase. Safe to call from any thread; later duplicates of an
    /// already-recorded phase are ignored.</summary>
    internal static void Mark(string phase) => Profiler.Mark(phase);

    /// <summary>
    /// Arms the first-frame measurement. Call immediately after <c>Window.Activate()</c> —
    /// subscribing earlier would catch a composition pass from before the window existed.
    /// </summary>
    internal static void TrackFirstFrame()
    {
        if (Profiler.HasMark(StartupPhases.FirstFrame)) return;
        CompositionTarget.Rendering += OnRendering;
    }

    private static void OnRendering(object? sender, object e)
    {
        CompositionTarget.Rendering -= OnRendering;
        Profiler.Mark(StartupPhases.FirstFrame);
        Report();
    }

    private static void Report()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();

            var metrics = Profiler.Snapshot(
                new StartupMemory(
                    process.WorkingSet64,
                    process.PrivateMemorySize64,
                    GC.GetTotalMemory(forceFullCollection: false)),
                Configuration,
                IsPackaged());

            Log.Information("{StartupSummary}", metrics.ToLogLine());
            WriteReportIfRequested(metrics);
        }
        catch (Exception ex)
        {
            // Telemetry must never be able to break a launch it is only observing.
            Log.Warning(ex, "Startup telemetry could not be captured");
        }
    }

    private static void WriteReportIfRequested(StartupMetrics metrics)
    {
        var path = Environment.GetEnvironmentVariable(StartupProfiler.OutputPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(path)) return;

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        // Written via a temp file + move so the harness, which polls for this path, can never
        // read a half-serialized document and conclude the run produced garbage.
        var temp = path + ".tmp";
        File.WriteAllText(temp, metrics.ToJson());
        File.Move(temp, path, overwrite: true);
    }

    private static string Configuration =>
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    private static bool IsPackaged()
    {
        // Package.Current throws when running unpackaged (dotnet run -p:RunUnpackaged=true);
        // that throw is the only reliable way to tell the two apart. Same trick as AppVersionService.
        try
        {
            _ = Windows.ApplicationModel.Package.Current.Id.Version;
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Process.StartTime can throw on a locked-down process; falling back to "now" makes that run
    // report an implausibly small number rather than losing the whole measurement, and the
    // harness's outlier reporting is what catches it.
    private static DateTime ResolveProcessStartUtc()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return process.StartTime.ToUniversalTime();
        }
        catch
        {
            return DateTime.UtcNow;
        }
    }
}
