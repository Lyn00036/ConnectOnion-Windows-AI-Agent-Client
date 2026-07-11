using System.Collections.Generic;
using ConnectOnion.WinUIClient.Models;

namespace ConnectOnion.WinUIClient.Services;

/// <summary>
/// The single source of truth for the shortcuts shown in the Keyboard shortcuts dialog. Every
/// entry here mirrors a shortcut that is actually handled elsewhere in the app — when you add,
/// remove, or re-key a shortcut in one of the places below, update the matching entry here so
/// the overview never drifts from real behavior:
///
///   • File / Edit / View groups → the KeyDown handlers in <c>MainWindow.FileMenu.cs</c>,
///     <c>MainWindow.EditMenu.cs</c>, <c>MainWindow.ViewMenu.cs</c>, and the MenuBar's
///     <c>KeyboardAcceleratorTextOverride</c> strings in <c>MainWindow.xaml</c>.
///   • Chat group  → <c>Controls/ChatComposer.xaml.cs</c> <c>InputBox_PreviewKeyDown</c>
///     (Enter submits, Shift+Enter inserts a newline).
///   • Find group  → <c>MainWindow.ViewMenu.cs</c> <c>FindTextBox_KeyDown</c> / <c>FindCommand</c>
///     and <c>Views/ChatPage.Find.cs</c>.
///   • General "Close dialog" (Esc) → the Esc handling shared by SettingsOverlay,
///     KeyboardShortcutsDialog, and the find overlay.
///
/// Do not list a shortcut here that is not actually handled in code.
/// </summary>
public static class KeyboardShortcutCatalog
{
    public static IReadOnlyList<KeyboardShortcutGroup> GetGroups() => new List<KeyboardShortcutGroup>
    {
        new("File", new List<KeyboardShortcutItem>
        {
            Item("New chat", Combo("Ctrl", "N")),
            Item("Open folder", Combo("Ctrl", "O")),
            Item("Open settings", Combo("Ctrl", ",")),
            Item("Close window", Combo("Ctrl", "W")),
            Item("Exit application", Combo("Ctrl", "Q")),
        }),
        new("Edit", new List<KeyboardShortcutItem>
        {
            Item("Undo", Combo("Ctrl", "Z")),
            Item("Redo", Combo("Ctrl", "Y")),
            Item("Cut", Combo("Ctrl", "X")),
            Item("Copy", Combo("Ctrl", "C")),
            Item("Paste", Combo("Ctrl", "V")),
            Item("Select all", Combo("Ctrl", "A")),
        }),
        new("View", new List<KeyboardShortcutItem>
        {
            Item("Toggle sidebar", Combo("Ctrl", "B")),
            Item("Open terminal", Combo("Ctrl", "`")),
            Item("Find", Combo("Ctrl", "F")),
            Item("Go back", Combo("Ctrl", "[")),
            Item("Go forward", Combo("Ctrl", "]")),
            Item("Zoom in", Combo("Ctrl", "Shift", "=")),
            Item("Zoom out", Combo("Ctrl", "-")),
            Item("Toggle full screen", Combo("F11")),
        }),
        new("Chat", new List<KeyboardShortcutItem>
        {
            Item("Send message", Combo("Enter")),
            Item("Insert new line", Combo("Shift", "Enter")),
        }),
        new("Find", new List<KeyboardShortcutItem>
        {
            Item("Next match", Combo("Enter")),
            Item("Previous match", Combo("Shift", "Enter")),
            Item("Close find", Combo("Esc")),
        }),
        new("General", new List<KeyboardShortcutItem>
        {
            Item("Close dialog", Combo("Esc")),
        }),
    };

    private static KeyboardShortcutItem Item(string name, params KeyBinding[] bindings)
        => new() { Name = name, KeyBindings = bindings };

    private static KeyBinding Combo(params string[] keys) => new(keys);
}
