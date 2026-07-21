using ConnectOnion.WinUIClient.Common;

namespace ConnectOnion.WinUIClient.UnitTests.Common;

public sealed class FriendlyAgentNameTests
{
    [Theory]
    [InlineData("remote-admin-agent", "Remote Admin Agent")]
    [InlineData("multimodal_agent", "Multimodal Agent")]
    [InlineData("email_assistant", "Email Assistant")]
    [InlineData("browser_agent", "Browser Agent")]
    [InlineData("agent", "Agent")]
    public void From_SnakeOrKebab_TitleCasesWords(string raw, string expected)
    {
        Assert.Equal(expected, FriendlyAgentName.From(raw));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void From_Blank_ReturnsEmpty(string raw, string expected)
    {
        Assert.Equal(expected, FriendlyAgentName.From(raw));
    }

    [Fact]
    public void From_Null_ReturnsEmpty()
    {
        Assert.Equal("", FriendlyAgentName.From(null));
    }

    [Fact]
    public void From_CollapsesRepeatedAndLeadingSeparators()
    {
        Assert.Equal("Remote Admin", FriendlyAgentName.From("--remote__admin--"));
    }

    [Fact]
    public void From_PreservesInteriorCasingOfAWord()
    {
        // Only the first letter of each word is touched, so an intentional acronym survives.
        Assert.Equal("My API Agent", FriendlyAgentName.From("my-API-agent"));
    }

    [Fact]
    public void From_AlreadyFriendlyProse_ReturnedUnchanged()
    {
        Assert.Equal("Customer Support", FriendlyAgentName.From("Customer Support"));
    }
}
