using Windows.ApplicationModel.DataTransfer;

namespace ConnectOnion.WinUIClient.Services;

/// <summary>
/// Small wrapper over the WinRT clipboard so the
/// <c>new DataPackage()</c> → <c>SetText</c> → <c>Clipboard.SetContent</c>
/// dance lives in one place instead of being copy-pasted per call site.
/// </summary>
public static class ClipboardService
{
    public static void CopyText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}
