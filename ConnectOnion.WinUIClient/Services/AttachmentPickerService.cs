using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Models;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace ConnectOnion.WinUIClient.Services;

/// <summary>
/// Wraps <see cref="FileOpenPicker"/> for attachment selection. HWND init follows
/// the same pattern already used for the folder picker in
/// <c>MainWindow.FileMenu.cs</c>'s <c>PickAndOpenFolderAsync</c>, but sourced from
/// <see cref="App.MainWindow"/> since the composer button lives inside a
/// <see cref="Microsoft.UI.Xaml.Controls.UserControl"/> (no HWND of its own).
/// </summary>
public static class AttachmentPickerService
{
    /// <summary>
    /// Opens a multi-select file picker filtered to the given attachment kind.
    /// Returns an empty list if the user cancels the picker — that is a normal
    /// outcome, not an error, and callers must not treat it as a failure.
    /// Only reads file metadata (name, size, content type) here; no file content
    /// is read until <c>AttachmentEncoder</c> runs after validation passes.
    /// </summary>
    public static async Task<IReadOnlyList<PendingAttachment>> PickAsync(AttachmentKind kind, CancellationToken ct = default)
    {
        if (App.MainWindow is null) return Array.Empty<PendingAttachment>();

        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));

        picker.ViewMode = PickerViewMode.List;
        picker.SuggestedStartLocation = PickerLocationId.Downloads;
        foreach (var ext in kind == AttachmentKind.Image
                     ? AttachmentValidationService.ImageExtensions
                     : AttachmentValidationService.FileExtensions)
        {
            picker.FileTypeFilter.Add(ext);
        }

        IReadOnlyList<StorageFile>? files;
        try
        {
            files = await picker.PickMultipleFilesAsync().AsTask(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Array.Empty<PendingAttachment>();
        }

        if (files is null || files.Count == 0) return Array.Empty<PendingAttachment>();

        var results = new List<PendingAttachment>(files.Count);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            long sizeBytes;
            try
            {
                var props = await file.GetBasicPropertiesAsync().AsTask(ct).ConfigureAwait(false);
                sizeBytes = (long)props.Size;
            }
            catch
            {
                // Metadata read failed (locked/removed file) — skip it rather
                // than fail the whole batch pick.
                continue;
            }

            results.Add(new PendingAttachment
            {
                Kind = kind,
                FileName = file.Name,
                LocalPath = file.Path,
                MimeType = MimeTypeResolver.Resolve(file.ContentType, file.Name),
                SizeBytes = sizeBytes,
            });
        }
        return results;
    }
}
