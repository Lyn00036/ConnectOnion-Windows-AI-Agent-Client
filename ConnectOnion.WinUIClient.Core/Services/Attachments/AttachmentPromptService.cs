using System.Collections.Generic;
using System.Linq;
using ConnectOnion.WinUIClient.Models;

namespace ConnectOnion.WinUIClient.Services.Attachments;

/// <summary>
/// Supplies a small, visible prompt for attachment-only submissions. The composer deliberately
/// allows a user to send an image or file without typing, while ConnectOnion hosts still require
/// a non-empty prompt on INPUT. Keeping the fallback here makes that contract explicit and keeps
/// the optimistic user bubble, persisted message, session title, and wire prompt identical.
/// </summary>
public static class AttachmentPromptService
{
    public static string Resolve(
        string? prompt,
        IReadOnlyList<PendingAttachment>? attachments)
    {
        var trimmed = prompt?.Trim() ?? "";
        if (trimmed.Length > 0 || attachments is not { Count: > 0 }) return trimmed;

        var imageCount = attachments.Count(attachment => attachment.Kind == AttachmentKind.Image);
        var fileCount = attachments.Count - imageCount;

        if (imageCount > 0 && fileCount > 0)
        {
            return $"Briefly describe the attached {Noun(imageCount, "image")} and summarize the attached {Noun(fileCount, "file")}.";
        }

        if (imageCount > 0)
        {
            return $"Briefly describe the attached {Noun(imageCount, "image")}.";
        }

        return $"Briefly summarize the attached {Noun(fileCount, "file")}.";
    }

    private static string Noun(int count, string singular)
        => count == 1 ? singular : $"{singular}s";
}
