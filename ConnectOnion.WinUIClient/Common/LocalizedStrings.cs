using System;
using Microsoft.Windows.ApplicationModel.Resources;

namespace ConnectOnion.WinUIClient.Common;

/// <summary>
/// Reads a string out of <c>Strings/&lt;locale&gt;/Resources.resw</c> for code that builds a
/// message rather than declaring it in XAML.
///
/// Views use <c>x:Uid</c> and never come through here. This exists for the handful of strings that
/// only exist at runtime — a failure message chosen from an error code, for instance.
///
/// <para><b>The fallback is the point.</b> <see cref="ResourceMap.GetValue(string)"/> throws on a
/// missing key and <c>TryGetValue</c> returns null, either of which would turn a forgotten resw
/// entry into a blank dialog or a crash in the one code path nobody exercises. Callers pass the
/// English text they would otherwise have inlined, so a missing key degrades to exactly the string
/// this repo had before it was localized — the same rule the XAML side follows by keeping literal
/// text alongside its <c>x:Uid</c>.</para>
/// </summary>
public static class LocalizedStrings
{
    private static readonly Lazy<ResourceMap?> Resources = new(() =>
    {
        try
        {
            return new ResourceManager().MainResourceMap.TryGetSubtree("Resources");
        }
        catch (Exception)
        {
            // An unpackaged host with no PRI loaded still has to render its UI in English.
            return null;
        }
    });

    /// <param name="key">The resw <c>data name</c>, without any <c>x:Uid</c> property suffix.</param>
    /// <param name="fallback">The English text to use when the key is missing.</param>
    public static string Get(string key, string fallback)
    {
        try
        {
            var candidate = Resources.Value?.TryGetValue(key)?.ValueAsString;
            return string.IsNullOrEmpty(candidate) ? fallback : candidate;
        }
        catch (Exception)
        {
            return fallback;
        }
    }
}
