using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ConnectOnion.Protocol;
using ConnectOnion.WinUIClient.Data;

namespace ConnectOnion.WinUIClient.Services;

/// <summary>
/// Resolves an inbound image reference (from an <c>agent_image</c> event) into a
/// locally cached file, so the chat message list and SQLite never hold a
/// multi-megabyte base64 string directly (see task requirement: no giant base64 in
/// the ViewModel or DB — only metadata + cache path).
///
/// Only <c>data:image/*</c> and <c>http(s)://</c> sources are accepted — current
/// source only ever produces the data-URL form (<c>network/io/base.py:send_image</c>),
/// but the http(s) path is kept for forward-compatibility since nothing in this
/// codebase should assume agent images are *always* one specific form. Any other
/// scheme (<c>file:</c>, <c>javascript:</c>, <c>data:text/html</c>, ...) is rejected.
/// </summary>
public static class AttachmentImageCacheService
{
    private const long MaxImageBytes = 50 * 1024 * 1024;
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(15);

    public static async Task<(string LocalPath, string MimeType)?> ResolveAndCacheAsync(
        string source, HttpClient http, CancellationToken ct = default)
    {
        byte[] bytes;
        string mime;

        switch (DataUrlCodec.ClassifyImageSource(source))
        {
            case ImageSourceKind.DataUrl:
                if (!DataUrlCodec.TryDecode(source, out mime, out bytes)) return null;
                if (!IsImageMime(mime)) return null;
                break;

            case ImageSourceKind.HttpUrl:
                var downloaded = await TryDownloadAsync(source, http, ct).ConfigureAwait(false);
                if (downloaded is null) return null;
                (bytes, mime) = downloaded.Value;
                break;

            default:
                return null;
        }

        if (bytes.Length == 0 || bytes.Length > MaxImageBytes) return null;

        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var path = Path.Combine(AppStorage.ImageCacheDir, hash + ExtensionFor(mime));

        if (!File.Exists(path))
        {
            Directory.CreateDirectory(AppStorage.ImageCacheDir);
            await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
        }

        return (path, mime);
    }

    private static async Task<(byte[] Bytes, string Mime)?> TryDownloadAsync(string url, HttpClient http, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(DownloadTimeout);
            using var response = await http.GetAsync(url, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            var mime = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!IsImageMime(mime)) return null;

            var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
            return (bytes, mime);
        }
        catch
        {
            // Timeout, DNS failure, non-2xx, etc. — treat as "couldn't load", not a crash.
            return null;
        }
    }

    private static bool IsImageMime(string mime)
        => mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    private static string ExtensionFor(string mime) => mime.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        _ => ".bin",
    };
}
