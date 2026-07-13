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

        Assert.Equal(2, await ScalarLongAsync(connection, "PRAGMA user_version;"));
        Assert.Equal(1, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'usage_events';"));
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

        await SchemaMigrator.ApplyAsync(connection);

        Assert.Equal(2, await ScalarLongAsync(connection, "PRAGMA user_version;"));
        Assert.Equal(1, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'usage_events';"));
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
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<string> ScalarStringAsync(Microsoft.Data.Sqlite.SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync())!;
    }
}
