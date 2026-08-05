using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConnectOnion.Protocol;

namespace ConnectOnion.WinUIClient.Services.Attachments;

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
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(15);

    public static async Task<(string LocalPath, string MimeType)?> ResolveAndCacheAsync(
        string source, HttpClient http, CancellationToken ct = default)
    {
        switch (DataUrlCodec.ClassifyImageSource(source))
        {
            case ImageSourceKind.DataUrl:
                byte[] bytes;
                string mime;
                if (!DataUrlCodec.TryDecode(source, out mime, out bytes)) return null;
                if (!IsImageMime(mime)) return null;
                var localPath = await ImageContentStore
                    .StoreBytesAsync(bytes, mime, ct)
                    .ConfigureAwait(false);
                return localPath is null ? null : (localPath, mime);

            case ImageSourceKind.HttpUrl:
                return await TryDownloadAsync(source, http, ct).ConfigureAwait(false);

            default:
                return null;
        }
    }

    private static async Task<(string LocalPath, string MimeType)?> TryDownloadAsync(
        string url, HttpClient http, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(DownloadTimeout);
            using var response = await http.GetAsync(
                url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            var mime = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!IsImageMime(mime)) return null;
            if (response.Content.Headers.ContentLength is > ImageContentStore.MaxImageBytes)
                return null;

            await using var stream = await response.Content
                .ReadAsStreamAsync(cts.Token)
                .ConfigureAwait(false);
            var path = await ImageContentStore
                .StoreStreamAsync(stream, mime, ImageContentStore.MaxImageBytes, cts.Token)
                .ConfigureAwait(false);
            return path is null ? null : (path, mime);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Timeout, DNS failure, non-2xx, etc. — treat as "couldn't load", not a crash.
            return null;
        }
    }

    private static bool IsImageMime(string mime)
        => mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

}
