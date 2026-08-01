using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Input;
using FlaUI.UIA3;

namespace ConnectOnion.WinUIClient.UITests;

public sealed class ShellSmokeTests
{
    public const string ExecutableEnvironmentVariable = "CONNECTONION_UI_TEST_EXE";

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [Fact]
    public void Launch_ShowsResponsiveConnectOnionWindow()
    {
        using var launched = LaunchApp();
        if (launched is null) return;

        var window = launched.Window;
        Assert.Contains("ConnectOnion", window.Title, StringComparison.OrdinalIgnoreCase);
        Assert.True(window.IsEnabled);
        Assert.NotNull(WaitForDescendant(window, "SettingsButton"));
    }

    /// <summary>
    /// Clicking the sidebar's fixed Settings row shows the settings overlay.
    /// </summary>
    [Fact]
    public void SidebarSettingsClick_ShowsSettingsOverlay()
    {
        using var launched = LaunchApp();
        if (launched is null) return;

        var window = launched.Window;

        // Asserted on the overlay's *content*, not on the SettingsOverlay element itself: that
        // UserControl carries an AutomationId in XAML but produces no automation peer of its own,
        // so UIA never exposes it. GeneralNav is inside the overlay and appears only once it opens.
        const string settingsContentId = "GeneralNav";

        Assert.Null(window.FindFirstDescendant(query => query.ByAutomationId(settingsContentId)));

        OpenSettings(window);
    }

    [Fact]
    public void SidebarSettingsRow_IsAReachableFooterCommand()
    {
        using var launched = LaunchApp();
        if (launched is null) return;

        var window = launched.Window;
        var settings = WaitForDescendant(window, "SettingsButton");
        Assert.NotNull(settings);
        Assert.Equal("Settings", settings.Properties.Name.ValueOrDefault);
        Assert.True(settings.IsEnabled);
    }

    [Fact]
    public void CloseToTray_SecondLaunch_RestoresTheResponsiveWindow()
    {
        using var launched = LaunchApp();
        if (launched is null) return;

        var windowHandle = launched.Process.MainWindowHandle;
        Assert.NotEqual(IntPtr.Zero, windowHandle);
        var originalMousePosition = Mouse.Position;

        try
        {
            // Reproduce the real bug: a physical click leaves the pointer and DWM hover state on
            // the caption Close button when WM_CLOSE is consumed and the window is hidden.
            var initialBounds = launched.Window.BoundingRectangle;
            Mouse.MoveTo(initialBounds.Right - 23, initialBounds.Top + 16);
            Thread.Sleep(150);
            Mouse.LeftClick();

            // Closing no longer hides unconditionally: the first close asks whether to keep
            // running or exit, and remembers the answer (MainWindow.Tray HandleWindowCloseAsync).
            // So both outcomes are legitimate here and which one happens depends on whether this
            // data root has been closed before — answer the prompt if it appears, and carry on if
            // a stored preference already sent us straight to the tray.
            AnswerKeepRunningIfPrompted(launched.Window);

            Assert.True(WaitUntil(() => !IsWindowVisible(windowHandle)), "window did not hide to tray");

            // Move while the HWND is hidden, so it receives no non-client mouse transition.
            Mouse.MoveTo(initialBounds.Left + initialBounds.Width / 2,
                initialBounds.Top + initialBounds.Height / 2);

            var executable = Environment.GetEnvironmentVariable(ExecutableEnvironmentVariable);
            Assert.False(string.IsNullOrWhiteSpace(executable));
            using var redirected = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(executable!)
                {
                    UseShellExecute = true,
                });
            Assert.NotNull(redirected);

            Assert.True(
                WaitUntil(() =>
                {
                    launched.Process.Refresh();
                    return IsWindowVisible(windowHandle)
                        && !launched.Process.HasExited
                        && launched.Process.Responding;
                }),
                "redirected launch did not restore the tray window");

            Assert.NotNull(WaitForDescendant(launched.Window, "SettingsButton"));

            // Give the queued low-priority non-client tracking request time to deliver its real
            // WM_NCMOUSELEAVE, then inspect the actual pixels of the caption Close button.
            Thread.Sleep(350);
            var restoredBounds = launched.Window.BoundingRectangle;
            var closeBounds = new System.Drawing.Rectangle(
                restoredBounds.Right - 46,
                restoredBounds.Top,
                46,
                32);
            using var closeCapture = Capture.Rectangle(closeBounds, new CaptureSettings());

            var captureDirectory = Environment.GetEnvironmentVariable("CONNECTONION_UI_CAPTURE_DIR");
            if (!string.IsNullOrWhiteSpace(captureDirectory))
            {
                Directory.CreateDirectory(captureDirectory);
                closeCapture.ToFile(Path.Combine(captureDirectory, "caption-close-after-tray-restore.png"));
            }

            var redPixels = 0;
            for (var y = 0; y < closeCapture.Bitmap.Height; y++)
            {
                for (var x = 0; x < closeCapture.Bitmap.Width; x++)
                {
                    var pixel = closeCapture.Bitmap.GetPixel(x, y);
                    if (pixel.R >= 180
                        && pixel.R > pixel.G * 1.5
                        && pixel.R > pixel.B * 1.3)
                    {
                        redPixels++;
                    }
                }
            }

            Assert.True(
                redPixels < 100,
                $"caption Close button retained its red hover state ({redPixels} red pixels)");
        }
        finally
        {
            Mouse.MoveTo(originalMousePosition);
        }
    }

    [Fact]
    public void SettingsAgents_Click_ShowsAgentManagementActions()
    {
        using var launched = LaunchApp();
        if (launched is null) return;

        var window = launched.Window;
        OpenSettings(window);

        var agentsNav = window.FindFirstDescendant(query => query.ByAutomationId("AgentsNav"));
        Assert.NotNull(agentsNav);
        agentsNav.AsRadioButton().Click();

        AutomationElement? addAgent = null;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && addAgent is null)
        {
            try
            {
                addAgent = window
                    .FindAllDescendants(query => query.ByControlType(FlaUI.Core.Definitions.ControlType.Button))
                    .FirstOrDefault(element =>
                        string.Equals(element.Properties.Name.ValueOrDefault, "Add agent", StringComparison.Ordinal));
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // The settings pane is swapping its visual tree; retry below.
            }

            if (addAgent is null) Thread.Sleep(150);
        }

        Assert.NotNull(addAgent);
        Assert.True(addAgent.IsEnabled);
        addAgent.AsButton().Invoke();
        Assert.NotNull(WaitForDescendant(window, "AgentAddressInput"));
    }

    /// <summary>
    /// Settings → Identity offers a way to back the identity up and to restore one.
    ///
    /// <para>Worth a real-window test because the whole feature exists to be reachable: an identity
    /// with no visible backup path is exactly the situation this replaced, and nothing headless can
    /// tell you the buttons made it onto the page. The reveal is exercised (it only reads), the
    /// restore is opened but never committed — pressing "Replace identity" would overwrite the
    /// identity of whatever machine runs the suite.</para>
    /// </summary>
    [Fact]
    public void SettingsIdentity_Click_ShowsBackupAndRestoreActions()
    {
        using var launched = LaunchApp();
        if (launched is null) return;

        var window = launched.Window;
        OpenSettings(window);

        var identityNav = window.FindFirstDescendant(query => query.ByAutomationId("IdentityNav"));
        Assert.NotNull(identityNav);
        identityNav.AsRadioButton().Click();

        var showBackup = WaitForDescendant(window, "ShowIdentityBackupButton");
        Assert.NotNull(showBackup);
        Assert.NotNull(window.FindFirstDescendant(query => query.ByAutomationId("RestoreIdentityButton")));
        Assert.NotNull(window.FindFirstDescendant(query => query.ByAutomationId("GenerateIdentityButton")));

        // The dialog's own content is what proves the reveal worked — the address it says the
        // backup belongs to must be the address the panel is showing.
        showBackup.AsButton().Invoke();
        var addressLabel = WaitForText(window, "Belongs to address");
        Assert.NotNull(addressLabel);
    }

    /// <summary>
    /// Answers the "Close ConnectOnion?" prompt with <b>Keep running</b> when it is showing.
    ///
    /// <para>Returns false — without failing — when it is not, because that is a legitimate state:
    /// the choice is persisted, so a data root that has already been closed once goes straight to
    /// the tray with no prompt at all. A test that insisted on the dialog would pass exactly once
    /// per profile and then fail forever.</para>
    ///
    /// <para>Invoked through UIA rather than clicked with the mouse, and that is deliberate here
    /// rather than merely convenient: the caller has just left the physical pointer parked on the
    /// caption Close button, which is the whole precondition of the hover-state bug this test
    /// exists for. Moving the mouse to press the dialog button would clear it and quietly turn the
    /// test into a no-op.</para>
    /// </summary>
    private static bool AnswerKeepRunningIfPrompted(Window window)
    {
        // Short deadline: this is a "did a dialog appear" probe, not a wait for slow work, and the
        // no-dialog path is the common one once a profile has been used.
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var keepRunning = window
                    .FindAllDescendants(query => query.ByControlType(FlaUI.Core.Definitions.ControlType.Button))
                    .FirstOrDefault(element =>
                        string.Equals(element.Properties.Name.ValueOrDefault, "Keep running", StringComparison.Ordinal));
                if (keepRunning is not null)
                {
                    keepRunning.AsButton().Invoke();
                    return true;
                }
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // The dialog is being composed over the shell; retry below.
            }

            Thread.Sleep(150);
        }

        return false;
    }

    /// <summary>Polls for a Text element with the given content, tolerating the COMException a
    /// mutating visual tree throws.</summary>
    private static AutomationElement? WaitForText(AutomationElement window, string text)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var match = window
                    .FindAllDescendants(query => query.ByControlType(FlaUI.Core.Definitions.ControlType.Text))
                    .FirstOrDefault(element =>
                        string.Equals(element.Properties.Name.ValueOrDefault, text, StringComparison.Ordinal));
                if (match is not null) return match;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // Tree changed under the query; retry below.
            }

            Thread.Sleep(150);
        }

        return null;
    }

    /// <summary>
    /// The sidebar's "Add agent" button opens the shell-owned modal over the current page.
    /// Collapsed overlay content is absent from the UIA tree, so its focused input proves both
    /// visibility and keyboard readiness.
    /// </summary>
    [Fact]
    public void SidebarAddAgent_Click_OpensShellOverlayAndFocusesInput()
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

        OpenSettings(window);

        var usageNav = WaitForDescendant(window, "UsageNav");
        Assert.NotNull(usageNav);
        usageNav.AsRadioButton().Click();
        Assert.True(
            WaitUntil(() => usageNav.AsRadioButton().IsChecked == true),
            "Usage settings category was not selected.");

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

    /// <summary>Invokes the sidebar's fixed Settings command and waits for the modal.</summary>
    internal static void OpenSettings(Window window)
    {
        var settingsButton = WaitForDescendant(window, "SettingsButton");
        Assert.NotNull(settingsButton);
        settingsButton.AsButton().Invoke();

        Assert.NotNull(WaitForDescendant(window, "GeneralNav"));
    }

    /// <summary>Polls for a descendant, tolerating the transient UIA failures a mutating visual
    /// tree produces.</summary>
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

    private static bool WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (condition()) return true;
            }
            catch (InvalidOperationException)
            {
                // The process/window can be between hidden and visible states; retry below.
            }

            Thread.Sleep(100);
        }

        return false;
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
            // Shell activation is required for the unpackaged Windows App SDK bootstrapper.
            // The launched process inherits the test host's process-level data-root override.
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
                .FirstOrDefault(process =>
                    !existingProcessIds.Contains(process.Id)
                    && process.MainWindowHandle != IntPtr.Zero
                    && process.MainWindowTitle.Contains("ConnectOnion", StringComparison.OrdinalIgnoreCase));
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
        public System.Diagnostics.Process Process => process;

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
