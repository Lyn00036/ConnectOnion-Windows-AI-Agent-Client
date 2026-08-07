namespace ConnectOnion.WinUIClient.UnitTests.Architecture;

public sealed class PortableLauncherContractTests
{
    private static string RepoFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);

        var path = Path.Combine(new[] { directory!.FullName }.Concat(relativeParts).ToArray());
        Assert.True(File.Exists(path), $"Expected to find {path}.");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Launcher_IsDependencyFreeNativeAotAndUsesTheProductIcon()
    {
        var project = RepoFile("ConnectOnion.PortableLauncher", "ConnectOnion.PortableLauncher.csproj");

        Assert.Contains("<OutputType>WinExe</OutputType>", project, StringComparison.Ordinal);
        Assert.Contains("<PublishAot>true</PublishAot>", project, StringComparison.Ordinal);
        Assert.Contains("<SelfContained>true</SelfContained>", project, StringComparison.Ordinal);
        Assert.Contains("<PublishSingleFile>true</PublishSingleFile>", project, StringComparison.Ordinal);
        Assert.Contains("<ApplicationIcon>..\\ConnectOnion.WinUIClient\\Assets\\app-icon.ico</ApplicationIcon>", project, StringComparison.Ordinal);
    }

    [Fact]
    public void Launcher_OnlyStartsTheFixedNestedApplicationAndForwardsArguments()
    {
        var program = RepoFile("ConnectOnion.PortableLauncher", "Program.cs");

        Assert.Contains("app\\ConnectOnion.WinUIClient.exe", program, StringComparison.Ordinal);
        Assert.Contains("Path.GetFullPath", program, StringComparison.Ordinal);
        Assert.Contains("File.Exists(applicationPath)", program, StringComparison.Ordinal);
        Assert.Contains("startInfo.ArgumentList.Add(argument)", program, StringComparison.Ordinal);
        Assert.Contains("UseShellExecute = false", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Console.", program, StringComparison.Ordinal);
    }
}
