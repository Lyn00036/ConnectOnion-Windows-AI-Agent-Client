using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ConnectOnion.WinUIClient.Data;

/// <summary>
/// Row-level message persistence. Each chat bubble is a row in the
/// <c>messages</c> table, keyed by <c>(conversation_id, id)</c>.
/// Replaces the previous JSON blob approach so messages can be queried,
/// paged, and written incrementally.
/// </summary>
public sealed class ConversationRepository
{
    private static readonly Action<ILogger, string, string, Exception?> LogDatabaseFailure =
        LoggerMessage.Define<string, string>(LogLevel.Error, new EventId(1, "ConversationDatabaseFailure"),
            "Database operation {Operation} failed for conversation {ConversationId}");
    private readonly ILogger<ConversationRepository> _logger;

    public ConversationRepository(ILogger<ConversationRepository>? logger = null)
        => _logger = logger ?? NullLogger<ConversationRepository>.Instance;

    public async Task<IReadOnlyList<ChatMessage>> LoadMessagesAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>();
        try
        {
            await using var connection = await AppDatabase.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, role, content, agent_name,
                       event_kind, event_key, event_eyebrow, event_title,
                       event_detail, event_meta, event_args, event_result,
                       event_status, is_onboarding, created_at
                FROM messages
                WHERE conversation_id = $conversation_id
                ORDER BY id;
                """;
            AppDatabase.Add(command, "$conversation_id", conversationId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                messages.Add(RowToMessage(reader));
            }

            await LoadAttachmentsIntoAsync(connection, conversationId, messages, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Missing / corrupt table → start fresh.
            LogFailure(nameof(LoadMessagesAsync), conversationId, ex);
        }
        return messages;
    }

    private static async Task LoadAttachmentsIntoAsync(
        SqliteConnection connection, string conversationId, IReadOnlyList<ChatMessage> messages,
        long? onlyMessageId = null, CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0) return;

        var byId = new Dictionary<long, ChatMessage>();
        foreach (var m in messages) byId[m.Id] = m;

        await using var command = connection.CreateCommand();
        command.CommandText = onlyMessageId is null
            ? """
                SELECT message_id, id, kind, file_name, mime_type, size_bytes,
                       local_cache_path, remote_uri, status
                FROM message_attachments
                WHERE conversation_id = $conversation_id
                ORDER BY message_id, rowid;
                """
            : """
                SELECT message_id, id, kind, file_name, mime_type, size_bytes,
                       local_cache_path, remote_uri, status
                FROM message_attachments
                WHERE conversation_id = $conversation_id AND message_id = $message_id
                ORDER BY rowid;
                """;
        AppDatabase.Add(command, "$conversation_id", conversationId);
        if (onlyMessageId is { } scopedMessageId) AppDatabase.Add(command, "$message_id", scopedMessageId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var messageId = reader.GetInt64(0);
            if (!byId.TryGetValue(messageId, out var message)) continue;

            message.Attachments.Add(new ChatAttachment
            {
                Id = reader.GetString(1),
                Kind = reader.GetString(2) == "image" ? AttachmentKind.Image : AttachmentKind.File,
                FileName = reader.GetString(3),
                MimeType = ReadNullableString(reader, 4),
                SizeBytes = reader.GetInt64(5),
                LocalCachePath = ReadNullableString(reader, 6),
                RemoteUri = ReadNullableString(reader, 7),
                Status = ReadNullableString(reader, 8) == "failed" ? AttachmentStatus.Failed : AttachmentStatus.Sent,
            });
        }
    }

    /// <summary>
    /// The id to give the next bubble appended to this conversation, without reading the
    /// conversation itself. Callers that only need to append (rather than reconcile the whole
    /// turn) have no reason to pull every row across just to take a maximum.
    /// </summary>
    public async Task<long> GetNextMessageIdAsync(string conversationId)
    {
        try
        {
            await using var connection = await AppDatabase.OpenAsync().ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COALESCE(MAX(id), 0) + 1 FROM messages WHERE conversation_id = $conversation_id;";
            AppDatabase.Add(command, "$conversation_id", conversationId);
            var value = await command.ExecuteScalarAsync().ConfigureAwait(false);
            return value is long next && next > 0 ? next : 1;
        }
        catch (Exception ex)
        {
            // Falling back to 1 on a conversation that already has rows would collide with the
            // primary key and lose the append — worth finding in the log rather than guessing at.
            LogFailure(nameof(GetNextMessageIdAsync), conversationId, ex);
            return 1;
        }
    }

    /// <summary>
    /// The conversation's most recent agent bubble (with its attachments), or null if it has
    /// none yet. This is the only pre-existing row a turn's projection can still touch — an
    /// <c>agent_image</c> event hangs its attachment on the last agent bubble, and the final
    /// reply is deduped against it — so loading just this row is what lets a turn be persisted
    /// without reading the whole conversation back first.
    /// </summary>
    public async Task<ChatMessage?> LoadLastAgentMessageAsync(string conversationId)
    {
        try
        {
            await using var connection = await AppDatabase.OpenAsync().ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, role, content, agent_name,
                       event_kind, event_key, event_eyebrow, event_title,
                       event_detail, event_meta, event_args, event_result,
                       event_status, is_onboarding, created_at
                FROM messages
                WHERE conversation_id = $conversation_id AND role = 'agent'
                ORDER BY id DESC
                LIMIT 1;
                """;
            AppDatabase.Add(command, "$conversation_id", conversationId);

            ChatMessage? message = null;
            await using (var reader = await command.ExecuteReaderAsync().ConfigureAwait(false))
            {
                if (await reader.ReadAsync().ConfigureAwait(false)) message = RowToMessage(reader);
            }
            if (message is null) return null;

            await LoadAttachmentsIntoAsync(connection, conversationId, new[] { message }, message.Id)
                .ConfigureAwait(false);
            return message;
        }
        catch (Exception ex)
        {
            LogFailure(nameof(LoadLastAgentMessageAsync), conversationId, ex);
            return null;
        }
    }

    /// <summary>
    /// Writes <paramref name="messages"/> — and only those rows — into the conversation, inserting
    /// the ones that are new and updating the ones already there. Nothing else in the conversation
    /// is read, deleted, or rewritten, so the cost of persisting a turn scales with the size of the
    /// turn rather than the length of the conversation.
    ///
    /// Each written row's attachments are replaced wholesale (a single indexed delete plus its own
    /// inserts): an attachment's id is content-derived rather than stable, and an existing agent
    /// bubble can gain an image mid-turn, so diffing them per attachment would cost more than
    /// rewriting the handful a bubble has. <c>created_at</c> is deliberately left alone on update —
    /// a row keeps the timestamp it was first written with.
    /// </summary>
    public async Task UpsertMessagesAsync(string conversationId, IReadOnlyList<ChatMessage> messages)
    {
        if (messages.Count == 0) return;

        try
        {
            await using var connection = await AppDatabase.OpenAsync().ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await using var upsertMessage = CreateMessageUpsert(connection, transaction);
            await using var clearAttachments = CreateAttachmentDelete(connection, transaction);
            await using var insertAttachment = CreateAttachmentInsert(connection, transaction);

            foreach (var message in messages)
            {
                BindMessage(upsertMessage, message, conversationId, now);
                await upsertMessage.ExecuteNonQueryAsync().ConfigureAwait(false);

                clearAttachments.Parameters["$conversation_id"].Value = conversationId;
                clearAttachments.Parameters["$message_id"].Value = message.Id;
                await clearAttachments.ExecuteNonQueryAsync().ConfigureAwait(false);

                foreach (var attachment in message.Attachments)
                {
                    BindAttachment(insertAttachment, attachment, conversationId, message.Id, now);
                    await insertAttachment.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }

            await transaction.CommitAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Best-effort: keep the live conversation working if the write fails.
            LogFailure(nameof(UpsertMessagesAsync), conversationId, ex);
        }
    }

    public async Task DeleteMessagesAsync(string conversationId)
    {
        try
        {
            await using var connection = await AppDatabase.OpenAsync().ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM message_attachments WHERE conversation_id = $conversation_id;
                DELETE FROM messages WHERE conversation_id = $conversation_id;
                """;
            AppDatabase.Add(command, "$conversation_id", conversationId);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogFailure(nameof(DeleteMessagesAsync), conversationId, ex);
        }
    }

    // --- helpers ---

    /// <summary>
    /// Every write here is best-effort: a failed save must not take the live conversation down
    /// with it. That is a deliberate trade, but a silent one is not — a message the user can see
    /// on screen and that is quietly missing after a restart should leave a trace to find. The
    /// Debug line is for an attached debugger only; the logger call is what survives in Release.
    /// </summary>
    private void LogFailure(string operation, string conversationId, Exception ex)
    {
        Debug.WriteLine($"[db] {operation} failed for conversation {conversationId}: {ex}");
        LogDatabaseFailure(_logger, operation, conversationId, ex);
    }

    private static ChatMessage RowToMessage(SqliteDataReader reader)
    {
        var m = new ChatMessage
        {
            Id = reader.GetInt64(0),
            Role = ParseRole(reader.GetString(1)),
            Content = ReadNullableString(reader, 2) ?? "",
            AgentName = ReadNullableString(reader, 3),
            EventKind = ReadNullableString(reader, 4) ?? "",
            EventKey = ReadNullableString(reader, 5),
            EventEyebrow = ReadNullableString(reader, 6) ?? "",
            EventTitle = ReadNullableString(reader, 7) ?? "",
            EventDetail = ReadNullableString(reader, 8),
            EventMeta = ReadNullableString(reader, 9),
            EventArgs = ReadNullableString(reader, 10),
            EventResult = ReadNullableString(reader, 11),
            Status = ParseEventStatus(ReadNullableString(reader, 12)),
            IsOnboarding = reader.GetInt32(13) != 0,
            CreatedAtUnixMs = reader.GetInt64(14),
        };
        // Tool activity is stored in the existing event_args payload column. This keeps the
        // schema backward compatible while making the aggregate durable across page teardown
        // and application restart. Legacy per-tool rows still load normally and are upgraded
        // by the chat projection when they are next replayed/saved.
        if (m.EventKind == "tool_activity" && !string.IsNullOrWhiteSpace(m.EventArgs))
        {
            try
            {
                m.ToolActivity = JsonSerializer.Deserialize<ToolActivityViewModel>(m.EventArgs);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[db] tool activity payload for message {m.Id} could not be restored: {ex}");
                m.EventKind = "activity";
                m.EventTitle = "Tool execution history could not be restored";
            }
        }
        return m;
    }

    // The write commands below are built once per transaction and rebound per row. Microsoft.Data
    // .Sqlite caches the compiled statement on the command object, so reusing one command means the
    // statement is parsed once rather than once per bubble — which is what made saving a long
    // conversation scale badly, far more than the row writes themselves.

    private static SqliteCommand CreateMessageUpsert(SqliteConnection connection, SqliteTransaction transaction)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO messages
              (id, conversation_id, role, content, agent_name,
               event_kind, event_key, event_eyebrow, event_title,
               event_detail, event_meta, event_args, event_result,
               event_status, is_onboarding, created_at)
            VALUES
              ($id, $conversation_id, $role, $content, $agent_name,
               $event_kind, $event_key, $event_eyebrow, $event_title,
               $event_detail, $event_meta, $event_args, $event_result,
               $event_status, $is_onboarding, $created_at)
            ON CONFLICT(conversation_id, id) DO UPDATE SET
               role = excluded.role,
               content = excluded.content,
               agent_name = excluded.agent_name,
               event_kind = excluded.event_kind,
               event_key = excluded.event_key,
               event_eyebrow = excluded.event_eyebrow,
               event_title = excluded.event_title,
               event_detail = excluded.event_detail,
               event_meta = excluded.event_meta,
               event_args = excluded.event_args,
               event_result = excluded.event_result,
               event_status = excluded.event_status,
               is_onboarding = excluded.is_onboarding;
            """;
        cmd.Parameters.Add("$id", SqliteType.Integer);
        cmd.Parameters.Add("$conversation_id", SqliteType.Text);
        cmd.Parameters.Add("$role", SqliteType.Text);
        cmd.Parameters.Add("$content", SqliteType.Text);
        cmd.Parameters.Add("$agent_name", SqliteType.Text);
        cmd.Parameters.Add("$event_kind", SqliteType.Text);
        cmd.Parameters.Add("$event_key", SqliteType.Text);
        cmd.Parameters.Add("$event_eyebrow", SqliteType.Text);
        cmd.Parameters.Add("$event_title", SqliteType.Text);
        cmd.Parameters.Add("$event_detail", SqliteType.Text);
        cmd.Parameters.Add("$event_meta", SqliteType.Text);
        cmd.Parameters.Add("$event_args", SqliteType.Text);
        cmd.Parameters.Add("$event_result", SqliteType.Text);
        cmd.Parameters.Add("$event_status", SqliteType.Text);
        cmd.Parameters.Add("$is_onboarding", SqliteType.Integer);
        cmd.Parameters.Add("$created_at", SqliteType.Integer);
        return cmd;
    }

    private static SqliteCommand CreateAttachmentDelete(SqliteConnection connection, SqliteTransaction transaction)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        // (conversation_id, message_id) is a prefix of the table's primary key, so this is a seek.
        cmd.CommandText = """
            DELETE FROM message_attachments
            WHERE conversation_id = $conversation_id AND message_id = $message_id;
            """;
        cmd.Parameters.Add("$conversation_id", SqliteType.Text);
        cmd.Parameters.Add("$message_id", SqliteType.Integer);
        return cmd;
    }

    private static SqliteCommand CreateAttachmentInsert(SqliteConnection connection, SqliteTransaction transaction)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO message_attachments
              (id, conversation_id, message_id, kind, file_name, mime_type,
               size_bytes, local_cache_path, remote_uri, status, created_at)
            VALUES
              ($id, $conversation_id, $message_id, $kind, $file_name, $mime_type,
               $size_bytes, $local_cache_path, $remote_uri, $status, $created_at);
            """;
        cmd.Parameters.Add("$id", SqliteType.Text);
        cmd.Parameters.Add("$conversation_id", SqliteType.Text);
        cmd.Parameters.Add("$message_id", SqliteType.Integer);
        cmd.Parameters.Add("$kind", SqliteType.Text);
        cmd.Parameters.Add("$file_name", SqliteType.Text);
        cmd.Parameters.Add("$mime_type", SqliteType.Text);
        cmd.Parameters.Add("$size_bytes", SqliteType.Integer);
        cmd.Parameters.Add("$local_cache_path", SqliteType.Text);
        cmd.Parameters.Add("$remote_uri", SqliteType.Text);
        cmd.Parameters.Add("$status", SqliteType.Text);
        cmd.Parameters.Add("$created_at", SqliteType.Integer);
        return cmd;
    }

    private static void BindMessage(SqliteCommand cmd, ChatMessage m, string conversationId, long now)
    {
        var eventArgs = m.IsToolActivityEvent
            ? JsonSerializer.Serialize(m.ToolActivity)
            : m.EventArgs;

        cmd.Parameters["$id"].Value = m.Id;
        cmd.Parameters["$conversation_id"].Value = conversationId;
        cmd.Parameters["$role"].Value = m.Role.ToString().ToLowerInvariant();
        cmd.Parameters["$content"].Value = string.IsNullOrEmpty(m.Content) ? DBNull.Value : m.Content;
        cmd.Parameters["$agent_name"].Value = m.AgentName ?? (object)DBNull.Value;
        cmd.Parameters["$event_kind"].Value = string.IsNullOrEmpty(m.EventKind) ? DBNull.Value : m.EventKind;
        cmd.Parameters["$event_key"].Value = m.EventKey ?? (object)DBNull.Value;
        cmd.Parameters["$event_eyebrow"].Value = string.IsNullOrEmpty(m.EventEyebrow) ? DBNull.Value : m.EventEyebrow;
        cmd.Parameters["$event_title"].Value = string.IsNullOrEmpty(m.EventTitle) ? DBNull.Value : m.EventTitle;
        cmd.Parameters["$event_detail"].Value = m.EventDetail ?? (object)DBNull.Value;
        cmd.Parameters["$event_meta"].Value = m.EventMeta ?? (object)DBNull.Value;
        cmd.Parameters["$event_args"].Value = eventArgs ?? (object)DBNull.Value;
        cmd.Parameters["$event_result"].Value = m.EventResult ?? (object)DBNull.Value;
        cmd.Parameters["$event_status"].Value = m.Status switch
        {
            EventStatus.Running => "running",
            EventStatus.Error => "error",
            _ => "done",
        };
        cmd.Parameters["$is_onboarding"].Value = m.IsOnboarding ? 1 : 0;
        cmd.Parameters["$created_at"].Value = m.CreatedAtUnixMs > 0 ? m.CreatedAtUnixMs : now;
    }

    private static void BindAttachment(SqliteCommand cmd, ChatAttachment a, string conversationId, long messageId, long now)
    {
        cmd.Parameters["$id"].Value = a.Id;
        cmd.Parameters["$conversation_id"].Value = conversationId;
        cmd.Parameters["$message_id"].Value = messageId;
        cmd.Parameters["$kind"].Value = a.Kind == AttachmentKind.Image ? "image" : "file";
        cmd.Parameters["$file_name"].Value = a.FileName;
        cmd.Parameters["$mime_type"].Value = a.MimeType ?? (object)DBNull.Value;
        cmd.Parameters["$size_bytes"].Value = a.SizeBytes;
        cmd.Parameters["$local_cache_path"].Value = a.LocalCachePath ?? (object)DBNull.Value;
        cmd.Parameters["$remote_uri"].Value = a.RemoteUri ?? (object)DBNull.Value;
        cmd.Parameters["$status"].Value = a.Status == AttachmentStatus.Failed ? "failed" : "sent";
        cmd.Parameters["$created_at"].Value = now;
    }

    private static ChatRole ParseRole(string value)
        => value switch
        {
            "agent" => ChatRole.Agent,
            "event" => ChatRole.Event,
            _ => ChatRole.User,
        };

    private static EventStatus ParseEventStatus(string? value)
        => value switch
        {
            "running" => EventStatus.Running,
            "error" => EventStatus.Error,
            _ => EventStatus.Done,
        };

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    // ---- executions ----

    public async Task InsertExecutionAsync(
        string executionId, string conversationId, string? remoteSessionId,
        string prompt, string status)
    {
        try
        {
            await using var connection = await AppDatabase.OpenAsync().ConfigureAwait(false);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO executions (id, conversation_id, remote_session_id, prompt, status, created_at)
                VALUES ($id, $conv, $remote, $prompt, $status, $ts);
                """;
            AppDatabase.Add(cmd, "$id", executionId);
            AppDatabase.Add(cmd, "$conv", conversationId);
            AppDatabase.Add(cmd, "$remote", remoteSessionId ?? (object)DBNull.Value);
            AppDatabase.Add(cmd, "$prompt", prompt);
            AppDatabase.Add(cmd, "$status", status);
            AppDatabase.Add(cmd, "$ts", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogFailure(nameof(InsertExecutionAsync), conversationId, ex);
        }
    }

    public async Task FinalizeExecutionAsync(
        string executionId, string conversationId, string result, string status, double durationMs)
    {
        try
        {
            await using var connection = await AppDatabase.OpenAsync().ConfigureAwait(false);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                UPDATE executions
                SET result = $result, status = $status, duration_ms = $dur
                WHERE id = $id AND conversation_id = $conv;
                """;
            AppDatabase.Add(cmd, "$result", result);
            AppDatabase.Add(cmd, "$status", status);
            AppDatabase.Add(cmd, "$dur", durationMs);
            AppDatabase.Add(cmd, "$id", executionId);
            AppDatabase.Add(cmd, "$conv", conversationId);
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogFailure(nameof(FinalizeExecutionAsync), conversationId, ex);
        }
    }

    // ---- trace events ----

    /// <summary>One trace row, as handed to <see cref="InsertTraceEventsAsync"/>.</summary>
    public readonly record struct TraceEventRow(
        string EventId, string? ExecutionId, string? SessionId,
        string Type, string PayloadJson, double? Ts);

    /// <summary>
    /// Writes a turn's whole trace in one connection and one transaction, reusing a single
    /// prepared command. A turn emits dozens of events, so the previous row-at-a-time API meant
    /// dozens of connection acquisitions and dozens of implicit commits.
    /// </summary>
    public async Task InsertTraceEventsAsync(string conversationId, IReadOnlyList<TraceEventRow> events)
    {
        if (events.Count == 0) return;

        try
        {
            await using var connection = await AppDatabase.OpenAsync().ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT OR REPLACE INTO trace_events (id, conversation_id, execution_id, session_id, type, payload_json, ts)
                VALUES ($id, $conv, $exec, $sess, $type, $payload, $ts);
                """;

            var id = cmd.Parameters.Add("$id", SqliteType.Text);
            cmd.Parameters.AddWithValue("$conv", conversationId);
            var exec = cmd.Parameters.Add("$exec", SqliteType.Text);
            var sess = cmd.Parameters.Add("$sess", SqliteType.Text);
            var type = cmd.Parameters.Add("$type", SqliteType.Text);
            var payload = cmd.Parameters.Add("$payload", SqliteType.Text);
            var ts = cmd.Parameters.Add("$ts", SqliteType.Real);
            cmd.Prepare();

            foreach (var e in events)
            {
                id.Value = e.EventId;
                exec.Value = e.ExecutionId ?? (object)DBNull.Value;
                sess.Value = e.SessionId ?? (object)DBNull.Value;
                type.Value = e.Type;
                payload.Value = e.PayloadJson;
                ts.Value = e.Ts ?? (object)DBNull.Value;
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            await transaction.CommitAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogFailure(nameof(InsertTraceEventsAsync), conversationId, ex);
        }
    }

    public async Task DeleteExecutionsAndTracesAsync(string conversationId)
    {
        try
        {
            await using var connection = await AppDatabase.OpenAsync().ConfigureAwait(false);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                DELETE FROM trace_events WHERE conversation_id = $conv;
                DELETE FROM executions WHERE conversation_id = $conv;
                """;
            AppDatabase.Add(cmd, "$conv", conversationId);
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogFailure(nameof(DeleteExecutionsAndTracesAsync), conversationId, ex);
        }
    }
}
