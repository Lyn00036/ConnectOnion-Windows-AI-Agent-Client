namespace ConnectOnion.WinUIClient.UnitTests.Architecture;

public sealed class PopupThemeContractTests
{
    [Fact]
    public void ContentDialogs_UseTheSharedThemedShowPath()
    {
        var appRoot = FindAppRoot();
        var offenders = Directory
            .EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(
                "ContentDialogExtensions.cs",
                StringComparison.OrdinalIgnoreCase))
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains("dialog.ShowAsync(", StringComparison.Ordinal)
                    || source.Contains("confirmation.ShowAsync(", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(appRoot, path))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ThemedShowPath_AppliesAndTracksTheWindowTheme()
    {
        var appRoot = FindAppRoot();
        var extension = File.ReadAllText(Path.Combine(
            appRoot,
            "Common",
            "ContentDialogExtensions.cs"));
        var themeService = File.ReadAllText(Path.Combine(
            appRoot,
            "Services",
            "ThemeService.cs"));

        Assert.Contains(
            "ApplyTheme(ThemeService.CurrentTheme);",
            extension,
            StringComparison.Ordinal);
        Assert.Contains(
            "ThemeService.ThemeApplied += ApplyTheme;",
            extension,
            StringComparison.Ordinal);
        Assert.Contains(
            "ThemeService.ThemeApplied -= ApplyTheme;",
            extension,
            StringComparison.Ordinal);
        Assert.Contains(
            "public static ElementTheme CurrentTheme => _currentTheme;",
            themeService,
            StringComparison.Ordinal);
        Assert.Contains(
            "PublishTheme(requestedTheme == ElementTheme.Default",
            themeService,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AccountMenu_StaysInTheSidebarVisualTreeWithoutCreatingAPopup()
    {
        var appRoot = FindAppRoot();
        var xaml = File.ReadAllText(Path.Combine(
            appRoot,
            "Controls",
            "Shell",
            "ShellSidebar.xaml"));
        var source = File.ReadAllText(Path.Combine(
            appRoot,
            "Controls",
            "Shell",
            "ShellSidebar.Events.cs"));

        Assert.DoesNotContain(
            "x:Name=\"AccountFlyout\"",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AccountFlyout_Opening",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"AccountMenuPanel\"",
            xaml,
            StringComparison.Ordinal);
        var accountPanelStart = xaml.IndexOf(
            "x:Name=\"AccountMenuPanel\"",
            StringComparison.Ordinal);
        Assert.True(accountPanelStart >= 0);
        var accountPanelXaml = xaml[accountPanelStart..];
        Assert.DoesNotContain(
            "MinWidth=\"272\"",
            accountPanelXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "HorizontalAlignment=\"Stretch\"",
            accountPanelXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"AccountMenuDismissLayer\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Click=\"BottomAccount_Click\"",
            xaml,
            StringComparison.Ordinal);
        var accountButtonStart = xaml.IndexOf(
            "x:Name=\"BottomAccountButton\"",
            StringComparison.Ordinal);
        Assert.True(accountButtonStart >= 0);
        Assert.DoesNotContain(
            "<Button.Flyout>",
            xaml[accountButtonStart..],
            StringComparison.Ordinal);
        Assert.Contains(
            "AccountMenuPanel.Visibility = Visibility.Visible;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "AccountMenuPanel.Visibility = Visibility.Collapsed;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "KeyDown=\"AccountMenuPanel_KeyDown\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "CloseAccountMenu();",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "x:Uid=\"SidebarTheme\"",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "BottomTheme_Click",
            source,
            StringComparison.Ordinal);
    }

    private static string FindAppRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var root = Path.Combine(directory.FullName, "ConnectOnion.WinUIClient");
            if (Directory.Exists(root))
                return root;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the WinUI app source directory.");
    }
}
