using System.Xml.Linq;

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
}
