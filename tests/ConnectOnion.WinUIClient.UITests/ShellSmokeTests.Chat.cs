using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using ConnectOnion.WinUIClient.Data;
using Microsoft.Data.Sqlite;

namespace ConnectOnion.WinUIClient.UITests;

public sealed partial class ShellSmokeTests
{
    private const string ChatAgentId = "ui-chat-agent";
    private const string ChatSessionId = "ui-chat-session";
    private const string ChatTitle = "Automation Chat Flow";
    private static readonly string ChatAddress = "0x" + new string('a', 64);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MoveWindow(
        IntPtr window,
        int x,
        int y,
        int width,
        int height,
        bool repaint);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(System.Drawing.Point point);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [Fact]
    public async Task Chat_SendMessage_RendersUserAndAgentBubbles()
    {
        var holdSocket = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedInput = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new UiFakeAgentServer(async (_, socket, ct) =>
        {
            await UiFakeAgentServer.ReceiveFrameOfTypeAsync(socket, "CONNECT", ct);
            await UiFakeAgentServer.SendTextAsync(socket,
                "{\"type\":\"CONNECTED\",\"session_id\":\"ui-remote\",\"status\":\"connected\"}", ct);
            var input = await UiFakeAgentServer.ReceiveFrameOfTypeAsync(socket, "INPUT", ct);
            receivedInput.TrySetResult(input);
            await UiFakeAgentServer.SendTextAsync(socket,
                "{\"type\":\"OUTPUT\",\"result\":\"Automation reply received\",\"duration_ms\":12}", ct);
            await holdSocket.Task.WaitAsync(ct);
        });

        if (!PrepareChatProfile(server.BaseUri)) return;
        try
        {
            using var launched = LaunchApp(handleFirstRunDialog: false);
            if (launched is null) return;
            var window = launched.Window;
            var input = OpenChat(window);

            input.Text = "send this automation prompt";
            var send = WaitForDescendant(window, "SendMessageButton");
            Assert.NotNull(send);
            Assert.True(WaitUntil(() => send.IsEnabled));
            send.AsButton().Invoke();

            var inputJson = await receivedInput.Task.WaitAsync(TimeSpan.FromSeconds(20));
            using (var document = JsonDocument.Parse(inputJson))
                Assert.Equal("send this automation prompt", document.RootElement.GetProperty("prompt").GetString());

            Assert.NotNull(WaitForAccessibleName(window, "send this automation prompt", TimeSpan.FromSeconds(15)));
            Assert.NotNull(WaitForAccessibleName(window, "Automation reply received", TimeSpan.FromSeconds(20)));
            server.ThrowIfFaulted();
        }
        finally
        {
            holdSocket.TrySetResult();
            RemoveChatFixture();
        }
    }

    [Fact]
    public async Task Chat_DroppedConnection_ReconnectsAndCompletesTheTurn()
    {
        var resumed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOutput = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holdSocket = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new UiFakeAgentServer(async (connection, socket, ct) =>
        {
            var connect = await UiFakeAgentServer.ReceiveFrameOfTypeAsync(socket, "CONNECT", ct);
            if (connection == 1)
            {
                await UiFakeAgentServer.SendTextAsync(socket,
                    "{\"type\":\"CONNECTED\",\"session_id\":\"ui-remote\",\"status\":\"connected\"}", ct);
                await UiFakeAgentServer.ReceiveFrameOfTypeAsync(socket, "INPUT", ct);
                // A WebSocket close frame is deterministic on loopback and exercises the same
                // client connection-loss path as a severed transport. Socket.Abort can leave the
                // Kestrel-side TCP teardown pending long enough to turn this into a watchdog test.
                await socket.CloseOutputAsync(
                    WebSocketCloseStatus.InternalServerError,
                    "automation drop",
                    ct);
                return;
            }

            await UiFakeAgentServer.SendTextAsync(socket,
                "{\"type\":\"CONNECTED\",\"session_id\":\"ui-remote\",\"status\":\"running\"}", ct);
            resumed.TrySetResult(connect);
            await releaseOutput.Task.WaitAsync(ct);
            await UiFakeAgentServer.SendTextAsync(socket,
                "{\"type\":\"OUTPUT\",\"result\":\"Reply survived reconnect\",\"duration_ms\":21}", ct);
            await holdSocket.Task.WaitAsync(ct);
        });

        if (!PrepareChatProfile(server.BaseUri)) return;
        try
        {
            using var launched = LaunchApp(handleFirstRunDialog: false);
            if (launched is null) return;
            var window = launched.Window;
            var input = OpenChat(window);

            input.Text = "drop and resume";
            var send = WaitForDescendant(window, "SendMessageButton");
            Assert.NotNull(send);
            Assert.True(WaitUntil(() => send.IsEnabled));
            send.AsButton().Invoke();

            var reconnectJson = await resumed.Task.WaitAsync(TimeSpan.FromSeconds(25));
            using (var document = JsonDocument.Parse(reconnectJson))
            {
                var session = document.RootElement.GetProperty("session");
                Assert.Equal("ui-remote", session.GetProperty("session_id").GetString());
            }

            Assert.NotNull(WaitForAccessibleName(window, "Reconnected - resuming", TimeSpan.FromSeconds(15)));
            releaseOutput.TrySetResult();
            Assert.NotNull(WaitForAccessibleName(window, "Reply survived reconnect", TimeSpan.FromSeconds(20)));
            Assert.True(server.ConnectionCount >= 2);
            server.ThrowIfFaulted();
        }
        finally
        {
            releaseOutput.TrySetResult();
            holdSocket.TrySetResult();
            RemoveChatFixture();
        }
    }

    [Fact(Skip = "Explorer drag coordinates vary with desktop DPI and layout; keep this scenario opt-in until the source is deterministic.")]
    public async Task Chat_DragAndDropFile_AddsPendingAttachment()
    {
        await using var server = new UiFakeAgentServer();
        if (!PrepareChatProfile(server.BaseUri)) return;

        var fileName = $"ui-drop-{Guid.NewGuid():N}.txt";
        var filePath = Path.Combine(Path.GetTempPath(), fileName);
        File.WriteAllText(filePath, "real-window drag-and-drop fixture");
        try
        {
            using var launched = LaunchApp(handleFirstRunDialog: false);
            if (launched is null) return;
            var window = launched.Window;
            var appHandle = new IntPtr(window.Properties.NativeWindowHandle.ValueOrDefault);
            Assert.True(
                MoveWindow(appHandle, 900, 0, 1000, 900, repaint: true),
                "could not position the app beside the Explorer drag source");
            Thread.Sleep(400);
            var input = OpenChat(window);
            Assert.True(
                WaitUntil(() => input.IsEnabled),
                "the composer did not become available after the fake agent health check");
            // Explorer provides the native StorageItems payload that WinUI expects. Aim at the
            // centre of the enabled text box, whose routed drag events bubble to ComposerSurface.
            var inputBounds = input.BoundingRectangle;
            DropFileFromExplorer(filePath, new System.Drawing.Point(
                inputBounds.Left + (inputBounds.Width / 2),
                inputBounds.Top + (inputBounds.Height / 2)),
                launched.Process.Id);

            Assert.NotNull(WaitForDescendant(window, "PendingAttachmentsList", TimeSpan.FromSeconds(15)));
            Assert.NotNull(WaitForText(window, fileName, TimeSpan.FromSeconds(15)));
            server.ThrowIfFaulted();
        }
        finally
        {
            try { File.Delete(filePath); } catch (IOException) { }
            RemoveChatFixture();
        }
    }

    [Fact]
    public async Task Chat_Find_NavigatesMatchesAndCloses()
    {
        await using var server = new UiFakeAgentServer();
        if (!PrepareChatProfile(server.BaseUri,
            (1, "user", "needle alpha needle"),
            (2, "agent", "needle omega"))) return;

        try
        {
            using var launched = LaunchApp(handleFirstRunDialog: false);
            if (launched is null) return;
            var window = launched.Window;
            OpenChat(window);

            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
            var find = WaitForDescendant(window, "FindTextBox");
            Assert.NotNull(find);
            Assert.True(WaitUntil(() => find.Properties.HasKeyboardFocus.ValueOrDefault));
            find.AsTextBox().Text = "needle";

            Assert.True(WaitUntil(() =>
                WaitForDescendant(window, "FindCounterText", TimeSpan.FromMilliseconds(250))?
                    .Properties.Name.ValueOrDefault == "1 / 3 results"));

            var next = WaitForDescendant(window, "NextFindButton");
            Assert.NotNull(next);
            next.AsButton().Invoke();
            Assert.True(WaitUntil(() =>
                WaitForDescendant(window, "FindCounterText", TimeSpan.FromMilliseconds(250))?
                    .Properties.Name.ValueOrDefault == "2 / 3 results"));

            var close = WaitForDescendant(window, "CloseFindButton");
            Assert.NotNull(close);
            close.AsButton().Invoke();
            Assert.True(WaitUntil(() =>
                window.FindFirstDescendant(query => query.ByAutomationId("FindTextBox")) is null));
        }
        finally
        {
            RemoveChatFixture();
        }
    }

    private static bool PrepareChatProfile(
        Uri directUri,
        params (long Id, string Role, string Content)[] messages)
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(ExecutableEnvironmentVariable))) return false;

        // Initialize the isolated profile directly through the same production persistence APIs
        // the app uses. Shell activation is intentionally reserved for the UI action under test;
        // depending on a throwaway window to create the fixture makes setup sensitive to whether
        // Windows App SDK activation preserved the parent process's environment block.
        using (AppDatabase.OpenAsync().GetAwaiter().GetResult()) { }
        IdentityStore.EnsureIdentity();

        var dataRoot = ResolveDataRoot();

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataRoot, "connectonion.db"),
            Mode = SqliteOpenMode.ReadWrite,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();

        using (var cleanup = connection.CreateCommand())
        {
            cleanup.Transaction = transaction;
            cleanup.CommandText = """
                DELETE FROM message_attachments
                WHERE conversation_id IN (SELECT id FROM sessions WHERE agent_id = $agent);
                DELETE FROM messages
                WHERE conversation_id IN (SELECT id FROM sessions WHERE agent_id = $agent);
                DELETE FROM trace_events
                WHERE conversation_id IN (SELECT id FROM sessions WHERE agent_id = $agent);
                DELETE FROM executions
                WHERE conversation_id IN (SELECT id FROM sessions WHERE agent_id = $agent);
                DELETE FROM sessions WHERE agent_id = $agent;
                DELETE FROM agents WHERE id = $agent;
                """;
            cleanup.Parameters.AddWithValue("$agent", ChatAgentId);
            cleanup.ExecuteNonQuery();
        }

        using (var seed = connection.CreateCommand())
        {
            seed.Transaction = transaction;
            seed.CommandText = """
                INSERT INTO agents
                    (id, name, address, direct_url, info_json, info_updated_at, sort_order)
                VALUES
                    ($agent, 'UI Automation Agent', $address, $direct_url,
                     '{"accepted_inputs":{"text":true,"images":true,"files":{"max_file_size_mb":10,"max_files_per_request":5}}}',
                     '2026-08-05T00:00:00.0000000Z', 0);

                INSERT INTO sessions
                    (id, agent_id, title, remote_session_id, last_processed_event_id,
                     created_at, updated_at, sort_order, mode, has_custom_title)
                VALUES
                    ($session, $agent, $title, NULL, NULL,
                     '2026-08-05T00:00:00.0000000Z', '2026-08-05T00:00:00.0000000Z',
                     0, 'safe', 1);
                """;
            seed.Parameters.AddWithValue("$agent", ChatAgentId);
            seed.Parameters.AddWithValue("$address", ChatAddress);
            seed.Parameters.AddWithValue("$direct_url", directUri.ToString().TrimEnd('/'));
            seed.Parameters.AddWithValue("$session", ChatSessionId);
            seed.Parameters.AddWithValue("$title", ChatTitle);
            seed.ExecuteNonQuery();
        }

        foreach (var message in messages)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO messages (id, conversation_id, role, content, created_at)
                VALUES ($id, $session, $role, $content, $created_at);
                """;
            insert.Parameters.AddWithValue("$id", message.Id);
            insert.Parameters.AddWithValue("$session", ChatSessionId);
            insert.Parameters.AddWithValue("$role", message.Role);
            insert.Parameters.AddWithValue("$content", message.Content);
            insert.Parameters.AddWithValue("$created_at", 1785888000000L + message.Id);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
        return true;
    }

    private static FlaUI.Core.AutomationElements.TextBox OpenChat(Window window)
    {
        var session = FindChatSession(window, TimeSpan.FromSeconds(8));
        if (session is null)
        {
            var agent = WaitForDescendant(window, "AgentButton", TimeSpan.FromSeconds(15));
            Assert.True(agent is not null,
                "No agent row reached UI Automation. Visible tree:\n" +
                string.Join("\n", window.FindAllDescendants()
                    .Where(element => !string.IsNullOrWhiteSpace(element.Properties.AutomationId.ValueOrDefault))
                    .Take(80)
                    .Select(element =>
                        $"{element.Properties.AutomationId.ValueOrDefault}: {element.Properties.Name.ValueOrDefault}")));
            agent!.AsButton().Invoke();
            session = FindChatSession(window, TimeSpan.FromSeconds(15));
        }

        Assert.NotNull(session);
        session.AsButton().Invoke();
        var input = WaitForDescendant(window, "MessageInput", TimeSpan.FromSeconds(20));
        Assert.NotNull(input);
        return input.AsTextBox();
    }

    private static AutomationElement? FindChatSession(Window window, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var match = window
                    .FindAllDescendants(query => query.ByAutomationId("SessionButton"))
                    .FirstOrDefault(element =>
                        (element.Properties.Name.ValueOrDefault ?? "")
                            .Contains(ChatTitle, StringComparison.OrdinalIgnoreCase));
                if (match is not null) return match;
            }
            catch (COMException) { }
            Thread.Sleep(150);
        }
        return null;
    }

    private static AutomationElement? WaitForAccessibleName(
        AutomationElement root,
        string expected,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var match = root.FindAllDescendants().FirstOrDefault(element =>
                    (element.Properties.Name.ValueOrDefault ?? "")
                        .Contains(expected, StringComparison.Ordinal));
                if (match is not null) return match;
            }
            catch (COMException)
            {
                // A message container was recycled while UIA walked the tree; retry below.
            }

            Thread.Sleep(150);
        }

        return null;
    }

    private static void RemoveChatFixture()
    {
        var database = Path.Combine(ResolveDataRoot(), "connectonion.db");
        if (!File.Exists(database)) return;

        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = database,
                Mode = SqliteOpenMode.ReadWrite,
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM message_attachments
                WHERE conversation_id IN (SELECT id FROM sessions WHERE agent_id = $agent);
                DELETE FROM messages
                WHERE conversation_id IN (SELECT id FROM sessions WHERE agent_id = $agent);
                DELETE FROM trace_events
                WHERE conversation_id IN (SELECT id FROM sessions WHERE agent_id = $agent);
                DELETE FROM executions
                WHERE conversation_id IN (SELECT id FROM sessions WHERE agent_id = $agent);
                DELETE FROM sessions WHERE agent_id = $agent;
                DELETE FROM agents WHERE id = $agent;
                """;
            command.Parameters.AddWithValue("$agent", ChatAgentId);
            command.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // A failed UI assertion may leave the app winding down for a moment. The isolated CI
            // profile is discarded after the run, so cleanup must not hide the original failure.
        }
    }

    private static void DropFileFromExplorer(
        string filePath,
        System.Drawing.Point targetPoint,
        int expectedTargetProcessId)
    {
        using var automation = new UIA3Automation();
        var desktop = automation.GetDesktop();
        var existingWindows = desktop
            .FindAllChildren(query => query.ByControlType(ControlType.Window))
            .Select(element => element.Properties.NativeWindowHandle.ValueOrDefault)
            .ToHashSet();

        var fileName = Path.GetFileName(filePath);
        using var explorerLauncher = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/n,/select,\"{filePath}\"",
            UseShellExecute = true,
        });
        Assert.NotNull(explorerLauncher);

        Window? explorerWindow = null;
        AutomationElement? fileItem = null;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        try
        {
            while (DateTime.UtcNow < deadline && fileItem is null)
            {
                try
                {
                    foreach (var candidate in desktop
                                 .FindAllChildren(query => query.ByControlType(ControlType.Window))
                                 .Where(element => !existingWindows.Contains(
                                     element.Properties.NativeWindowHandle.ValueOrDefault)))
                    {
                        var match = candidate.FindAllDescendants().FirstOrDefault(element =>
                            IsExplorerFileItem(element, fileName));
                        if (match is null) continue;
                        explorerWindow = candidate.AsWindow();
                        fileItem = match;
                        break;
                    }
                }
                catch (COMException)
                {
                    // Explorer is replacing its folder view; retry once the new tree settles.
                }

                if (fileItem is null) Thread.Sleep(150);
            }

            Assert.NotNull(explorerWindow);
            Assert.NotNull(fileItem);
            var explorerHandle = new IntPtr(
                explorerWindow.Properties.NativeWindowHandle.ValueOrDefault);
            ShowWindow(explorerHandle, 9); // SW_RESTORE
            Assert.True(
                MoveWindow(explorerHandle, 0, 0, 850, 700, repaint: true),
                "could not move the Explorer drag source away from the composer");
            Thread.Sleep(500);

            // Moving from a maximized window replaces Explorer's folder-view subtree.
            fileItem = explorerWindow.FindAllDescendants().FirstOrDefault(element =>
                IsExplorerFileItem(element, fileName));
            Assert.NotNull(fileItem);
            Assert.NotEqual(
                0u,
                GetWindowThreadProcessId(WindowFromPoint(targetPoint), out var targetProcessId));
            Assert.Equal((uint)expectedTargetProcessId, targetProcessId);
            explorerWindow.Focus();
            fileItem.Focus();

            var sourcePoint = fileItem.GetClickablePoint();
            Mouse.MoveTo(sourcePoint);
            Thread.Sleep(500);
            Mouse.Click(FlaUI.Core.Input.MouseButton.Left);
            Thread.Sleep(250);
            Mouse.Drag(sourcePoint, targetPoint, FlaUI.Core.Input.MouseButton.Left);
        }
        finally
        {
            try { Mouse.Up(FlaUI.Core.Input.MouseButton.Left); } catch { }
            try { explorerWindow?.Close(); } catch (COMException) { }
        }
    }

    private static bool IsExplorerFileItem(AutomationElement element, string fileName)
    {
        if (!string.Equals(
                element.Properties.Name.ValueOrDefault,
                fileName,
                StringComparison.OrdinalIgnoreCase)) return false;

        var controlType = element.Properties.ControlType.ValueOrDefault;
        return controlType == ControlType.ListItem || controlType == ControlType.DataItem;
    }
}
