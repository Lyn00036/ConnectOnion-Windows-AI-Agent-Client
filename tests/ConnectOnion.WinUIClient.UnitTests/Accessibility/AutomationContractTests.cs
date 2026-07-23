using System.Text.RegularExpressions;

namespace ConnectOnion.WinUIClient.UnitTests.Accessibility;

public sealed partial class AutomationContractTests
{
    private static readonly string[] RequiredIds =
    {
        "MainWindow",
        "AgentList",
        "AgentAvatarButton",
        "AgentCapabilitiesLoadingIndicator",
        "AddAgentButton",
        "AgentAddressInput",
        "SubmitAgentButton",
        "MessageList",
        "MessageInput",
        "SendMessageButton",
        "StopResponseButton",
        "ConnectionStatus",
        "SettingsButton",
        "HelpMenuButton",
        "AboutMenuItem",
        "ConnectOnionDocsMenuItem",
        "KeyboardShortcutsMenuItem",
        "SettingsOverlay",
        "AgentsNav",
        "SettingsAgentList",
        "SettingsAddAgentButton",
        "AboutOverlay",
        "KeyboardShortcutsOverlay",
        "AboutOkButton",
        "ShortcutsSearchBox",
        "ApprovalModeButton",
    };

    [Fact]
    public void Xaml_E2eAutomationIds_ArePresentAndUnique()
    {
        var root = FindRepositoryRoot();
        var xaml = Directory.GetFiles(
                Path.Combine(root, "ConnectOnion.WinUIClient"), "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => AutomationIdRegex().Matches(File.ReadAllText(path)).Select(match => match.Groups[1].Value))
            .ToList();

        foreach (var required in RequiredIds)
            Assert.Contains(required, xaml);

        var duplicates = xaml.GroupBy(id => id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        Assert.Empty(duplicates);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "ConnectOnion.WinUIClient")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the ConnectOnion repository root.");
    }

    [GeneratedRegex("(?:AutomationProperties\\.AutomationId=\"|Property=\"AutomationProperties\\.AutomationId\" Value=\")([^\"]+)\"")]
    private static partial Regex AutomationIdRegex();
}
