using ConnectOnion.WinUIClient.Diagnostics;

namespace ConnectOnion.WinUIClient.UnitTests.Diagnostics;

public sealed class AtomicTextFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "ConnectOnion-AtomicTextFileTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WriteAllText_RetriesWhileReaderPreventsAtomicReplacement()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "startup.json");
        await File.WriteAllTextAsync(path, "old");

        var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var write = Task.Run(() => AtomicTextFile.WriteAllText(
            path,
            "new",
            maxAttempts: 100,
            retryDelayMilliseconds: 10));

        await Task.Delay(75);
        Assert.False(write.IsCompleted);
        await reader.DisposeAsync();
        await write;

        Assert.Equal("new", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
