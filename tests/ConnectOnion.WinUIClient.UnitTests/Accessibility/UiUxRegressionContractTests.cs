using System.Xml.Linq;

namespace ConnectOnion.WinUIClient.UnitTests.Accessibility;

public sealed class UiUxRegressionContractTests
{
    [Fact]
    public void Composer_OnboardingAndFooter_AdaptInsteadOfDisappearingOrOverflowing()
    {
        var xaml = Read("ConnectOnion.WinUIClient", "Controls", "Chat", "ChatComposer.xaml");
        var code = Read("ConnectOnion.WinUIClient", "Controls", "Chat", "ChatComposer.xaml.cs");

        Assert.Contains("x:Name=\"SuggestionScroller\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollMode=\"Enabled\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ComposerFooter\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Summarize your capabilities", xaml, StringComparison.Ordinal);
        Assert.Contains("Help me get started", xaml, StringComparison.Ordinal);
        Assert.Contains("Suggest three useful tasks", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("What can you do?", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("width >= 620", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Math.Max(280", code, StringComparison.Ordinal);
        Assert.Contains("ModeLabel.Visibility = isNarrow", code, StringComparison.Ordinal);
        Assert.Contains("ComposerFooter.ColumnSpacing = isNarrow", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Sidebar_DisclosuresNameTheirTargetAndExposeUsablePointerTargets()
    {
        var styles = XDocument.Load(PathFor(
            "ConnectOnion.WinUIClient", "Styles", "ControlStyles.xaml"));
        var sidebar = Read("ConnectOnion.WinUIClient", "Controls", "Shell", "ShellSidebar.xaml");
        var item = Read("ConnectOnion.WinUIClient", "Models", "ShellAgentItem.cs");
        var chat = Read("ConnectOnion.WinUIClient", "Views", "ChatPage.xaml");

        var smallStyle = styles.Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value
                    == "SmallIconButtonStyle");
        Assert.Contains(smallStyle.Elements(), element =>
            element.Attribute("Property")?.Value == "Width"
            && element.Attribute("Value")?.Value == "36");
        Assert.Contains(smallStyle.Elements(), element =>
            element.Attribute("Property")?.Value == "Height"
            && element.Attribute("Value")?.Value == "36");

        // Both disclosures name their target. Compiled bindings (the sidebar is x:Bind throughout,
        // for the per-row cost), so the mode is part of the markup being asserted: these names
        // change with expansion state and would go stale under x:Bind's OneTime default.
        Assert.Contains("AutomationProperties.Name=\"{x:Bind ToggleAccessibilityName, Mode=OneWay}\"", sidebar, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{x:Bind PinnedToggleAccessibilityName, Mode=OneWay}\"", sidebar, StringComparison.Ordinal);
        Assert.Contains("Text=\"Agent library\"", sidebar, StringComparison.Ordinal);
        Assert.Contains("Text=\"Pinned shortcuts\"", sidebar, StringComparison.Ordinal);
        // No "By agent" heading is asserted, and its absence is the point: every group in the tree
        // is headed by an agent's own avatar and name, so the label restated the structure it sat
        // on and spent a row of the sidebar doing it.
        Assert.DoesNotContain("Text=\"By agent\"", sidebar, StringComparison.Ordinal);
        Assert.Contains("Collapse chats for {0}", item, StringComparison.Ordinal);
        Assert.Contains("Expand chats for {0}", item, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Save a copy\"", chat, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_SearchScopeAndFailures_AreVisibleToTheUser()
    {
        var overlay = Read("ConnectOnion.WinUIClient", "Controls", "Settings", "SettingsOverlay.xaml.cs");
        var settingsXaml = Read("ConnectOnion.WinUIClient", "Views", "SettingsPage.xaml");
        var settingsCode = Read("ConnectOnion.WinUIClient", "Views", "SettingsPage.xaml.cs");
        var usageXaml = Read("ConnectOnion.WinUIClient", "Controls", "Settings", "UsageSettingsContent.xaml");
        var usageCode = Read("ConnectOnion.WinUIClient", "Controls", "Settings", "UsageSettingsContent.xaml.cs");

        Assert.Contains("SearchBox.Visibility = isSettingsPage", overlay, StringComparison.Ordinal);
        Assert.Contains("SettingsSearchResultsHeading", overlay, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SearchEmptyState\"", settingsXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SettingsErrorBar\"", settingsXaml, StringComparison.Ordinal);
        Assert.Contains("SaveSettingAsync", settingsCode, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"UsageErrorBar\"", usageXaml, StringComparison.Ordinal);
        Assert.Contains("UsageErrorBar.IsOpen = true", usageCode, StringComparison.Ordinal);
        Assert.Contains("await Vm.SetRangeAsync(range)", usageCode, StringComparison.Ordinal);
        Assert.DoesNotContain("catch { /* the panel is informational", usageCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_TitleBarHasACompactState_AndModeShortcutIsLocalized()
    {
        var window = Read("ConnectOnion.WinUIClient", "MainWindow.xaml.cs");
        var localizer = Read("ConnectOnion.WinUIClient", "Common", "KeyboardTextLocalizer.cs");
        var chinese = XDocument.Load(PathFor(
            "ConnectOnion.WinUIClient", "Strings", "zh-CN", "Resources.resw"));

        Assert.Contains("UpdateTitleBarLayout(windowWidth)", window, StringComparison.Ordinal);
        Assert.Contains("windowWidth < 720", window, StringComparison.Ordinal);
        Assert.Contains("[\"Cycle approval mode\"] = \"KeyboardCycleChatMode\"", localizer, StringComparison.Ordinal);
        Assert.Contains(chinese.Descendants("data"), element =>
            element.Attribute("name")?.Value == "KeyboardCycleChatMode"
            && !string.IsNullOrWhiteSpace(element.Element("value")?.Value));
    }

    private static string Read(params string[] parts) => File.ReadAllText(PathFor(parts));

    private static string PathFor(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
