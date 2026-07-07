using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ConnectOnion.WinUIClient.Models;

public enum ThemeMode
{
    System,
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
    private const string ComposerEnterKey = "composer.enterKey";
    private const string EnterNewLineValue = "newline";

    [JsonPropertyName("theme")]
    public ThemeMode Theme { get; set; } = ThemeMode.System;

    [JsonPropertyName("sidebarVisible")]
    public bool SidebarVisible { get; set; } = true;

    [JsonPropertyName("messageFontSize")]
    public MessageFontSize MessageFontSize { get; set; } = MessageFontSize.Md;

    [JsonPropertyName("shortcutOverrides")]
    public Dictionary<string, string> ShortcutOverrides { get; set; } = new();

    /// <summary>Selected capture device id, or empty for system default.</summary>
    [JsonPropertyName("microphoneDeviceId")]
    public string MicrophoneDeviceId { get; set; } = "";

    [JsonIgnore]
    public bool EnterToSend
    {
        get => !ShortcutOverrides.TryGetValue(ComposerEnterKey, out var value)
               || !string.Equals(value, EnterNewLineValue, System.StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                ShortcutOverrides.Remove(ComposerEnterKey);
                return;
            }

            ShortcutOverrides[ComposerEnterKey] = EnterNewLineValue;
        }
    }
}
