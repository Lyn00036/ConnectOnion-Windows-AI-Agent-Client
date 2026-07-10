using System;
using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace ConnectOnion.WinUIClient;

public sealed partial class MainWindow
{
    private TextBox? _lastFocusedTextBox;

    private void RegisterEditMenuAccelerators()
    {
        RootGrid.GotFocus += EditTarget_GotFocus;
        RootGrid.KeyDown += EditMenuShortcut_KeyDown;
    }

    private void EditTarget_GotFocus(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox textBox)
        {
            _lastFocusedTextBox = textBox;
        }
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
        => UndoText();

    private void Redo_Click(object sender, RoutedEventArgs e)
        => RedoText();

    private void Cut_Click(object sender, RoutedEventArgs e)
        => CutText();

    private void Copy_Click(object sender, RoutedEventArgs e)
        => CopyText();

    private async void Paste_Click(object sender, RoutedEventArgs e)
        => await PasteTextAsync();

    private void Delete_Click(object sender, RoutedEventArgs e)
        => DeleteSelectedText();

    private void SelectAll_Click(object sender, RoutedEventArgs e)
        => SelectAllText();

    private async void EditMenuShortcut_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // TextBox natively handles Ctrl+C/V/X/Z/Y/A/Delete.  Don't double-fire
        // by routing the same key through our custom handler while the TextBox
        // (or a child of one) is focused.
        if (e.OriginalSource is TextBox or PasswordBox
            || (e.OriginalSource is FrameworkElement fe && FindParentTextBox(fe) is not null))
            return;

        var target = CurrentTextBoxTarget();
        if (target is null) return;

        var hasControl = IsKeyDown(VirtualKey.Control);
        var hasShift = IsKeyDown(VirtualKey.Shift);
        var hasAlt = IsKeyDown(VirtualKey.Menu);

        if (!hasAlt && !hasShift && hasControl)
        {
            switch (e.Key)
            {
                case VirtualKey.Z:
                    UndoText(target);
                    e.Handled = true;
                    return;
                case VirtualKey.Y:
                    RedoText(target);
                    e.Handled = true;
                    return;
                case VirtualKey.X:
                    CutText(target);
                    e.Handled = true;
                    return;
                case VirtualKey.C:
                    CopyText(target);
                    e.Handled = true;
                    return;
                case VirtualKey.V:
                    await PasteTextAsync(target);
                    e.Handled = true;
                    return;
                case VirtualKey.A:
                    SelectAllText(target);
                    e.Handled = true;
                    return;
            }
        }

        if (!hasControl && !hasShift && !hasAlt && e.Key == VirtualKey.Delete)
        {
            DeleteSelectedText(target);
            e.Handled = true;
        }
    }

    private TextBox? CurrentTextBoxTarget()
        => FocusManager.GetFocusedElement(RootGrid.XamlRoot) as TextBox ?? _lastFocusedTextBox;

    /// <summary>Walk up the visual tree to find a parent TextBox (useful when the
    /// event source is a child element like a Grid or Border inside a TextBox template).</summary>
    private static TextBox? FindParentTextBox(DependencyObject element)
    {
        while (element is not null)
        {
            if (element is TextBox tb) return tb;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    private void UndoText()
    {
        if (CurrentTextBoxTarget() is { } target)
            UndoText(target);
    }

    private static void UndoText(TextBox target)
    {
        if (target.CanUndo)
            target.Undo();
    }

    private void RedoText()
    {
        if (CurrentTextBoxTarget() is { } target)
            RedoText(target);
    }

    private static void RedoText(TextBox target)
    {
        target.Redo();
    }

    private void CutText()
    {
        if (CurrentTextBoxTarget() is { } target)
            CutText(target);
    }

    private static void CutText(TextBox target)
    {
        if (target.SelectionLength == 0) return;

        CopyText(target);
        DeleteSelectedText(target);
    }

    private void CopyText()
    {
        if (CurrentTextBoxTarget() is { } target)
            CopyText(target);
    }

    private static void CopyText(TextBox target)
        => ClipboardService.CopyText(target.SelectedText);

    private async Task PasteTextAsync()
    {
        if (CurrentTextBoxTarget() is { } target)
            await PasteTextAsync(target);
    }

    private static async Task PasteTextAsync(TextBox target)
    {
        var content = Clipboard.GetContent();
        if (!content.Contains(StandardDataFormats.Text)) return;

        var text = await content.GetTextAsync();
        ReplaceSelection(target, text);
    }

    private void DeleteSelectedText()
    {
        if (CurrentTextBoxTarget() is { } target)
            DeleteSelectedText(target);
    }

    private static void DeleteSelectedText(TextBox target)
    {
        if (target.SelectionLength > 0)
        {
            ReplaceSelection(target, string.Empty);
        }
    }

    private void SelectAllText()
    {
        if (CurrentTextBoxTarget() is { } target)
            SelectAllText(target);
    }

    private static void SelectAllText(TextBox target)
        => target.SelectAll();

    private static void ReplaceSelection(TextBox target, string text)
    {
        var start = target.SelectionStart;
        var length = target.SelectionLength;
        var value = target.Text ?? string.Empty;

        target.Text = value.Remove(start, length).Insert(start, text);
        target.SelectionStart = start + text.Length;
        target.SelectionLength = 0;
    }
}
