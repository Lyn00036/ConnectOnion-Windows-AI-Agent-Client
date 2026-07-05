using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Models;

namespace ConnectOnion.WinUIClient.Data;

/// <summary>
/// Local persistence for desktop preferences. Port of <c>preferencesStorage.ts</c>.
/// The web app kept each setting under its own key; here they live in one
/// SQLite row.
/// </summary>
public sealed class PreferencesRepository
{
    public async Task<PreferencesSnapshot> LoadAsync()
    {
        await using var connection = await AppDatabase.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT theme, sidebar_visible, message_font_size, shortcut_overrides_json
            FROM preferences
            WHERE id = 1;
            """;

        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        if (!await reader.ReadAsync().ConfigureAwait(false)) return new PreferencesSnapshot();

        var snapshot = new PreferencesSnapshot
        {
            Theme = Enum.TryParse<ThemeMode>(reader.GetString(0), out var theme) ? theme : ThemeMode.Light,
            SidebarVisible = reader.GetInt32(1) != 0,
            MessageFontSize = Enum.TryParse<MessageFontSize>(reader.GetString(2), out var size) ? size : MessageFontSize.Md,
        };

        try
        {
            snapshot.ShortcutOverrides =
                JsonSerializer.Deserialize(reader.GetString(3), AppJsonContext.Default.DictionaryStringString) ?? new();
        }
        catch
        {
            snapshot.ShortcutOverrides = new();
        }

        return snapshot;
    }

    public async Task SaveAsync(PreferencesSnapshot snapshot)
    {
        await using var connection = await AppDatabase.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO preferences (id, theme, sidebar_visible, message_font_size, shortcut_overrides_json)
            VALUES (1, $theme, $sidebar_visible, $message_font_size, $shortcut_overrides_json)
            ON CONFLICT(id) DO UPDATE SET
                theme = excluded.theme,
                sidebar_visible = excluded.sidebar_visible,
                message_font_size = excluded.message_font_size,
                shortcut_overrides_json = excluded.shortcut_overrides_json;
            """;
        AppDatabase.Add(command, "$theme", snapshot.Theme.ToString());
        AppDatabase.Add(command, "$sidebar_visible", snapshot.SidebarVisible ? 1 : 0);
        AppDatabase.Add(command, "$message_font_size", snapshot.MessageFontSize.ToString());
        AppDatabase.Add(command, "$shortcut_overrides_json", JsonSerializer.Serialize(snapshot.ShortcutOverrides, AppJsonContext.Default.DictionaryStringString));
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
