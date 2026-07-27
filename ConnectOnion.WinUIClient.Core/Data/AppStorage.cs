using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConnectOnion.WinUIClient.Data;

/// <summary>
/// Shared filesystem and JSON plumbing for the local repositories.
///
/// Data lives under <c>%AppData%\ConnectOnion</c>. SQLite is the active store:
/// <code>
///   connectonion.db
/// </code>
///
/// The remaining JSON helpers are kept for JSON blob serialization and any
/// small file-based utilities that still need them.
/// </summary>
public static class AppStorage
{
    /// <summary>
    /// Optional data-root override for isolated automation and portable
    /// development runs.
    ///
    /// Production leaves this unset and continues to use
    /// <c>%AppData%\ConnectOnion</c>.
    /// </summary>
    public const string DataRootEnvironmentVariable =
        "CONNECTONION_DATA_ROOT";

    /// <summary>
    /// Root directory for all ConnectOnion local application data.
    /// </summary>
    public static readonly string RootDir = ResolveRootDir();

    /// <summary>
    /// Legacy or auxiliary conversation file storage.
    /// Conversation records themselves are primarily stored in SQLite.
    /// </summary>
    public static readonly string ConversationsDir =
        Path.Combine(RootDir, "conversations");

    /// <summary>
    /// Content store for user-selected and agent-produced conversation images.
    ///
    /// Files are named by content hash so storing the same image is idempotent,
    /// and remote filenames are never trusted as local paths.
    /// </summary>
    public static readonly string ImageCacheDir =
        Path.Combine(RootDir, "cache", "images");

    /// <summary>
    /// Permanent storage for user-selected Agent icons.
    ///
    /// The database stores only a relative path such as:
    /// <code>
    /// avatars/agent-123.png
    /// </code>
    ///
    /// The image itself is stored in this directory.
    /// </summary>
    public static readonly string AgentIconsDir =
        Path.Combine(RootDir, "avatars");

    /// <summary>
    /// Temporary storage used while an Agent icon is being selected.
    ///
    /// For example, Add Agent may place a processed image here while the form
    /// is open. If the user confirms the form, the image is moved to
    /// <see cref="AgentIconsDir"/>. If the user cancels, the temporary image
    /// can be deleted without leaving an unused permanent file.
    /// </summary>
    public static readonly string TemporaryAgentIconsDir =
        Path.Combine(RootDir, "temp", "avatars");

    /// <summary>
    /// Rolling structured application logs.
    /// </summary>
    public static readonly string LogsDir =
        Path.Combine(RootDir, "logs");

    /// <summary>
    /// camelCase enum and property naming keeps the JSON byte-compatible with
    /// the web app's string values:
    /// <c>light</c>/<c>dark</c>, <c>sm</c>/<c>md</c>/<c>lg</c>,
    /// and <c>user</c>/<c>agent</c>.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        AllowDuplicateProperties = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
        },
    };

    /// <summary>
    /// Creates every application-managed directory if it does not already
    /// exist.
    /// </summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDir);
        Directory.CreateDirectory(ConversationsDir);
        Directory.CreateDirectory(ImageCacheDir);
        Directory.CreateDirectory(AgentIconsDir);
        Directory.CreateDirectory(TemporaryAgentIconsDir);
        Directory.CreateDirectory(LogsDir);
    }

    /// <summary>
    /// Returns an absolute path under the application's root directory.
    /// </summary>
    public static string PathFor(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        return Path.Combine(RootDir, fileName);
    }

    /// <summary>
    /// Creates the relative path that should be stored in
    /// <c>agents.icon_path</c>.
    ///
    /// For example:
    /// <code>
    /// CreateAgentIconRelativePath("agent-123.png")
    /// // returns "avatars/agent-123.png"
    /// </code>
    ///
    /// Forward slashes are used in the persisted value so the stored path
    /// remains stable and independent of Windows path formatting.
    /// </summary>
    public static string CreateAgentIconRelativePath(string fileName)
    {
        var safeFileName = ValidateLeafFileName(fileName);

        return $"avatars/{safeFileName}";
    }

    /// <summary>
    /// Resolves an Agent icon path stored in SQLite into an absolute local
    /// filesystem path.
    ///
    /// The supplied path must resolve inside <see cref="AgentIconsDir"/>.
    /// Paths that attempt to escape the avatar directory are rejected.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The relative path is null, empty, or contains only whitespace.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The path resolves outside the managed Agent icon directory.
    /// </exception>
    public static string GetAgentIconAbsolutePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var normalizedRelativePath = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        var absolutePath = Path.GetFullPath(
            Path.Combine(RootDir, normalizedRelativePath));

        EnsurePathInsideDirectory(
            absolutePath,
            AgentIconsDir,
            "The Agent icon path must remain inside the managed avatars directory.");

        return absolutePath;
    }

    /// <summary>
    /// Attempts to resolve a stored Agent icon path.
    ///
    /// Returns <see langword="false"/> for an empty, malformed, or unsafe path.
    /// This is suitable for UI fallback behavior where an invalid path should
    /// display the default Agent avatar instead of crashing the application.
    /// </summary>
    public static bool TryGetAgentIconAbsolutePath(
        string? relativePath,
        out string? absolutePath)
    {
        absolutePath = null;

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        try
        {
            absolutePath = GetAgentIconAbsolutePath(relativePath);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns an absolute path in the temporary Agent icon directory.
    ///
    /// Only a plain filename is accepted. Directory components such as
    /// <c>../</c> or <c>folder/image.png</c> are rejected.
    /// </summary>
    public static string GetTemporaryAgentIconAbsolutePath(string fileName)
    {
        var safeFileName = ValidateLeafFileName(fileName);

        return Path.Combine(TemporaryAgentIconsDir, safeFileName);
    }

    /// <summary>
    /// Returns an absolute path in the permanent Agent icon directory.
    ///
    /// This method is useful when committing a processed temporary icon into
    /// permanent storage.
    /// </summary>
    public static string GetPermanentAgentIconAbsolutePath(string fileName)
    {
        var safeFileName = ValidateLeafFileName(fileName);

        return Path.Combine(AgentIconsDir, safeFileName);
    }

    private static string ResolveRootDir()
    {
        var configured = Environment.GetEnvironmentVariable(
            DataRootEnvironmentVariable);

        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "ConnectOnion")
            : Path.GetFullPath(configured);
    }

    /// <summary>
    /// Verifies that a value contains only one filename and no directory
    /// components.
    /// </summary>
    private static string ValidateLeafFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var trimmed = fileName.Trim();
        var leafFileName = Path.GetFileName(trimmed);

        if (!string.Equals(
                trimmed,
                leafFileName,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The value must contain a filename without directory components.",
                nameof(fileName));
        }

        if (leafFileName is "." or "..")
        {
            throw new ArgumentException(
                "The value is not a valid filename.",
                nameof(fileName));
        }

        if (leafFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException(
                "The filename contains invalid characters.",
                nameof(fileName));
        }

        return leafFileName;
    }

    /// <summary>
    /// Ensures that a resolved file path remains inside an expected directory.
    /// </summary>
    private static void EnsurePathInsideDirectory(
        string candidatePath,
        string expectedDirectory,
        string errorMessage)
    {
        var candidateFullPath = Path.GetFullPath(candidatePath);

        var directoryFullPath = Path
            .GetFullPath(expectedDirectory)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!candidateFullPath.StartsWith(
                directoryFullPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(errorMessage);
        }
    }

    /// <summary>
    /// Reads and deserializes a store file.
    ///
    /// Treats the file as untrusted input: a missing or corrupt file yields
    /// <paramref name="fallback"/> rather than throwing, mirroring the web
    /// app's try/catch-to-default behavior.
    /// </summary>
    public static async Task<T> ReadAsync<T>(
        string path,
        T fallback)
    {
        try
        {
            if (!File.Exists(path))
            {
                return fallback;
            }

            await using var stream = File.OpenRead(path);

            var value = await JsonSerializer
                .DeserializeAsync<T>(stream, Json)
                .ConfigureAwait(false);

            return value ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>
    /// Serializes and writes a store file atomically.
    ///
    /// Failures are swallowed so in-memory state keeps working even when the
    /// disk is unavailable.
    /// </summary>
    public static async Task WriteAsync<T>(
        string path,
        T value)
    {
        try
        {
            EnsureDirectories();

            var tmp = path + ".tmp";

            await using (var stream = File.Create(tmp))
            {
                await JsonSerializer
                    .SerializeAsync(stream, value, Json)
                    .ConfigureAwait(false);
            }

            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            // Persisting is best-effort; keep the app usable if the write fails.
        }
    }
}
