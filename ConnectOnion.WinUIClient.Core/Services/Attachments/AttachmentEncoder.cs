using ConnectOnion.Protocol;
using System.Buffers;
using System.Text;

namespace ConnectOnion.WinUIClient.Services.Attachments;

/// <summary>
/// Encodes a validated attachment file into the wire's <c>data:</c> URL format.
/// Reads in aligned pooled chunks so a large send never holds the complete raw file and its
/// Base64 representation at the same time. Encoding is deliberately send-time only.
/// </summary>
public static class AttachmentEncoder
{
    public static async Task<string> EncodeToDataUrlAsync(string localPath, string mimeType, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        const int chunkSize = 48 * 1024; // divisible by three: chunk boundaries preserve Base64.
        var prefix = $"data:{mimeType};base64,";
        var fileLength = new FileInfo(localPath).Length;
        var estimatedLength = DataUrlCodec.EstimateEncodedLength(fileLength, mimeType);
        var builder = new StringBuilder(
            estimatedLength <= int.MaxValue ? (int)estimatedLength : prefix.Length);
        builder.Append(prefix);

        var buffer = ArrayPool<byte>.Shared.Rent(chunkSize);
        // Base64 of a 3-byte-aligned chunk is exactly 4/3 its length, so one rented buffer covers
        // every chunk. Encoding into it instead of calling Convert.ToBase64String means a 50 MB
        // send no longer allocates ~1000 intermediate 64 KB strings on the way to the builder.
        var encoded = ArrayPool<char>.Shared.Rent(((chunkSize + 2) / 3) * 4);
        try
        {
            await using var stream = new FileStream(
                localPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: chunkSize,
                useAsync: true);
            while (true)
            {
                var count = 0;
                while (count < chunkSize)
                {
                    var read = await stream.ReadAsync(
                            buffer.AsMemory(count, chunkSize - count), ct)
                        .ConfigureAwait(false);
                    if (read == 0) break;
                    count += read;
                }
                if (count == 0) break;
                if (!Convert.TryToBase64Chars(buffer.AsSpan(0, count), encoded, out var written))
                {
                    // Cannot happen with the sizing above; falling back rather than throwing keeps
                    // a send working if that arithmetic is ever changed incorrectly.
                    builder.Append(Convert.ToBase64String(buffer, 0, count));
                }
                else
                {
                    builder.Append(encoded, 0, written);
                }
                if (count < chunkSize) break;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            ArrayPool<char>.Shared.Return(encoded);
        }

        return builder.ToString();
    }
}
