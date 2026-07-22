using ConnectOnion.WinUIClient.Models;

namespace ConnectOnion.WinUIClient.UnitTests.Models;

public sealed class WindowPlacementPolicyTests
{
    [Theory]
    [InlineData("120,340", 120, 340, false)]
    [InlineData("-1920,-40,0", -1920, -40, false)]
    [InlineData("80,40,1", 80, 40, true)]
    public void TryParse_ValidInvariantCoordinates_RoundTrips(string value, int x, int y, bool maximized)
    {
        var position = WindowPlacementPolicy.TryParse(value);

        Assert.Equal(new WindowPlacement(new WindowPosition(x, y), maximized), position);
        Assert.Equal($"{x},{y},{(maximized ? 1 : 0)}", WindowPlacementPolicy.Serialize(position!.Value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12")]
    [InlineData("x,20")]
    [InlineData("10,20,30")]
    public void TryParse_InvalidValue_ReturnsNull(string? value)
        => Assert.Null(WindowPlacementPolicy.TryParse(value));

    [Fact]
    public void ClampToWorkArea_PositionAlreadyOnNegativeCoordinateMonitor_IsPreserved()
    {
        var result = WindowPlacementPolicy.ClampToWorkArea(
            new WindowPosition(-1700, 120),
            new PixelSize(1200, 800),
            new PixelRect(-1920, 0, 1920, 1080));

        Assert.Equal(new WindowPosition(-1700, 120), result);
    }

    [Fact]
    public void ClampToWorkArea_DisconnectedMonitor_MovesWindowFullyIntoNearestDisplay()
    {
        var result = WindowPlacementPolicy.ClampToWorkArea(
            new WindowPosition(2600, 1400),
            new PixelSize(1200, 800),
            new PixelRect(0, 0, 1920, 1040));

        Assert.Equal(new WindowPosition(720, 240), result);
    }

    [Fact]
    public void ClampToWorkArea_WindowLargerThanDisplay_KeepsCaptionAtWorkAreaOrigin()
    {
        var result = WindowPlacementPolicy.ClampToWorkArea(
            new WindowPosition(500, 400),
            new PixelSize(2200, 1400),
            new PixelRect(0, 40, 1920, 1000));

        Assert.Equal(new WindowPosition(0, 40), result);
    }
}
