using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ConnectOnion.WinUIClient.Models;

public enum ThemeMode
{
    Light,
    Dark,
}

public enum MessageFontSize
{
    Sm,
    Md,
    Lg,
}

/// <summary>
/// Desktop preferences (theme, sidebar, message font size, shortcut overrides).
/// Mirrors the TypeScript <c>PreferencesSnapshot</c> type.
/// </summary>
public sealed class PreferencesSnapshot
{
    [JsonPropertyName("theme")]
    public ThemeMode Theme { get; set; } = ThemeMode.Light;

    [JsonPropertyName("sidebarVisible")]
    public bool SidebarVisible { get; set; } = true;

    [JsonPropertyName("messageFontSize")]
    public MessageFontSize MessageFontSize { get; set; } = MessageFontSize.Md;

    [JsonPropertyName("shortcutOverrides")]
    public Dictionary<string, string> ShortcutOverrides { get; set; } = new();
}
