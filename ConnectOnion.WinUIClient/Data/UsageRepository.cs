using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Models;
using Microsoft.Data.Sqlite;

namespace ConnectOnion.WinUIClient.Data;

/// <summary>
/// The token-usage ledger (<c>usage_events</c>, schema v2).
///
/// <b>The ledger is independent of conversation and agent lifetime.</b> Deleting a conversation or
/// an agent does not touch it — the table has no foreign key and is not part of any cascade. That is
/// the whole point: the tokens were spent, and that stays true after the chat is gone. Totals that
/// shrank whenever a user tidied their sidebar would be unable to answer any question honestly.
///
/// The only way usage is ever removed is <see cref="ClearAsync"/>, which the user invokes
/// explicitly from the Usage panel.
///
/// Aggregation is done in SQL (<c>GROUP BY model</c>), never by reading rows into memory — the
/// ledger grows without bound, and "load everything and group in C#" would become the slowest thing
/// in Settings.
/// </summary>
public sealed class UsageRepository
{
    /// <summary>
    /// Appends a turn's usage. Idempotent: the primary key is the server's event id, so re-persisting
    /// the same run (a retry, a replay) updates rather than double-counts.
    /// </summary>
    public async Task InsertAsync(IReadOnlyList<UsageRecord> records)
    {
        if (records.Count == 0) return;

        try
        {
            await using var connection = await AppDatabase.OpenAsync().ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO usage_events (
                    id, conversation_id, agent_id, agent_name, model,
                    input_tokens, output_tokens, cached_tokens, cache_write_tokens,
                    duration_ms, created_at)
                VALUES ($id, $conv, $agent, $agent_name, $model,
                        $in, $out, $cached, $cache_write, $duration, $created)
                ON CONFLICT(id) DO UPDATE SET
                    input_tokens       = excluded.input_tokens,
                    output_tokens      = excluded.output_tokens,
                    cached_tokens      = excluded.cached_tokens,
                    cache_write_tokens = excluded.cache_write_tokens,
                    duration_ms        = excluded.duration_ms;
                """;

            var id = cmd.Parameters.Add("$id", SqliteType.Text);
            var conv = cmd.Parameters.Add("$conv", SqliteType.Text);
            var agent = cmd.Parameters.Add("$agent", SqliteType.Text);
            var agentName = cmd.Parameters.Add("$agent_name", SqliteType.Text);
            var model = cmd.Parameters.Add("$model", SqliteType.Text);
            var input = cmd.Parameters.Add("$in", SqliteType.Integer);
            var output = cmd.Parameters.Add("$out", SqliteType.Integer);
            var cached = cmd.Parameters.Add("$cached", SqliteType.Integer);
            var cacheWrite = cmd.Parameters.Add("$cache_write", SqliteType.Integer);
            var duration = cmd.Parameters.Add("$duration", SqliteType.Real);
            var created = cmd.Parameters.Add("$created", SqliteType.Integer);
            cmd.Prepare();

            foreach (var r in records)
            {
                id.Value = r.Id;
                conv.Value = r.ConversationId ?? (object)DBNull.Value;
                agent.Value = r.AgentId ?? (object)DBNull.Value;
                agentName.Value = r.AgentName ?? (object)DBNull.Value;
                model.Value = r.Model;
                input.Value = r.InputTokens;
                output.Value = r.OutputTokens;
                cached.Value = r.CachedTokens;
                cacheWrite.Value = r.CacheWriteTokens;
                duration.Value = r.DurationMs ?? (object)DBNull.Value;
                created.Value = r.CreatedAt;
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            await transaction.CommitAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Accounting must never take a turn down with it.
            LogFailure(nameof(InsertAsync), ex);
        }
    }

    /// <summary>Per-model totals for the window, biggest spender first.</summary>
    public async Task<IReadOnlyList<ModelUsageSummary>> GetByModelAsync(long? sinceUnixMs, string? agentId = null)
    {
        var results = new List<ModelUsageSummary>();

        try
        {
            await using var connection = await AppDatabase.OpenAsync().ConfigureAwait(false);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"""
                SELECT model,
                       COUNT(*)                       AS calls,
                       SUM(input_tokens)              AS input_tokens,
                       SUM(output_tokens)             AS output_tokens,
                       SUM(cached_tokens)             AS cached_tokens,
                       SUM(cache_write_tokens)        AS cache_write_tokens,
                       COALESCE(SUM(duration_ms), 0)  AS duration_ms
                FROM usage_events
                WHERE ($since IS NULL OR created_at >= $since)
                  AND ($agent IS NULL OR agent_id = $agent)
                GROUP BY model
                ORDER BY (SUM(input_tokens) + SUM(output_tokens)) DESC;
                """;
            AppDatabase.Add(cmd, "$since", sinceUnixMs);
            AppDatabase.Add(cmd, "$agent", agentId);

            await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                results.Add(new ModelUsageSummary(
                    Model: reader.GetString(0),
                    Calls: reader.GetInt64(1),
                    InputTokens: reader.GetInt64(2),
                    OutputTokens: reader.GetInt64(3),
                    CachedTokens: reader.GetInt64(4),
                    CacheWriteTokens: reader.GetInt64(5),
                    TotalDurationMs: reader.GetDouble(6)));
            }
        }
        catch (Exception ex)
        {
            LogFailure(nameof(GetByModelAsync), ex);
        }

        return results;
    }

    /// <summary>Timestamp of the oldest recorded call, so the panel can say what "all time" means.
    /// Null when the ledger is empty.</summary>
    public async Task<DateTimeOffset?> GetFirstRecordedAsync()
    {
        try
        {
            await using var connection = await AppDatabase.OpenAsync().ConfigureAwait(false);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT MIN(created_at) FROM usage_events;";

            var value = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            if (value is null || value is DBNull) return null;

            return DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(value)).ToLocalTime();
        }
        catch (Exception ex)
        {
            LogFailure(nameof(GetFirstRecordedAsync), ex);
            return null;
        }
    }

    /// <summary>
    /// Erases usage history — the only path that ever deletes from this table, and only ever on an
    /// explicit user action. <paramref name="sinceUnixMs"/> null clears everything.
    /// </summary>
    public async Task ClearAsync(long? sinceUnixMs = null)
    {
        try
        {
            await using var connection = await AppDatabase.OpenAsync().ConfigureAwait(false);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM usage_events WHERE ($since IS NULL OR created_at >= $since);";
            AppDatabase.Add(cmd, "$since", sinceUnixMs);
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogFailure(nameof(ClearAsync), ex);
        }
    }

    private static void LogFailure(string operation, Exception ex)
        => Debug.WriteLine($"[UsageRepository] {operation} failed: {ex}");
}
