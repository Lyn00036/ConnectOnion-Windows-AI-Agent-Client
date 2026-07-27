using ConnectOnion.WinUIClient.Data;

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
