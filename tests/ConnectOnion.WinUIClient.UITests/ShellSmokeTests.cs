using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace ConnectOnion.WinUIClient.UITests;

public sealed class ShellSmokeTests
{
    public const string ExecutableEnvironmentVariable = "CONNECTONION_UI_TEST_EXE";

    [Fact]
    public void Launch_ShowsResponsiveConnectOnionWindow()
    {
        using var launched = LaunchApp();
        if (launched is null) return;

        var window = launched.Window;
        Assert.Contains("ConnectOnion", window.Title, StringComparison.OrdinalIgnoreCase);
        Assert.True(window.IsEnabled);
        Assert.NotNull(window.FindFirstDescendant(query => query.ByAutomationId("SettingsButton")));
    }

    /// <summary>
    /// Clicking the sidebar's Settings button shows the settings overlay. This is the first
    /// coverage of an actual interaction (the other test only asserts the shell renders), and it
    /// is what catches a change to how the overlay is constructed or wired — an attempt to defer
    /// it with <c>x:Load</c> failed exactly here, silently, with the button doing nothing.
    /// </summary>
    [Fact]
    public void SettingsButton_Click_ShowsSettingsOverlay()
    {
        using var launched = LaunchApp();
        if (launched is null) return;

        var window = launched.Window;

        // Asserted on the overlay's *content*, not on the SettingsOverlay element itself: that
        // UserControl carries an AutomationId in XAML but produces no automation peer of its own,
        // so UIA never exposes it. GeneralNav is inside the overlay and appears only once it opens.
        const string settingsContentId = "GeneralNav";

        Assert.Null(window.FindFirstDescendant(query => query.ByAutomationId(settingsContentId)));

        var settingsButton = window.FindFirstDescendant(query => query.ByAutomationId("SettingsButton"));
        Assert.NotNull(settingsButton);
        settingsButton.AsButton().Invoke();

        // The overlay's show animation is not instant; poll rather than sleep a fixed amount, so
        // a slow machine does not fail a working build.
        //
        // The COMException catch is required, not defensive: UIA throws E_UNEXPECTED
        // ("Catastrophic failure") when a FindFirst races a visual-tree mutation, which is
        // exactly what showing the overlay is. It means "ask again", not "broken".
        AutomationElement? overlay = null;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && overlay is null)
        {
            try
            {
                overlay = window.FindFirstDescendant(query => query.ByAutomationId(settingsContentId));
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // Tree changed under the query; retry below.
            }

            if (overlay is null) Thread.Sleep(150);
        }

        Assert.NotNull(overlay);
    }

    /// <summary>
    /// The sidebar's "Add agent" button navigates to the landing page *and* opens its add-agent
    /// form. Clicking it and landing on a page where the form is still collapsed — which is what
    /// the button used to do — reads as a dead button, so the form's input is the assertion.
    /// </summary>
    [Fact]
    public void SidebarAddAgent_Click_OpensAddAgentFormOnHome()
    {
        using var launched = LaunchApp();
        if (launched is null) return;

        var window = launched.Window;

        // Collapsed controls are not in the UIA tree, so absence here is what "the form is shut"
        // means — and its later presence is what proves the click opened it.
        Assert.Null(window.FindFirstDescendant(query => query.ByAutomationId("AgentAddressInput")));

        var addAgent = window.FindFirstDescendant(query => query.ByAutomationId("AddAgentButton"));
        Assert.NotNull(addAgent);
        addAgent.AsButton().Invoke();

        var input = WaitForDescendant(window, "AgentAddressInput");
        Assert.NotNull(input);

        // The form is opened *focused* so the user can paste an address immediately; without
        // this the click would still leave them one more click from typing.
        Assert.True(input.Properties.HasKeyboardFocus.ValueOrDefault, "add-agent input did not take focus");
    }

    /// <summary>
    /// The token-usage heatmap renders a full grid of day squares in Settings → Usage.
    ///
    /// <para>Asserted through a day square's accessible name rather than the control's own
    /// AutomationId: the squares are Buttons and therefore real automation peers, whereas the
    /// UserControl wrapping them produces none (the same trap that made an earlier attempt at
    /// this assert nothing). It doubles as the accessibility check — a keyboard or screen-reader
    /// user must be told what a square means, and this is that text.</para>
    /// </summary>
    [Fact]
    public void UsageHeatmap_RendersDaySquares_WithAccessibleDescriptions()
    {
        using var launched = LaunchApp();
        if (launched is null) return;

        var window = launched.Window;

        window.FindFirstDescendant(query => query.ByAutomationId("SettingsButton"))?.AsButton().Invoke();
        Assert.NotNull(WaitForDescendant(window, "GeneralNav"));

        var usageNav = window.FindFirstDescendant(query => query.ByName("Token usage"));
        Assert.NotNull(usageNav);
        usageNav.AsRadioButton().Click();

        // A fresh profile has no usage, so every square is an empty day — which is exactly the
        // case worth pinning: the grid must still render rather than collapsing to nothing.
        AutomationElement? square = null;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && square is null)
        {
            try
            {
                // Properties.Name.ValueOrDefault, not .Name: the latter throws
                // PropertyNotSupportedException on elements that expose no Name at all, and this
                // sweep necessarily walks over some of those.
                square = window
                    .FindAllDescendants(query => query.ByControlType(FlaUI.Core.Definitions.ControlType.Button))
                    .FirstOrDefault(e => e.Properties.Name.ValueOrDefault?
                        .StartsWith("No token usage", StringComparison.Ordinal) == true);
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // Tree still settling; retry below.
            }

            if (square is null) Thread.Sleep(200);
        }

        Assert.NotNull(square);
    }

    /// <summary>Polls for a descendant, tolerating the transient UIA failures a mutating visual
    /// tree produces (see the note in <see cref="SettingsButton_Click_ShowsSettingsOverlay"/>).</summary>
    private static AutomationElement? WaitForDescendant(Window window, string automationId)
    {
        AutomationElement? found = null;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && found is null)
        {
            try
            {
                found = window.FindFirstDescendant(query => query.ByAutomationId(automationId));
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // Tree changed under the query; retry below.
            }

            if (found is null) Thread.Sleep(150);
        }

        return found;
    }

    /// <summary>
    /// Launches the app and attaches automation to its window, or returns null when the opt-in
    /// environment variable is unset. Opt-in because hosted CI agents often have no interactive
    /// desktop; the documented UI-smoke workflow sets the path after publishing an unpackaged
    /// x64 build.
    /// </summary>
    internal static LaunchedApp? LaunchApp()
    {
        var executable = Environment.GetEnvironmentVariable(ExecutableEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(executable)) return null;

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
        var app = Application.Attach(windowProcess.Id);
        var automation = new UIA3Automation();
        var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(20));
        Assert.NotNull(window);

        return new LaunchedApp(windowProcess, app, automation, window);
    }

    /// <summary>Owns one launched app instance for the duration of a test.</summary>
    internal sealed class LaunchedApp(
        System.Diagnostics.Process process,
        Application app,
        UIA3Automation automation,
        Window window) : IDisposable
    {
        public Window Window { get; } = window;

        public void Dispose()
        {
            // The product's normal window Close action can minimize to the tray by user
            // preference. This process is owned by the smoke test, so terminate it explicitly.
            try { app.Kill(); } catch { /* already gone */ }
            automation.Dispose();
            app.Dispose();
            process.Dispose();
        }
    }
}
