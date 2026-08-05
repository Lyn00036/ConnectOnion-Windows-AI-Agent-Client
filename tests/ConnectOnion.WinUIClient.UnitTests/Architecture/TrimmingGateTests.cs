using System.Text.RegularExpressions;

namespace ConnectOnion.WinUIClient.UnitTests.Architecture;

/// <summary>
/// Pins the trimming decision so that changing it stays a decision rather than an edit.
///
/// <para>Trimming was enabled for Release on 2026-08-05 (see <c>docs/TRIMMING.md</c> for the
/// evidence). This test used to assert the opposite — that production stayed untrimmed — and the
/// reason it still exists, pointed the other way, is unchanged: the 2026-07-25 audit found a
/// trim-only failure where a Release build restored empty Tool Activity timelines while Debug
/// worked perfectly. That class of bug is invisible in the inner loop and reaches a user as blank
/// history, so neither direction of this flag should be reachable by a one-character change that
/// nothing notices.</para>
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
    public void ReleaseConfiguration_IsExplicitlyTrimmed()
    {
        var csproj = RepoFile("ConnectOnion.WinUIClient", "ConnectOnion.WinUIClient.csproj");

        var releaseSetting = Regex.Match(
            csproj,
            @"<PublishTrimmed Condition=""'\$\(Configuration\)' != 'Debug'"">(?<value>\w+)</PublishTrimmed>");

        Assert.True(releaseSetting.Success,
            "The Release PublishTrimmed declaration is gone. It must stay explicit — see docs/TRIMMING.md.");
        Assert.Equal("true", releaseSetting.Groups["value"].Value.ToLowerInvariant());
    }

    /// <summary>
    /// Debug must stay untrimmed. Trimming it would put the inner loop behind a slow publish for
    /// no benefit, and — worse — would hide the very failure mode this gate exists for by making
    /// Debug and Release behave alike for the wrong reason.
    /// </summary>
    [Fact]
    public void DebugConfiguration_StaysUntrimmed()
    {
        var csproj = RepoFile("ConnectOnion.WinUIClient", "ConnectOnion.WinUIClient.csproj");

        var debugSetting = Regex.Match(
            csproj,
            @"<PublishTrimmed Condition=""'\$\(Configuration\)' == 'Debug'"">(?<value>\w+)</PublishTrimmed>");

        Assert.True(debugSetting.Success, "The Debug PublishTrimmed declaration is gone.");
        Assert.Equal("false", debugSetting.Groups["value"].Value.ToLowerInvariant());
    }

    [Fact]
    public void ReleaseConfiguration_ExplainsTheDecision()
    {
        var csproj = RepoFile("ConnectOnion.WinUIClient", "ConnectOnion.WinUIClient.csproj");

        // A bare value invites someone to "clean it up" as a leftover default. The comment is what
        // tells the next reader it is load-bearing, so it is part of the contract.
        Assert.Contains("docs/TRIMMING.md", csproj, StringComparison.Ordinal);
    }

    /// <summary>
    /// CI must publish in the same configuration production ships. It previously asserted the
    /// reverse — that every publish was untrimmed — which was right while production was untrimmed
    /// and would now mean the per-PR UI smoke suite exercises a build nobody ever installs.
    /// </summary>
    [Fact]
    public void Ci_PublishesWhatProductionShips()
    {
        var workflow = RepoFile(".github", "workflows", "ci.yml");

        var trimmedSettings = Regex.Matches(workflow, @"-p:PublishTrimmed=(?<value>\w+)");

        Assert.NotEmpty(trimmedSettings);
        Assert.All(trimmedSettings, match =>
            Assert.Equal("true", match.Groups["value"].Value.ToLowerInvariant()));
    }

    /// <summary>
    /// The release workflow is the one that produces the artifact users download, so its publish
    /// must be trimmed too — the portable ZIP is the only distribution channel this repo ships and
    /// the only one the gate's evidence covers.
    /// </summary>
    [Fact]
    public void Release_PublishesTrimmed()
    {
        var workflow = RepoFile(".github", "workflows", "release.yml");

        var trimmedSettings = Regex.Matches(workflow, @"-p:PublishTrimmed=(?<value>\w+)");

        Assert.NotEmpty(trimmedSettings);
        Assert.All(trimmedSettings, match =>
            Assert.Equal("true", match.Groups["value"].Value.ToLowerInvariant()));
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
