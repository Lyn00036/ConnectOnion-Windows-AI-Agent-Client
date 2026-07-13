using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace ConnectOnion.WinUIClient.Data;

/// <summary>
/// Versions the local database and upgrades it forward, one step at a time.
///
/// Before this existed the schema was created purely with <c>CREATE TABLE IF NOT EXISTS</c>, which
/// silently does nothing when a table already exists but has the wrong shape — so adding a column
/// meant every existing install either crashed or quietly lost the new field, and the documented
/// recovery was "delete the .db file". That is fine in development and unacceptable once anyone
/// has data worth keeping.
///
/// The version lives in SQLite's own <c>PRAGMA user_version</c> (an integer in the DB header)
/// rather than a row in <c>app_meta</c>: it needs no table to exist first, costs no query, and is
/// written atomically inside the same transaction as the migration it records.
///
/// <b>To add a migration:</b> append one entry to <see cref="Migrations"/> with the next version
/// number and bump <see cref="LatestVersion"/>. Never edit or renumber a released migration —
/// databases in the wild have already recorded that they ran it.
/// </summary>
internal static class SchemaMigrator
{
    /// <summary>
    /// Version 1 is the baseline: the schema produced by <see cref="AppDatabase.CreateSchemaAsync"/>.
    /// Databases created before versioning existed have that exact shape, so they are stamped as
    /// version 1 without running anything (see <see cref="ApplyAsync"/>).
    /// </summary>
    private const int BaselineVersion = 1;

    /// <summary>The schema version this build expects. Bump when you append to <see cref="Migrations"/>.</summary>
    internal const int LatestVersion = 1;

    /// <summary>
    /// Ordered forward migrations. Each runs exactly once, in its own transaction, on databases
    /// whose recorded version is below it.
    /// </summary>
    /// <example>
    /// The next one will look like:
    /// <code>
    /// new(2, "ALTER TABLE agents ADD COLUMN pinned INTEGER NOT NULL DEFAULT 0;"),
    /// </code>
    /// </example>
    private static readonly IReadOnlyList<Migration> Migrations = Array.Empty<Migration>();

    private sealed record Migration(int Version, string Sql);

    /// <summary>
    /// Brings <paramref name="connection"/> up to <see cref="LatestVersion"/>. Called by
    /// <see cref="AppDatabase"/> right after the baseline schema is ensured.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The database was written by a newer build than this one. We refuse rather than run against a
    /// shape we do not understand — a downgrade that "mostly works" is how data gets corrupted.
    /// </exception>
    internal static async Task ApplyAsync(SqliteConnection connection)
    {
        var current = await ReadVersionAsync(connection).ConfigureAwait(false);

        // 0 means "never versioned": either a database we just created (already at the baseline
        // shape) or one created by a pre-versioning build (same shape, because the baseline is
        // exactly what those builds produced). Either way it is a valid version 1 — stamp it.
        if (current == 0)
        {
            await WriteVersionAsync(connection, BaselineVersion).ConfigureAwait(false);
            current = BaselineVersion;
        }

        if (current > LatestVersion)
        {
            throw new InvalidOperationException(
                $"This database is at schema version {current}, but this build of ConnectOnion only " +
                $"understands version {LatestVersion}. It was most likely written by a newer version " +
                "of the app. Update ConnectOnion rather than continuing with this database.");
        }

        foreach (var migration in Migrations)
        {
            if (migration.Version <= current) continue;

            await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = migration.Sql;
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            // PRAGMA user_version does not accept a parameter, and the value is an int constant
            // from our own migration list — never user input.
            await using (var stamp = connection.CreateCommand())
            {
                stamp.Transaction = (SqliteTransaction)transaction;
                stamp.CommandText = $"PRAGMA user_version = {migration.Version};";
                await stamp.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            await transaction.CommitAsync().ConfigureAwait(false);
            current = migration.Version;
        }
    }

    private static async Task<int> ReadVersionAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var value = await command.ExecuteScalarAsync().ConfigureAwait(false);
        return value is null ? 0 : Convert.ToInt32(value);
    }

    private static async Task WriteVersionAsync(SqliteConnection connection, int version)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {version};";
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
