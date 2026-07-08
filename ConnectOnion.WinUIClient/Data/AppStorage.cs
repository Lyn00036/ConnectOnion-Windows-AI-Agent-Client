using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ConnectOnion.WinUIClient.Data;

/// <summary>
/// Shared filesystem + JSON plumbing for the local repositories.
///
/// Data lives under <c>%AppData%\ConnectOnion</c>. SQLite is the active store:
/// <code>
///   connectonion.db
/// </code>
/// The remaining JSON helpers are kept for JSON blob serialization and any
/// small file-based utilities that still need them.
/// </summary>
public static class AppStorage
{
    public static readonly string RootDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ConnectOnion");

    public static readonly string ConversationsDir = Path.Combine(RootDir, "conversations");

    /// <summary>
    /// Cache for decoded/downloaded incoming images (from <c>agent_image</c> events).
    /// Files are named by content hash so re-receiving the same image is idempotent
    /// and remote filenames are never trusted as local paths.
    /// </summary>
    public static readonly string ImageCacheDir = Path.Combine(RootDir, "cache", "images");

    /// <summary>
    /// camelCase enum + property naming keeps the JSON byte-compatible with the
    /// web app's string values ("light"/"dark", "sm"/"md"/"lg", "user"/"agent").
    /// </summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        AllowDuplicateProperties = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDir);
        Directory.CreateDirectory(ConversationsDir);
        Directory.CreateDirectory(ImageCacheDir);
    }

    public static string PathFor(string fileName) => Path.Combine(RootDir, fileName);

    /// <summary>
    /// Reads and deserializes a store file. Treats the file as untrusted input:
    /// a missing or corrupt file yields <paramref name="fallback"/> rather than
    /// throwing, mirroring the web app's try/catch-to-default behavior.
    /// </summary>
    public static async Task<T> ReadAsync<T>(string path, T fallback)
    {
        try
        {
            if (!File.Exists(path)) return fallback;
            await using var stream = File.OpenRead(path);
            var value = await JsonSerializer.DeserializeAsync<T>(stream, Json).ConfigureAwait(false);
            return value ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>
    /// Serializes and writes a store file atomically. Failures are swallowed so
    /// in-memory state keeps working even when the disk is unavailable.
    /// </summary>
    public static async Task WriteAsync<T>(string path, T value)
    {
        try
        {
            EnsureDirectories();
            var tmp = path + ".tmp";
            await using (var stream = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(stream, value, Json).ConfigureAwait(false);
            }
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            // Persisting is best-effort; keep the app usable if the write fails.
        }
    }
}
