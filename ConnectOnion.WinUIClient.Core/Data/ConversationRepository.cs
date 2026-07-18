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
public sealed partial class ConversationRepository
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
        // Cancellation is not a failure — rethrow it so a page that navigated away during the
        // load doesn't get logged as a database error, and doesn't silently receive a partial
        // conversation as if it were the whole thing.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Missing / corrupt table → start fresh. Note `messages` may already hold rows
            // read before the failure; they are returned rather than discarded, since a
            // partial history still beats an empty one for a user staring at the page.
            LogFailure(nameof(LoadMessagesAsync), conversationId, ex);
        }
        return messages;
    }

    /// <summary>
    /// Attaches each row's attachments to the already-materialized messages, in one pass.
    ///
    /// Separate query rather than a JOIN on the message read: a JOIN would repeat every
    /// message column once per attachment and then need de-duplicating in code. Two queries
    /// and a dictionary is both cheaper and simpler. Pass <paramref name="onlyMessageId"/> to
    /// scope it to a single bubble (the <see cref="LoadLastAgentMessageAsync"/> path).
    /// </summary>
    private static async Task LoadAttachmentsIntoAsync(
        SqliteConnection connection, string conversationId, IReadOnlyList<ChatMessage> messages,
        long? onlyMessageId = null, CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0) return;

        // Index by id so the reader below is a dictionary hit per attachment rather than a
        // scan of the message list — this runs over a whole conversation on every open.
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
            // An attachment whose message wasn't in the requested set is skipped rather than
            // treated as an error — expected whenever this is called with a scoped message
            // list, and harmless otherwise.
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

            // One timestamp for the whole batch: a turn's bubbles are conceptually written at
            // one instant, and sharing it keeps their created_at ordering consistent with
            // their id ordering.
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            // Commands are built once and re-bound per row inside the loop. Rebuilding them
            // per message would re-prepare the same SQL for every bubble in the turn.
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

    /// <summary>
    /// Drops a conversation's bubbles and their attachments.
    ///
    /// <b>The statement order is load-bearing.</b> The FKs in this schema declare no
    /// <c>ON DELETE</c> behavior, so nothing cascades — children must go before parents by
    /// hand. Reversing these two lines raises a constraint violation that the catch below
    /// swallows into a log line, leaving the conversation half-deleted with no visible error.
    /// The same applies one level up in <c>SessionRepository</c>: this method and
    /// <c>DeleteExecutionsAndTracesAsync</c> both have to run before the <c>sessions</c> row.
    /// </summary>
    public async Task DeleteMessagesAsync(string conversationId)
    {
        try
        {
            await using var connection = await AppDatabase.OpenAsync().ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            // Attachments reference messages, so they go first.
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
