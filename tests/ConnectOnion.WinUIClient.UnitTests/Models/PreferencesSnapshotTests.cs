using ConnectOnion.WinUIClient.Models;

namespace ConnectOnion.WinUIClient.UnitTests.Models;

public sealed class PreferencesSnapshotTests
{
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
}
