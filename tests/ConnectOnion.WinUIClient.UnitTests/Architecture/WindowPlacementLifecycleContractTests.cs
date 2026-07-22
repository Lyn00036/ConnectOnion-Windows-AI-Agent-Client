namespace ConnectOnion.WinUIClient.UnitTests.Architecture;

public sealed class WindowPlacementLifecycleContractTests
{
    [Fact]
    public void SavePath_DoesNotResolveServiceFromPossiblyDisposedProvider()
    {
        var source = ReadAppSource("Shell", "MainWindow.Placement.cs");

        Assert.DoesNotContain("AppServices.WindowPlacement", source, StringComparison.Ordinal);
        Assert.Contains("_windowPlacementStore.SaveAsync", source, StringComparison.Ordinal);
        Assert.Contains("_windowPlacementSaved", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitExit_SavesPlacementBeforeHostShutdown()
    {
        var source = ReadAppSource("Shell", "MainWindow.Tray.cs");
        var save = source.IndexOf("await SaveWindowPlacementAsync()", StringComparison.Ordinal);
        var shutdown = source.IndexOf("await ((App)Application.Current).ShutdownAsync()", StringComparison.Ordinal);

        Assert.True(save >= 0, "Explicit exit does not save window placement.");
        Assert.True(shutdown > save, "Host shutdown must happen after window placement is saved.");
    }

    [Fact]
    public void Placement_RestoresAndPersistsMaximizedState()
    {
        var source = ReadAppSource("Shell", "MainWindow.Placement.cs");

        Assert.Contains("presenter.Maximize()", source, StringComparison.Ordinal);
        Assert.Contains("OverlappedPresenterState.Maximized", source, StringComparison.Ordinal);
        Assert.Contains("new WindowPlacement(position, isMaximized)", source, StringComparison.Ordinal);
    }

    private static string ReadAppSource(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var root = Path.Combine(directory.FullName, "ConnectOnion.WinUIClient");
            if (Directory.Exists(root))
                return File.ReadAllText(Path.Combine([root, .. relativeParts]));
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the WinUI app source directory.");
    }
}
