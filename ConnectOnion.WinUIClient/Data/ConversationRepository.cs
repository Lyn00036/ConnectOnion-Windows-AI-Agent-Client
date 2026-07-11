using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Models;
using Microsoft.Data.Sqlite;

namespace ConnectOnion.WinUIClient.Data;

/// <summary>
/// Row-level message persistence. Each chat bubble is a row in the
/// <c>messages</c> table, keyed by <c>(conversation_id, id)</c>.
/// Replaces the previous JSON blob approach so messages can be queried,
/// paged, and written incrementally.
/// </summary>
public sealed class ConversationRepository
{
    public async Task<IReadOnlyList<ChatMessage>> LoadMessagesAsync(string conversationId)
    {
        var messages = new List<ChatMessage>();
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
                WHERE conversation_id = $conversation_id
                ORDER BY id;
                """;
            AppDatabase.Add(command, "$conversation_id", conversationId);
            await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                messages.Add(RowToMessage(reader));
            }

            await LoadAttachmentsIntoAsync(connection, conversationId, messages).ConfigureAwait(false);
        }
        catch
        {
            // Missing / corrupt table → start fresh.
        }
        return messages;
    }

    private static async Task LoadAttachmentsIntoAsync(
        SqliteConnection connection, string conversationId, IReadOnlyList<ChatMessage> messages)
    {
        if (messages.Count == 0) return;

        var byId = new Dictionary<long, ChatMessage>();
        foreach (var m in messages) byId[m.Id] = m;

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT message_id, id, kind, file_name, mime_type, size_bytes,
                   local_cache_path, remote_uri, status
            FROM message_attachments
            WHERE conversation_id = $conversation_id
            ORDER BY message_id, rowid;
            """;
        AppDatabase.Add(command, "$conversation_id", conversationId);
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
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

    public async Task SaveMessagesAsync(string conversationId, IReadOnlyList<ChatMessage> messages)
    {
        if (messages.Count == 0)
        {
            await DeleteMessagesAsync(conversationId).ConfigureAwait(false);
            return;
        }

        try
        {
            await using var connection = await AppDatabase.OpenAsync().ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);

            // Remove old rows then re-insert the full list.
            await using (var del = connection.CreateCommand())
            {
                del.Transaction = transaction;
                del.CommandText = """
                    DELETE FROM message_attachments WHERE conversation_id = $conversation_id;
                    DELETE FROM messages WHERE conversation_id = $conversation_id;
                    """;
                AppDatabase.Add(del, "$conversation_id", conversationId);
                await del.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (var message in messages)
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO messages
                      (id, conversation_id, role, content, agent_name,
                       event_kind, event_key, event_eyebrow, event_title,
                       event_detail, event_meta, event_args, event_result,
                       event_status, is_onboarding, created_at)
                    VALUES
                      ($id, $conversation_id, $role, $content, $agent_name,
                       $event_kind, $event_key, $event_eyebrow, $event_title,
                       $event_detail, $event_meta, $event_args, $event_result,
                       $event_status, $is_onboarding, $created_at);
                    """;
                BindMessage(insert, message, conversationId, now);
                await insert.ExecuteNonQueryAsync().ConfigureAwait(false);

                foreach (var attachment in message.Attachments)
                {
                    await using var insertAttachment = connection.CreateCommand();
                    insertAttachment.Transaction = transaction;
                    insertAttachment.CommandText = """
                        INSERT INTO message_attachments
                          (id, conversation_id, message_id, kind, file_name, mime_type,
                           size_bytes, local_cache_path, remote_uri, status, created_at)
                        VALUES
                          ($id, $conversation_id, $message_id, $kind, $file_name, $mime_type,
                           $size_bytes, $local_cache_path, $remote_uri, $status, $created_at);
                        """;
                    BindAttachment(insertAttachment, attachment, conversationId, message.Id, now);
                    await insertAttachment.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }

            await transaction.CommitAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort: keep the live conversation working if the write fails.
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
        catch
        {
            // Ignore delete failures.
        }
    }

    // --- helpers ---

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
        };
        // Tool activity is stored in the existing event_args payload column. This keeps the
        // schema backward compatible while making the aggregate durable across page teardown
        // and application restart. Legacy per-tool rows still load normally and are upgraded
        // by the chat projection when they are next replayed/saved.
        if (m.EventKind == "tool_activity" && !string.IsNullOrWhiteSpace(m.EventArgs))
        {
            try { m.ToolActivity = JsonSerializer.Deserialize<ToolActivityViewModel>(m.EventArgs); }
            catch { m.EventKind = "activity"; m.EventTitle = "Tool execution history could not be restored"; }
        }
        return m;
    }

    private static void BindMessage(SqliteCommand cmd, ChatMessage m, string conversationId, long now)
    {
        AppDatabase.Add(cmd, "$id", m.Id);
        AppDatabase.Add(cmd, "$conversation_id", conversationId);
        AppDatabase.Add(cmd, "$role", m.Role.ToString().ToLowerInvariant());
        AppDatabase.Add(cmd, "$content", string.IsNullOrEmpty(m.Content) ? DBNull.Value : m.Content);
        AppDatabase.Add(cmd, "$agent_name", m.AgentName ?? (object)DBNull.Value);
        AppDatabase.Add(cmd, "$event_kind", string.IsNullOrEmpty(m.EventKind) ? DBNull.Value : m.EventKind);
        AppDatabase.Add(cmd, "$event_key", m.EventKey ?? (object)DBNull.Value);
        AppDatabase.Add(cmd, "$event_eyebrow", string.IsNullOrEmpty(m.EventEyebrow) ? DBNull.Value : m.EventEyebrow);
        AppDatabase.Add(cmd, "$event_title", string.IsNullOrEmpty(m.EventTitle) ? DBNull.Value : m.EventTitle);
        AppDatabase.Add(cmd, "$event_detail", m.EventDetail ?? (object)DBNull.Value);
        AppDatabase.Add(cmd, "$event_meta", m.EventMeta ?? (object)DBNull.Value);
        var eventArgs = m.IsToolActivityEvent
            ? JsonSerializer.Serialize(m.ToolActivity)
            : m.EventArgs;
        AppDatabase.Add(cmd, "$event_args", eventArgs ?? (object)DBNull.Value);
        AppDatabase.Add(cmd, "$event_result", m.EventResult ?? (object)DBNull.Value);
        AppDatabase.Add(cmd, "$event_status", m.Status switch
        {
            EventStatus.Running => "running",
            EventStatus.Error => "error",
            _ => "done",
        });
        AppDatabase.Add(cmd, "$is_onboarding", m.IsOnboarding ? 1 : 0);
        AppDatabase.Add(cmd, "$created_at", now);
    }

    private static void BindAttachment(SqliteCommand cmd, ChatAttachment a, string conversationId, long messageId, long now)
    {
        AppDatabase.Add(cmd, "$id", a.Id);
        AppDatabase.Add(cmd, "$conversation_id", conversationId);
        AppDatabase.Add(cmd, "$message_id", messageId);
        AppDatabase.Add(cmd, "$kind", a.Kind == AttachmentKind.Image ? "image" : "file");
        AppDatabase.Add(cmd, "$file_name", a.FileName);
        AppDatabase.Add(cmd, "$mime_type", a.MimeType ?? (object)DBNull.Value);
        AppDatabase.Add(cmd, "$size_bytes", a.SizeBytes);
        AppDatabase.Add(cmd, "$local_cache_path", a.LocalCachePath ?? (object)DBNull.Value);
        AppDatabase.Add(cmd, "$remote_uri", a.RemoteUri ?? (object)DBNull.Value);
        AppDatabase.Add(cmd, "$status", a.Status == AttachmentStatus.Failed ? "failed" : "sent");
        AppDatabase.Add(cmd, "$created_at", now);
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
        catch { /* best-effort */ }
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
        catch { /* best-effort */ }
    }

    // ---- trace events ----

    public async Task InsertTraceEventAsync(
        string eventId, string conversationId, string? executionId,
        string? sessionId, string type, string payloadJson, double? ts)
    {
        try
        {
            await using var connection = await AppDatabase.OpenAsync().ConfigureAwait(false);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO trace_events (id, conversation_id, execution_id, session_id, type, payload_json, ts)
                VALUES ($id, $conv, $exec, $sess, $type, $payload, $ts);
                """;
            AppDatabase.Add(cmd, "$id", eventId);
            AppDatabase.Add(cmd, "$conv", conversationId);
            AppDatabase.Add(cmd, "$exec", executionId ?? (object)DBNull.Value);
            AppDatabase.Add(cmd, "$sess", sessionId ?? (object)DBNull.Value);
            AppDatabase.Add(cmd, "$type", type);
            AppDatabase.Add(cmd, "$payload", payloadJson);
            AppDatabase.Add(cmd, "$ts", ts ?? (object)DBNull.Value);
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch { /* best-effort */ }
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
        catch { /* best-effort */ }
    }
}
