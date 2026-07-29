using System.Xml;
using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace ConnectOnion.WinUIClient.UnitTests.Architecture;

public sealed class LocalizationResourceTests
{
    [Fact]
    public void ChineseLocale_HasSameKeysAsEnglishAndNonEmptyValues()
    {
        var english = ReadResources("en-US");
        var chinese = ReadResources("zh-CN");

        Assert.Equal(english.Keys.Order(), chinese.Keys.Order());
        foreach (var key in english.Keys)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(chinese[key]),
                $"Chinese resource '{key}' is empty.");
        }
    }

    [Fact]
    public void RuntimeLookup_HasEnglishFallbackAndCultureAwareFormatting()
    {
        var source = ReadAppSource("Common", "LocalizedStrings.cs");

        Assert.Contains("return fallback;", source, StringComparison.Ordinal);
        Assert.Contains("CultureInfo.CurrentCulture", source, StringComparison.Ordinal);
        Assert.Contains("ReportFallbackOnce", source, StringComparison.Ordinal);
        Assert.Contains("using English", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LanguagePicker_DefaultsToEnglishAndPersistsSimplifiedChinese()
    {
        var pageSource = ReadAppSource("Views", "SettingsPage.xaml.cs");
        var storeSource = ReadAppSource("Services", "LanguagePreferenceStore.cs");

        Assert.Contains("LanguagePreferenceStore.English", pageSource, StringComparison.Ordinal);
        Assert.Contains("LanguagePreferenceStore.SimplifiedChinese", pageSource, StringComparison.Ordinal);
        Assert.Contains("LanguageRestartInfo.IsOpen = true", pageSource, StringComparison.Ordinal);
        Assert.Contains("public string Current { get; private set; } = English", storeSource, StringComparison.Ordinal);
        Assert.Contains("application_language", storeSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationLanguages.PrimaryLanguageOverride = Current", storeSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ApplicationLanguages.PrimaryLanguageOverride = normalized",
            storeSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyDotNetCulture(normalized)", storeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Xaml_UserVisibleEnglishTextRequiresAResourceUid()
    {
        var appDirectory = FindRepositoryDirectory("ConnectOnion.WinUIClient");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        string[] visibleAttributes =
        [
            "Text", "Content", "Header", "Title", "Description",
            "PlaceholderText", "PrimaryButtonText", "SecondaryButtonText",
            "CloseButtonText", "OnContent", "OffContent",
            "ToolTipService.ToolTip"
        ];
        var violations = new List<string>();

        foreach (var path in Directory.EnumerateFiles(appDirectory, "*.xaml", SearchOption.AllDirectories))
        {
            if (path.Split(Path.DirectorySeparatorChar).Contains("obj", StringComparer.OrdinalIgnoreCase))
                continue;

            var document = XDocument.Load(path, LoadOptions.SetLineInfo);
            foreach (var element in document.Descendants())
            {
                if (element.Attribute(xaml + "Uid") is not null)
                    continue;

                foreach (var attributeName in visibleAttributes)
                {
                    var attribute = element.Attribute(attributeName);
                    if (attribute is null || !ContainsEnglishWord(attribute.Value))
                        continue;
                    if (attribute.Value is "ConnectOnion" ||
                        attribute.Value.StartsWith("Ctrl+", StringComparison.Ordinal) ||
                        attribute.Value.Contains("://", StringComparison.Ordinal))
                        continue;

                    var line = ((IXmlLineInfo)element).HasLineInfo()
                        ? ((IXmlLineInfo)element).LineNumber
                        : 0;
                    violations.Add(
                        $"{Path.GetRelativePath(appDirectory, path)}:{line} " +
                        $"{element.Name.LocalName}.{attributeName}=\"{attribute.Value}\"");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "User-visible XAML text must use x:Uid and .resw resources:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Sidebar_MoreActionsTooltipsAllUseLocalizedResource()
    {
        var path = FindRepositoryFile(
            "ConnectOnion.WinUIClient", "Controls", "Shell", "ShellSidebar.xaml");
        var document = XDocument.Load(path);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var buttons = document.Descendants()
            .Where(element =>
                string.Equals(
                    element.Attribute("ToolTipService.ToolTip")?.Value,
                    "More actions",
                    StringComparison.Ordinal))
            .ToList();

        Assert.Equal(3, buttons.Count);
        Assert.All(
            buttons,
            button => Assert.Equal(
                "SidebarMoreActions",
                button.Attribute(xaml + "Uid")?.Value));
    }

    [Fact]
    public void PrimaryCodeBehind_DoesNotAssignHardCodedEnglishToUiProperties()
    {
        string[] primaryDirectories = ["Views", "Controls", "Shell"];
        var assignment = new Regex(
            @"(?:Text|Content|Title|Header|PlaceholderText|PrimaryButtonText|" +
            @"SecondaryButtonText|CloseButtonText)\s*=\s*""[A-Za-z]",
            RegexOptions.CultureInvariant);
        var violations = new List<string>();

        foreach (var directoryName in primaryDirectories)
        {
            var directory = FindRepositoryDirectory("ConnectOnion.WinUIClient", directoryName);
            foreach (var path in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(
                    FindRepositoryDirectory("ConnectOnion.WinUIClient"),
                    path);
                var lineNumber = 0;
                foreach (var line in File.ReadLines(path))
                {
                    lineNumber++;
                    if (!assignment.IsMatch(line))
                        continue;

                    // Internal font measurement probe; it is never attached to the visual tree.
                    if (relativePath.EndsWith(
                            Path.Combine("Controls", "Primitives", "HighlightedTextBlock.cs"),
                            StringComparison.OrdinalIgnoreCase) &&
                        line.Contains("new TextBlock { Text = \"Hg\"", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    violations.Add($"{relativePath}:{lineNumber} {line.Trim()}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Code-behind must resolve user-visible text through LocalizedStrings:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static bool ContainsEnglishWord(string value)
    {
        if (value.StartsWith('{') || value.Length < 2)
            return false;

        var consecutiveLetters = 0;
        foreach (var character in value)
        {
            consecutiveLetters = char.IsAsciiLetter(character) ? consecutiveLetters + 1 : 0;
            if (consecutiveLetters >= 2)
                return true;
        }
        return false;
    }

    private static Dictionary<string, string> ReadResources(string locale)
    {
        var path = FindRepositoryFile(
            "ConnectOnion.WinUIClient", "Strings", locale, "Resources.resw");
        var document = XDocument.Load(path);
        return document.Root!
            .Elements("data")
            .ToDictionary(
                entry => entry.Attribute("name")!.Value,
                entry => entry.Element("value")?.Value ?? "",
                StringComparer.Ordinal);
    }

    private static string ReadAppSource(params string[] relativeParts)
    {
        string[] pathParts = ["ConnectOnion.WinUIClient", .. relativeParts];
        return File.ReadAllText(FindRepositoryFile(pathParts));
    }

    private static string FindRepositoryFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repository file {Path.Combine(relativeParts)}.");
    }

    private static string FindRepositoryDirectory(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (Directory.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate repository directory {Path.Combine(relativeParts)}.");
    }
}
