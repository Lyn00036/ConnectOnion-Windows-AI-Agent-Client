using System.Collections.Generic;

namespace ConnectOnion.WinUIClient.Models;

/// <summary>A titled section of related shortcuts (e.g. "Chat"), as shown in the dialog.</summary>
public sealed class KeyboardShortcutGroup
{
    public KeyboardShortcutGroup()
    {
    }

    public KeyboardShortcutGroup(string title, IReadOnlyList<KeyboardShortcutItem> shortcuts)
    {
        Title = title;
        Shortcuts = shortcuts;
    }

    public string Title { get; set; } = "";

    public IReadOnlyList<KeyboardShortcutItem> Shortcuts { get; set; } = new List<KeyboardShortcutItem>();
}
