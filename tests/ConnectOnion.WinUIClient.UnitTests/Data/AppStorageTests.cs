using ConnectOnion.WinUIClient.Data;
using ConnectOnion.WinUIClient.Services.Runtime;

namespace ConnectOnion.WinUIClient.UnitTests.Data;

public sealed class AppStorageTests
{
    [Fact]
    public void SelectRootDir_Packaged_UsesLocalStateFolder()
    {
        var localState = Path.Combine(Path.GetTempPath(), "package", "LocalState");

        Assert.Equal(
            Path.Combine(localState, "ConnectOnion"),
            AppStorage.SelectRootDir(null, localState, null));
    }

    [Fact]
    public void SelectRootDir_Unpackaged_UsesRoamingAppData()
    {
        var roaming = Path.Combine(Path.GetTempPath(), "Roaming");

        Assert.Equal(
            Path.Combine(roaming, "ConnectOnion"),
            AppStorage.SelectRootDir(null, null, roaming));
    }

    [Fact]
    public void SelectRootDir_OverrideWinsAcrossDeploymentModels()
    {
        var configured = Path.Combine(Path.GetTempPath(), "isolated");

        Assert.Equal(
            Path.GetFullPath(configured),
            AppStorage.SelectRootDir(configured, "ignored-package", "ignored-roaming"));
    }

    [Fact]
    public void DataRootInstanceIdentity_NormalizesCaseAndTrailingSeparator()
    {
        var root = Path.Combine(Path.GetTempPath(), "ConnectOnion-profile");

        Assert.Equal(
            DataRootInstanceIdentity.ForPath(root),
            DataRootInstanceIdentity.ForPath(root.ToUpperInvariant() + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void DataRootInstanceIdentity_DifferentProfilesCanRunIndependently()
    {
        var root = Path.GetTempPath();

        Assert.NotEqual(
            DataRootInstanceIdentity.ForPath(Path.Combine(root, "profile-a")),
            DataRootInstanceIdentity.ForPath(Path.Combine(root, "profile-b")));
    }
}
