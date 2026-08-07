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
    /// A normal Release build leaves untrimmed publish intermediates under <c>obj</c>. Reusing
    /// those files with <c>publish --no-restore</c> can silently produce an untrimmed portable ZIP
    /// even though <c>PublishTrimmed=true</c> is present on the command line. The release job must
    /// clean that runtime-specific state and let publish restore its own asset graph.
    /// </summary>
    [Fact]
    public void Release_CleansPublishIntermediatesAndDoesNotSkipRestore()
    {
        var workflow = RepoFile(".github", "workflows", "release.yml");

        AssertPublishStartsFromCleanIntermediates(
            workflow,
            "Clean portable publish intermediates",
            "release workflow");
    }

    /// <summary>
    /// The real-window CI suite launches the published output, so it needs the same clean,
    /// RID-specific property graph as the release artifact. Otherwise stale intermediates from
    /// the preceding solution build can produce an executable that exits before UIA sees a
    /// window, making every smoke test fail at launch.
    /// </summary>
    [Fact]
    public void Ci_CleansPublishIntermediatesAndDoesNotSkipRestore()
    {
        var workflow = RepoFile(".github", "workflows", "ci.yml");

        AssertPublishStartsFromCleanIntermediates(
            workflow,
            "Clean UI smoke publish intermediates",
            "CI workflow");
    }

    /// <summary>
    /// <b>Every</b> publish in a workflow, not just the first one each check above happens to
    /// match.
    ///
    /// <para>The two tests above use a regex that stops at the first <c>dotnet publish</c> in the
    /// file, so the release workflow's second publish — the NativeAOT portable launcher — was
    /// never covered and carried <c>--no-restore</c> unnoticed. It needed the guarantee more than
    /// the app did, not less: the solution build compiles that project <i>without</i> AOT, so its
    /// RID intermediates come from a different property graph than the one
    /// <c>-p:PublishAot=true</c> asks for, and there was no clean step in front of it.</para>
    ///
    /// <para>This is the general rule; keep the specific tests above for the messages they give
    /// when the app's own publish is the one that regressed.</para>
    /// </summary>
    [Theory]
    [InlineData("ci.yml")]
    [InlineData("release.yml")]
    public void EveryPublish_RestoresItsOwnAssetGraph(string workflowFile)
    {
        var workflow = RepoFile(".github", "workflows", workflowFile);

        var publishes = Regex.Matches(workflow, @"dotnet publish[\s\S]*?(?=\r?\n\s*(?:#|- name:))");
        Assert.NotEmpty(publishes);

        foreach (var publish in publishes.Cast<Match>())
        {
            Assert.DoesNotContain("--no-restore", publish.Value, StringComparison.Ordinal);
        }
    }

    /// <summary>Each publish is preceded by a clean of the same project, so the publish cannot
    /// inherit intermediates the preceding solution build produced under different properties.</summary>
    [Fact]
    public void ReleaseWorkflow_CleansBothProjectsItPublishes()
    {
        var workflow = RepoFile(".github", "workflows", "release.yml");

        Assert.Contains("Clean portable publish intermediates", workflow, StringComparison.Ordinal);
        Assert.Contains("Clean portable launcher intermediates", workflow, StringComparison.Ordinal);

        // A clean per publish. The counts moving apart is the signal that one was added without
        // the other, which is exactly how the launcher went uncovered the first time.
        var cleans = Regex.Matches(workflow, @"dotnet clean ").Count;
        var publishes = Regex.Matches(workflow, @"dotnet publish ").Count;
        Assert.Equal(publishes, cleans);
    }

    private static void AssertPublishStartsFromCleanIntermediates(
        string workflow,
        string cleanStepName,
        string workflowDescription)
    {
        Assert.Contains(cleanStepName, workflow, StringComparison.Ordinal);
        var cleanCommand = Regex.Match(
            workflow,
            @"dotnet clean[\s\S]*?(?=\r?\n\s*- name:)");

        Assert.True(cleanCommand.Success, $"Expected the {workflowDescription} to clean publish intermediates.");
        Assert.Contains("--configuration $env:CONFIGURATION", cleanCommand.Value, StringComparison.Ordinal);
        Assert.Contains("--runtime win-x64", cleanCommand.Value, StringComparison.Ordinal);

        var publishCommand = Regex.Match(
            workflow,
            @"dotnet publish[\s\S]*?(?=\r?\n\s*- name:)");

        Assert.True(publishCommand.Success, $"Expected the {workflowDescription} to publish the app.");
        Assert.DoesNotContain("--no-restore", publishCommand.Value, StringComparison.Ordinal);
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
