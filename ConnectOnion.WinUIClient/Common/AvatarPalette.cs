namespace ConnectOnion.WinUIClient.Common;

/// <summary>Maps a stable agent name to one of the shared avatar palette slots.</summary>
public static class AvatarPalette
{
    public static int From(string? value)
    {
        unchecked
        {
            var hash = 5381u;
            foreach (var character in value ?? "")
            {
                hash = ((hash << 5) + hash) ^ char.ToUpperInvariant(character);
            }
            return (int)(hash % 12);
        }
    }
}
