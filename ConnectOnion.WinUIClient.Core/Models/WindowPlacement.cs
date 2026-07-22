using System.Globalization;

namespace ConnectOnion.WinUIClient.Models;

public readonly record struct WindowPosition(int X, int Y);
public readonly record struct WindowPlacement(WindowPosition Position, bool IsMaximized);
public readonly record struct PixelSize(int Width, int Height);
public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>WinUI-free parsing and visibility rules for persisted main-window placement.</summary>
public static class WindowPlacementPolicy
{
    public static string Serialize(WindowPlacement placement)
        => string.Create(CultureInfo.InvariantCulture,
            $"{placement.Position.X},{placement.Position.Y},{(placement.IsMaximized ? 1 : 0)}");

    public static WindowPlacement? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value.Split(',');
        if (parts.Length is not (2 or 3)
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
            return null;

        // Two fields are placements written by older builds; they reopen restored.
        var maximized = false;
        if (parts.Length == 3)
        {
            if (parts[2] == "1") maximized = true;
            else if (parts[2] != "0") return null;
        }
        return new WindowPlacement(new WindowPosition(x, y), maximized);
    }

    /// <summary>Keeps the full window in the work area when it fits; otherwise anchors it at the
    /// top-left so the caption remains reachable after a monitor, resolution, or DPI change.</summary>
    public static WindowPosition ClampToWorkArea(
        WindowPosition position, PixelSize window, PixelRect workArea)
    {
        var maxX = workArea.X + Math.Max(0, workArea.Width - window.Width);
        var maxY = workArea.Y + Math.Max(0, workArea.Height - window.Height);
        return new WindowPosition(
            Math.Clamp(position.X, workArea.X, maxX),
            Math.Clamp(position.Y, workArea.Y, maxY));
    }
}
