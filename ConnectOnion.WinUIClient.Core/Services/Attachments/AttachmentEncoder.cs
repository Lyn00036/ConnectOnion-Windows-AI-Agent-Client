using ConnectOnion.Protocol;

namespace ConnectOnion.WinUIClient.Services.Attachments;

/// <summary>
/// Encodes a validated attachment file into the wire's <c>data:</c> URL format.
/// Runs off the UI thread: <c>File.ReadAllBytesAsync</c> is genuinely async I/O,
/// and the subsequent base64 encode continues on a thread-pool thread because the
/// await isn't UI-context-captured here (no XAML dispatcher involved in this call
/// chain). Callers should invoke this right after a <c>PendingAttachment</c> passes
/// validation and cache the result on <see cref="Models.PendingAttachment.DataUrl"/>
/// so Send never re-reads or re-encodes the same file.
/// </summary>
public static class AttachmentEncoder
{
    public static async Task<string> EncodeToDataUrlAsync(string localPath, string mimeType, CancellationToken ct = default)
    {
        var bytes = await File.ReadAllBytesAsync(localPath, ct).ConfigureAwait(false);
        return DataUrlCodec.Encode(mimeType, bytes);
    }
}
