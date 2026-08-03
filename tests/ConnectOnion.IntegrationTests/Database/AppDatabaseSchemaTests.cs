using ConnectOnion.WinUIClient.Data;
using ConnectOnion.WinUIClient.Models;

namespace ConnectOnion.IntegrationTests.Database;

[Collection(DatabaseCollection.Name)]
public sealed class AppDatabaseSchemaTests(TempDatabaseFixture fixture)
{
    [Fact]
    public async Task OpenAsync_CalledTwice_CreatesSchemaIdempotently()
    {
        await using (var first = await AppDatabase.OpenAsync())
            Assert.Equal(1, await ScalarLongAsync(first, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'messages';"));

        await using var second = await AppDatabase.OpenAsync();
        Assert.Equal(1, await ScalarLongAsync(second, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'message_attachments';"));
        Assert.True(File.Exists(fixture.DatabasePath));
    }

    [Fact]
    public async Task OpenAsync_NewDatabase_AppliesLatestMigration()
    {
        await using var connection = await AppDatabase.OpenAsync();

        // Asserted against the constant, not a literal: these three tests are about "reached the
        // latest version", so a new migration should not have to touch them to stay honest.
        Assert.Equal(SchemaMigrator.LatestVersion, (int)await ScalarLongAsync(connection, "PRAGMA user_version;"));
        Assert.Equal(1, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'usage_events';"));
        Assert.Equal(1, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('sessions') WHERE name = 'mode';"));
        Assert.Equal(1, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('identity_keys') WHERE name = 'mnemonic';"));
        Assert.Equal(1, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('agents') WHERE name = 'icon_path';"));
    }

    [Fact]
    public async Task ApplyAsync_VersionFiveDatabase_AddsIconPathAndLeavesExistingAgentsWithoutOne()
    {
        await using var initialized = await AppDatabase.OpenAsync();
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await AppDatabase.CreateSchemaAsync(connection);
        await ExecuteAsync(connection, """
            ALTER TABLE identity_keys ADD COLUMN mnemonic TEXT NULL;
            PRAGMA user_version = 5;

            INSERT INTO agents (id, name, address, sort_order)
            VALUES ('agent-existing', 'Existing agent', '0xabc', 0);
            """);

        await SchemaMigrator.ApplyAsync(connection);

        Assert.Equal(SchemaMigrator.LatestVersion, (int)await ScalarLongAsync(connection, "PRAGMA user_version;"));
        Assert.Equal(1, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('agents') WHERE name = 'icon_path';"));
        // "No custom icon" is the normal state for everyone who upgrades: the column is nullable
        // precisely so an existing agent keeps working with its name-initial avatar.
        Assert.Equal(1, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM agents WHERE id = 'agent-existing' AND icon_path IS NULL;"));
    }

    [Fact]
    public async Task OpenAsync_Connection_EnablesWalAndForeignKeys()
    {
        await using var connection = await AppDatabase.OpenAsync();

        Assert.Equal("wal", await ScalarStringAsync(connection, "PRAGMA journal_mode;"));
        Assert.Equal(1, await ScalarLongAsync(connection, "PRAGMA foreign_keys;"));
    }

    [Fact]
    public async Task ApplyAsync_VersionZeroDatabase_UpgradesToLatestVersion()
    {
        await using var initialized = await AppDatabase.OpenAsync();
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        // A version-0 database is a *pre-versioning* one: it has the full baseline schema, it just
        // never recorded a version. Migrating a bare empty database is not a case that exists (the
        // app always ensures the baseline first), and pretending it is would let a migration that
        // alters an existing table pass a test with no table to alter.
        await AppDatabase.CreateSchemaAsync(connection);

        await SchemaMigrator.ApplyAsync(connection);

        Assert.Equal(SchemaMigrator.LatestVersion, (int)await ScalarLongAsync(connection, "PRAGMA user_version;"));
        Assert.Equal(1, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'usage_events';"));
        Assert.Equal(1, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('sessions') WHERE name = 'mode';"));
        Assert.Equal(1, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('identity_keys') WHERE name = 'mnemonic';"));
    }

    [Fact]
    public async Task ApplyAsync_VersionFourDatabase_AddsMnemonicColumnAndKeepsTheExistingIdentity()
    {
        await using var initialized = await AppDatabase.OpenAsync();
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await AppDatabase.CreateSchemaAsync(connection);
        await ExecuteAsync(connection, """
            PRAGMA user_version = 4;

            INSERT INTO identity_keys (id, address, private_seed)
            VALUES (1, '0xexisting', 'protected-seed-blob');
            """);

        await SchemaMigrator.ApplyAsync(connection);

        Assert.Equal(SchemaMigrator.LatestVersion, (int)await ScalarLongAsync(connection, "PRAGMA user_version;"));
        Assert.Equal(1, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('identity_keys') WHERE name = 'mnemonic';"));
        // The upgrade must not disturb an identity that already exists — its address is the one
        // every agent has authorized, and nothing can derive a phrase for its seed after the fact.
        Assert.Equal("0xexisting", await ScalarStringAsync(connection, "SELECT address FROM identity_keys WHERE id = 1;"));
        Assert.Equal("protected-seed-blob", await ScalarStringAsync(connection, "SELECT private_seed FROM identity_keys WHERE id = 1;"));
        Assert.Equal(1, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM identity_keys WHERE mnemonic IS NULL;"));
    }

    /// <summary>
    /// v7 replaces the <c>^Conversation \d+$</c> title test with a stored flag. The backfill has to
    /// reproduce that predicate exactly, because it decides which existing conversations may still
    /// have their title overwritten by their next message — get it wrong in one direction and a
    /// user's own title is destroyed, in the other and a conversation is stuck on "Conversation 4"
    /// forever.
    /// </summary>
    [Theory]
    // Still the placeholder: the next message may claim the title.
    [InlineData("Conversation 1", false)]
    [InlineData("Conversation 12", false)]
    [InlineData("Conversation 987", false)]
    // Settled titles, which must be left alone.
    [InlineData("Conversation 1 draft", true)]
    [InlineData("Conversation one", true)]
    [InlineData("Conversation", true)]
    [InlineData("My conversation 1", true)]
    [InlineData("Ship the migration", true)]
    [InlineData("对话 1", true)]
    public async Task ApplyAsync_VersionSixDatabase_BackfillsHasCustomTitleFromTheOldTitlePattern(
        string title,
        bool expectedHasCustomTitle)
    {
        await using var initialized = await AppDatabase.OpenAsync();
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await AppDatabase.CreateSchemaAsync(connection);
        await ExecuteAsync(connection, """
            ALTER TABLE sessions ADD COLUMN mode TEXT NOT NULL DEFAULT 'safe';
            PRAGMA user_version = 6;

            INSERT INTO agents (id, name, address, sort_order)
            VALUES ('agent-1', 'Agent', 'address', 0);
            """);

        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO sessions (id, agent_id, title, created_at, updated_at, sort_order, mode)
                VALUES ('session-1', 'agent-1', $title, 'now', 'now', 0, 'safe');
                """;
            insert.Parameters.AddWithValue("$title", title);
            await insert.ExecuteNonQueryAsync();
        }

        await SchemaMigrator.ApplyAsync(connection);

        Assert.Equal(SchemaMigrator.LatestVersion, (int)await ScalarLongAsync(connection, "PRAGMA user_version;"));
        Assert.Equal(
            expectedHasCustomTitle ? 1 : 0,
            await ScalarLongAsync(connection, "SELECT has_custom_title FROM sessions WHERE id = 'session-1';"));
        // The title text itself is never rewritten by the migration.
        Assert.Equal(title, await ScalarStringAsync(connection, "SELECT title FROM sessions WHERE id = 'session-1';"));
    }

    /// <summary>A conversation created after v7 starts as a placeholder regardless of the wording,
    /// which is what lets the placeholder be translated.</summary>
    [Fact]
    public void NewConversation_WithLocalizedPlaceholder_IsStillTreatedAsUntitled()
    {
        var session = SessionSummary.NewConversation("agent-1", [], "对话 {0}");

        Assert.Equal("对话 1", session.Title);
        Assert.False(session.HasCustomTitle);

        Assert.True(session.TryApplyTitleFromPrompt("Ship the migration"));
        Assert.Equal("Ship the migration", session.Title);
        Assert.True(session.HasCustomTitle);

        // A second message must not re-title a conversation whose title is already settled.
        Assert.False(session.TryApplyTitleFromPrompt("Something else entirely"));
        Assert.Equal("Ship the migration", session.Title);
    }

    /// <summary>
    /// The flag has to survive the write path, or a renamed conversation would silently revert to
    /// being overwritable by its next message after a restart.
    ///
    /// Rows are seeded directly and updated through <c>UpdateSessionAsync</c> rather than through
    /// <c>SaveAsync</c>: that one reconciles the *whole* index and deletes every session not in the
    /// list it is handed, which in this shared-database collection means deleting rows other tests
    /// still own.
    /// </summary>
    [Fact]
    public async Task SessionRepository_UpdateSession_RoundTripsHasCustomTitle()
    {
        await using (var connection = await AppDatabase.OpenAsync())
        {
            await ExecuteAsync(connection, """
                INSERT OR IGNORE INTO agents (id, name, address) VALUES ('agent-title', 'Agent', '0x1');
                INSERT OR REPLACE INTO sessions (id, agent_id, title, created_at, updated_at, sort_order, mode, has_custom_title)
                VALUES
                    ('session-placeholder', 'agent-title', 'Conversation 1', 'now', 'now', 0, 'safe', 0),
                    ('session-renamed', 'agent-title', 'Conversation 2', 'now', 'now', 1, 'safe', 0);
                """);
        }

        var repository = new SessionRepository();
        var loadedBefore = (await repository.LoadAsync()).Sessions;
        var renamed = loadedBefore.Single(s => s.Id == "session-renamed");
        Assert.False(renamed.HasCustomTitle);

        Assert.True(renamed.TryRename("Weekly report"));
        await repository.UpdateSessionAsync(renamed);

        var loadedAfter = (await repository.LoadAsync()).Sessions;
        Assert.False(loadedAfter.Single(s => s.Id == "session-placeholder").HasCustomTitle);

        var reloaded = loadedAfter.Single(s => s.Id == "session-renamed");
        Assert.True(reloaded.HasCustomTitle);
        Assert.Equal("Weekly report", reloaded.Title);
    }

    [Fact]
    public async Task ApplyAsync_VersionThreeDatabase_RemovesOrphanConversationGraphAndAddsGuards()
    {
        await using var initialized = await AppDatabase.OpenAsync();
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await AppDatabase.CreateSchemaAsync(connection);
        await ExecuteAsync(connection, """
            ALTER TABLE sessions ADD COLUMN mode TEXT NOT NULL DEFAULT 'safe';
            PRAGMA user_version = 3;

            INSERT INTO agents (id, name, address, sort_order)
            VALUES ('agent-valid', 'Valid agent', 'address', 0);

            INSERT INTO sessions (id, agent_id, title, created_at, updated_at, sort_order, mode)
            VALUES
                ('session-valid', 'agent-valid', 'Keep me', 'now', 'now', 0, 'safe'),
                ('session-orphan', 'agent-missing', 'Remove me', 'now', 'now', 1, 'safe');

            INSERT INTO messages (id, conversation_id, role, content, created_at)
            VALUES (1, 'session-orphan', 'user', 'orphan message', 1);

            INSERT INTO message_attachments (
                id, conversation_id, message_id, kind, file_name, size_bytes, status, created_at)
            VALUES ('attachment-orphan', 'session-orphan', 1, 'file', 'test.txt', 1, 'sent', 1);

            INSERT INTO executions (id, conversation_id, prompt, status, created_at)
            VALUES ('execution-orphan', 'session-orphan', 'test', 'complete', 1);

            INSERT INTO trace_events (id, conversation_id, execution_id, type, payload_json)
            VALUES ('trace-orphan', 'session-orphan', 'execution-orphan', 'tool_call', '{}');

            INSERT INTO app_meta (key, value)
            VALUES ('active_session_id', 'session-orphan');
            """);

        await SchemaMigrator.ApplyAsync(connection);

        Assert.Equal(SchemaMigrator.LatestVersion, (int)await ScalarLongAsync(connection, "PRAGMA user_version;"));
        Assert.Equal(1, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM sessions;"));
        Assert.Equal(1, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM sessions WHERE id = 'session-valid';"));
        Assert.Equal(0, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM messages WHERE conversation_id = 'session-orphan';"));
        Assert.Equal(0, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM message_attachments WHERE conversation_id = 'session-orphan';"));
        Assert.Equal(0, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM executions WHERE conversation_id = 'session-orphan';"));
        Assert.Equal(0, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM trace_events WHERE conversation_id = 'session-orphan';"));
        Assert.Equal(0, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM app_meta WHERE key = 'active_session_id';"));

        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => ExecuteAsync(connection, """
            INSERT INTO sessions (id, agent_id, title, created_at, updated_at, sort_order, mode)
            VALUES ('blocked-session', 'agent-missing', 'Blocked', 'now', 'now', 2, 'safe');
            """));
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(
            () => ExecuteAsync(connection, "DELETE FROM agents WHERE id = 'agent-valid';"));
    }

    [Fact]
    public async Task ApplyAsync_NewerDatabaseVersion_ThrowsWithoutChangingVersion()
    {
        await using var initialized = await AppDatabase.OpenAsync();
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var stamp = connection.CreateCommand())
        {
            stamp.CommandText = "PRAGMA user_version = 999;";
            await stamp.ExecuteNonQueryAsync();
        }

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => SchemaMigrator.ApplyAsync(connection));

        Assert.Contains("newer version", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(999, await ScalarLongAsync(connection, "PRAGMA user_version;"));
    }

    private static async Task<long> ScalarLongAsync(Microsoft.Data.Sqlite.SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<string> ScalarStringAsync(Microsoft.Data.Sqlite.SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(
            await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture)!;
    }

    private static async Task ExecuteAsync(Microsoft.Data.Sqlite.SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
