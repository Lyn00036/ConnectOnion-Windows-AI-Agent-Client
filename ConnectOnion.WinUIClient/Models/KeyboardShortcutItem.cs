using System;
using System.Collections.Generic;
using System.Linq;

namespace ConnectOnion.WinUIClient.Models;

/// <summary>
/// One labelled action and the key combination(s) that trigger it. A row may carry several
/// equivalent bindings (e.g. Ctrl+W and Esc), shown side by side.
/// </summary>
public sealed class KeyboardShortcutItem
{
    public string Name { get; set; } = "";

    public IReadOnlyList<KeyBinding> KeyBindings { get; set; } = new List<KeyBinding>();

    /// <summary>Extra terms folded into search beyond the name and key text (optional).</summary>
    public IReadOnlyList<string> SearchKeywords { get; set; } = new List<string>();

    /// <summary>Full spoken form of every binding ("Ctrl + N, or Esc"), set as the keycap
    /// group's AutomationProperties.Name so a screen reader reads the whole combo — the keycaps
    /// themselves are hidden from the accessibility tree.</summary>
    public string KeysAccessibleText => string.Join(", or ", KeyBindings.Select(b => b.DisplayText));

    /// <summary>Case-insensitive match across the action name, its key combinations (both the
    /// individual keys and the flat "Ctrl + N" form), and any extra keywords.</summary>
    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;

        if (Name.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;

        if (KeyBindings.Any(b =>
                b.DisplayText.Contains(query, StringComparison.OrdinalIgnoreCase)
                || b.Keys.Any(k => k.Contains(query, StringComparison.OrdinalIgnoreCase))))
        {
            return true;
        }

        return SearchKeywords.Any(k => k.Contains(query, StringComparison.OrdinalIgnoreCase));
    }
}
