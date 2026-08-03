using ConnectOnion.WinUIClient.Models;

namespace ConnectOnion.WinUIClient.UnitTests.Models;

public sealed class PreferencesSnapshotTests
{
    [Fact]
    public void InterfaceTextSize_DefaultsToMediumWithoutStoredOverride()
    {
        var preferences = new PreferencesSnapshot();

        Assert.Equal(InterfaceTextSize.Medium, preferences.InterfaceTextSize);
        Assert.False(preferences.ShortcutOverrides.ContainsKey("ui.textSize"));
    }

    [Theory]
    [InlineData(InterfaceTextSize.Small, "Small")]
    [InlineData(InterfaceTextSize.Medium, null)]
    [InlineData(InterfaceTextSize.Large, "Large")]
    public void InterfaceTextSize_RoundTripsThroughSharedPreferenceMap(
        InterfaceTextSize size,
        string? storedValue)
    {
        var preferences = new PreferencesSnapshot();

        preferences.InterfaceTextSize = size;

        Assert.Equal(size, preferences.InterfaceTextSize);
        if (storedValue is null)
            Assert.False(preferences.ShortcutOverrides.ContainsKey("ui.textSize"));
        else
            Assert.Equal(storedValue, preferences.ShortcutOverrides["ui.textSize"]);
    }

    [Fact]
    public void InterfaceTextSize_UnknownStoredValue_FallsBackToMedium()
    {
        var preferences = new PreferencesSnapshot
        {
            ShortcutOverrides = new Dictionary<string, string>
            {
                ["ui.textSize"] = "future-value",
            },
        };

        Assert.Equal(InterfaceTextSize.Medium, preferences.InterfaceTextSize);
    }

    [Fact]
    public void EnterToSend_Disabled_SetsNewlineOverride()
    {
        var preferences = new PreferencesSnapshot();

        preferences.EnterToSend = false;

        Assert.False(preferences.EnterToSend);
        Assert.Equal("newline", preferences.ShortcutOverrides["composer.enterKey"]);
    }

    [Fact]
    public void EnterToSend_Reenabled_RemovesOnlyComposerOverride()
    {
        var preferences = new PreferencesSnapshot
        {
            ShortcutOverrides = new Dictionary<string, string>
            {
                ["composer.enterKey"] = "newline",
                ["other"] = "keep",
            },
        };

        preferences.EnterToSend = true;

        Assert.True(preferences.EnterToSend);
        Assert.False(preferences.ShortcutOverrides.ContainsKey("composer.enterKey"));
        Assert.Equal("keep", preferences.ShortcutOverrides["other"]);
    }

    [Theory]
    [InlineData(WindowCloseBehavior.Ask, null)]
    [InlineData(WindowCloseBehavior.HideToTray, "tray")]
    [InlineData(WindowCloseBehavior.Exit, "exit")]
    public void CloseBehavior_RoundTripsThroughSharedPreferenceMap(
        WindowCloseBehavior behavior,
        string? storedValue)
    {
        var preferences = new PreferencesSnapshot
        {
            ShortcutOverrides = new Dictionary<string, string>
            {
                ["other"] = "keep",
            },
        };

        preferences.CloseBehavior = behavior;

        Assert.Equal(behavior, preferences.CloseBehavior);
        Assert.Equal("keep", preferences.ShortcutOverrides["other"]);
        if (storedValue is null)
            Assert.False(preferences.ShortcutOverrides.ContainsKey("window.closeBehavior"));
        else
            Assert.Equal(storedValue, preferences.ShortcutOverrides["window.closeBehavior"]);
    }

    [Fact]
    public void CloseBehavior_UnknownStoredValue_FallsBackToAsk()
    {
        var preferences = new PreferencesSnapshot
        {
            ShortcutOverrides = new Dictionary<string, string>
            {
                ["window.closeBehavior"] = "future-value",
            },
        };

        Assert.Equal(WindowCloseBehavior.Ask, preferences.CloseBehavior);
    }
}
