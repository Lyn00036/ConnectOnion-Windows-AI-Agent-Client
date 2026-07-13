using ConnectOnion.WinUIClient.Common;

namespace ConnectOnion.WinUIClient.UnitTests.Common;

public sealed class NameInitialTests
{
    [Theory]
    [InlineData("connectonion", "C")]
    [InlineData("", "?")]
    [InlineData("   ", "?")]
    [InlineData("  onion", "O")]
    public void From_Name_ReturnsUppercaseFirstGrapheme(string name, string expected)
    {
        Assert.Equal(expected, NameInitial.From(name));
    }

    [Fact]
    public void From_EmojiPrefix_ReturnsWholeSurrogatePair()
    {
        const string emoji = "\U0001F642";
        Assert.Equal(emoji, NameInitial.From(emoji + "x"));
    }

    [Fact]
    public void From_Null_ReturnsQuestionMark()
    {
        Assert.Equal("?", NameInitial.From(null));
    }
}
