using System.Text.RegularExpressions;

namespace ConnectOnion.WinUIClient.UnitTests.Architecture;

/// <summary>
/// Keeps the last acceptance criterion of the trimming gate honest: <b>production and CI remain
/// explicitly untrimmed until the gate passes.</b>
///
/// <para>The 2026-07-25 release audit found a trim-only Tool Activity restoration failure — a
/// Release build that restored empty timelines while Debug worked perfectly. The repair is in,
/// and the smoke harness now proves the serialization paths survive a trimmed publish, but the
/// criteria that need a signed MSIX, a real window and a same-machine performance comparison are
/// not met yet (see <c>docs/TRIMMING.md</c>). Until they are, flipping <c>PublishTrimmed</c> back
/// on is a decision, not an edit — and this test is what makes it one, because the alternative is
/// a one-character change that nothing notices until a user's history renders blank.</para>
///
/// <para>Asserting on the file text rather than on an evaluated MSBuild property is deliberate:
/// the point is that the <i>declaration</i> stays explicit and commented, which a headless test
/// host cannot learn by evaluating the project.</para>
/// </summary>
public sealed class TrimmingGateTests
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
    public void ReleaseConfiguration_IsExplicitlyUntrimmed()
    {
        var csproj = RepoFile("ConnectOnion.WinUIClient", "ConnectOnion.WinUIClient.csproj");

        var releaseSetting = Regex.Match(
            csproj,
            @"<PublishTrimmed Condition=""'\$\(Configuration\)' != 'Debug'"">(?<value>\w+)</PublishTrimmed>");

        Assert.True(releaseSetting.Success,
            "The Release PublishTrimmed declaration is gone. It must stay explicit — see docs/TRIMMING.md.");
        Assert.Equal("false", releaseSetting.Groups["value"].Value.ToLowerInvariant());
    }

    [Fact]
    public void ReleaseConfiguration_ExplainsWhyItIsUntrimmed()
    {
        var csproj = RepoFile("ConnectOnion.WinUIClient", "ConnectOnion.WinUIClient.csproj");

        // A bare `false` invites someone to "clean it up" as a leftover default. The comment is
        // what tells the next reader it is load-bearing, so it is part of the contract.
        Assert.Contains("docs/TRIMMING.md", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void Ci_PublishesAndBuildsUntrimmed()
    {
        var workflow = RepoFile(".github", "workflows", "ci.yml");

        var trimmedSettings = Regex.Matches(workflow, @"-p:PublishTrimmed=(?<value>\w+)");

        Assert.NotEmpty(trimmedSettings);
        Assert.All(trimmedSettings, match =>
            Assert.Equal("false", match.Groups["value"].Value.ToLowerInvariant()));
    }

    /// <summary>
    /// The harness only proves anything while the reflection fallback is off. If that property
    /// were dropped from the smoke project, every check in it would start passing vacuously — and
    /// a green audit would be worse than no audit.
    /// </summary>
    [Fact]
    public void SmokeHarness_KeepsTheReflectionFallbackDisabled()
    {
        var csproj = RepoFile("tests", "ConnectOnion.TrimSmoke", "ConnectOnion.TrimSmoke.csproj");

        Assert.Contains(
            "<JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>",
            csproj,
            StringComparison.Ordinal);
        Assert.Contains("<PublishTrimmed>true</PublishTrimmed>", csproj, StringComparison.Ordinal);
    }
}
