using FlaUI.Core;
using FlaUI.UIA3;

namespace ConnectOnion.WinUIClient.UITests;

public sealed class ShellSmokeTests
{
    public const string ExecutableEnvironmentVariable = "CONNECTONION_UI_TEST_EXE";

    [Fact]
    public void Launch_ShowsResponsiveConnectOnionWindow()
    {
        var executable = Environment.GetEnvironmentVariable(ExecutableEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(executable))
        {
            // Opt-in because hosted CI agents often have no interactive desktop. The documented
            // UI-smoke workflow sets the path after publishing an unpackaged x64 build.
            return;
        }

        Assert.True(File.Exists(executable), $"UI test executable not found: {executable}");

        var existingProcessIds = System.Diagnostics.Process
            .GetProcessesByName("ConnectOnion.WinUIClient")
            .Select(process => process.Id)
            .ToHashSet();

        using var launchedProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable)
        {
            UseShellExecute = true,
        });
        Assert.NotNull(launchedProcess);

        // An unpackaged WinUI executable can hand off to the Windows App SDK host, so the
        // process returned by Start may exit before the real top-level window is created.
        System.Diagnostics.Process? windowProcess = null;
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline && windowProcess is null)
        {
            windowProcess = System.Diagnostics.Process
                .GetProcessesByName("ConnectOnion.WinUIClient")
                .FirstOrDefault(process => !existingProcessIds.Contains(process.Id) && process.MainWindowHandle != IntPtr.Zero);
            if (windowProcess is null)
            {
                Thread.Sleep(200);
            }
        }

        Assert.NotNull(windowProcess);
        using var attachedProcess = windowProcess;
        using var app = Application.Attach(windowProcess.Id);
        using var automation = new UIA3Automation();
        var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(20));

        Assert.NotNull(window);
        Assert.Contains("ConnectOnion", window.Title, StringComparison.OrdinalIgnoreCase);
        Assert.True(window.IsEnabled);
        Assert.NotNull(window.FindFirstDescendant(query => query.ByAutomationId("SettingsButton")));

        // The product's normal window Close action can minimize to the tray by user
        // preference. This process is owned by the smoke test, so terminate it explicitly.
        app.Kill();
    }
}
