using System.Xml.Linq;

namespace ConnectOnion.WinUIClient.UnitTests.Architecture;

/// <summary>
/// Guards the package identity in the committed manifest.
///
/// <para><b>`Name` + `Publisher` are the upgrade identity and can never change.</b> Windows keys
/// installs off that pair: a package whose pair differs is a different application, so it installs
/// beside the previous one rather than upgrading it, and it gets a different
/// <c>%LOCALAPPDATA%\Packages\&lt;Name&gt;_&lt;publisher hash&gt;\</c> folder. Every conversation
/// and the DPAPI-protected agent identity are still on disk and completely invisible to the new
/// install. Nothing warns anyone, and there is no rename path afterwards.</para>
///
/// <para>These are cheap assertions against a one-line edit whose consequence is permanent and
/// silent. See <c>docs/RELEASE.md</c>.</para>
/// </summary>
public sealed class PackageIdentityTests
{
    private const string IdentityName = "ConnectOnion.Desktop";

    private static XElement Identity()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);

        var path = Path.Combine(directory!.FullName, "ConnectOnion.WinUIClient", "Package.appxmanifest");
        Assert.True(File.Exists(path), $"Expected to find {path}.");

        var document = XDocument.Load(path);
        var ns = XNamespace.Get("http://schemas.microsoft.com/appx/manifest/foundation/windows10");
        var identity = document.Root?.Element(ns + "Identity");
        Assert.NotNull(identity);
        return identity!;
    }

    [Fact]
    public void UpgradeIdentityName_IsUnchanged()
        => Assert.Equal(IdentityName, Identity().Attribute("Name")?.Value);

    /// <summary>
    /// The committed publisher is the local/self-signed default; the release workflow overwrites
    /// it with the certificate's subject. What must never come back is a developer's machine name.
    /// </summary>
    [Fact]
    public void Publisher_IsNotADeveloperMachineIdentity()
    {
        var publisher = Identity().Attribute("Publisher")?.Value;

        Assert.False(string.IsNullOrWhiteSpace(publisher));
        Assert.StartsWith("CN=", publisher, StringComparison.Ordinal);
        Assert.False(
            string.Equals("CN=ROG", publisher, StringComparison.OrdinalIgnoreCase),
            "The development publisher is back in the manifest.");
    }

    /// <summary>
    /// `0.0.0.0` is load-bearing: <c>Set-PackageIdentity.ps1</c> refuses it as a release version,
    /// so a package carrying it proves the release pipeline was bypassed. Committing a real-looking
    /// version would destroy that signal.
    /// </summary>
    [Fact]
    public void CommittedVersion_IsThePlaceholder()
        => Assert.Equal("0.0.0.0", Identity().Attribute("Version")?.Value);

    [Fact]
    public void DisplayNames_AreProductNamesNotProjectNames()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }
        var manifest = File.ReadAllText(
            Path.Combine(directory!.FullName, "ConnectOnion.WinUIClient", "Package.appxmanifest"));

        // "ConnectOnion.WinUIClient" is the assembly name; it was the Start-menu entry, the
        // installed-apps entry and the toast attribution before this pipeline existed.
        Assert.DoesNotContain("<DisplayName>ConnectOnion.WinUIClient</DisplayName>", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("DisplayName=\"ConnectOnion.WinUIClient\"", manifest, StringComparison.Ordinal);
        Assert.Contains("<DisplayName>ConnectOnion Desktop</DisplayName>", manifest, StringComparison.Ordinal);
    }

    // The release workflow's PublishTrimmed setting used to be asserted here as well, pinned to
    // false. It moved out rather than being flipped: TrimmingGateTests is the designated guard for
    // that flag and now pins it in four places (Release, Debug, and both workflows' publishes), and
    // two tests asserting the same contract from different files is how they end up disagreeing —
    // which is exactly what happened when trimming was enabled on 2026-08-05 and this copy still
    // demanded the opposite. The concern that put it here (someone reaching for trimming to escape
    // a size-budget breach) is now answered where it actually arises: Test-PackageContents.ps1 says
    // so in the failure message. See docs/TRIMMING.md.

    [Fact]
    public void ReleaseWorkflow_PublishesOnlySelfContainedPortableArchive()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }
        var workflow = File.ReadAllText(
            Path.Combine(directory!.FullName, ".github", "workflows", "release.yml"));

        Assert.Contains("-p:SelfContained=true", workflow, StringComparison.Ordinal);
        Assert.Contains("-p:WindowsAppSDKSelfContained=true", workflow, StringComparison.Ordinal);
        Assert.Contains("-p:RunUnpackaged=true", workflow, StringComparison.Ordinal);
        Assert.Contains("Microsoft.WindowsAppRuntime.dll", workflow, StringComparison.Ordinal);
        Assert.Contains("-x64-portable.zip", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "Test-PackageContents.ps1 -PackagePath '${{ steps.collect.outputs.portable }}'",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains("No portable ZIP to publish.", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("SIGNING_CERTIFICATE", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("SIGNING_PUBLISHER_DN", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("signtool", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GenerateAppxPackageOnBuild=true", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("steps.collect.outputs.msix", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleasePayloadIncludesThirdPartyNotices()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.True(File.Exists(Path.Combine(directory!.FullName, "THIRD-PARTY-NOTICES.md")));
        var project = File.ReadAllText(Path.Combine(
            directory.FullName,
            "ConnectOnion.WinUIClient",
            "ConnectOnion.WinUIClient.csproj"));
        Assert.Contains(
            @"<Content Include=""..\THIRD-PARTY-NOTICES.md""",
            project,
            StringComparison.Ordinal);
        Assert.Contains(
            "<CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>",
            project,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseWorkflow_DryRunUsesCommitRatherThanAnUncreatedTag()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }
        var workflow = File.ReadAllText(
            Path.Combine(directory!.FullName, ".github", "workflows", "release.yml"));

        Assert.Contains("'${{ github.sha }}'", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "git log \"$previous..$upperRevision\"",
            workflow,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "git log \"$previous..${{ steps.version.outputs.version }}\"",
            workflow,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PackageAuditRejectsExternalFrameworkDependencies()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }
        var audit = File.ReadAllText(
            Path.Combine(directory!.FullName, "scripts", "Test-PackageContents.ps1"));

        Assert.Contains("PackageDependency", audit, StringComparison.Ordinal);
        Assert.Contains("External MSIX framework dependency", audit, StringComparison.Ordinal);
    }
}
