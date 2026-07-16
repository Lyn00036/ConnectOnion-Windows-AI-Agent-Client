using System;

namespace ConnectOnion.WinUIClient.Services;

public static class AgentAddressValidator
{
    public const string ValidationMessage =
        "Enter a valid agent address (0x + 64 hex characters)";

    public static bool IsValid(string address)
    {
        if (address.Length != 66 || !address.StartsWith("0x", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var character in address.AsSpan(2))
        {
            if (!IsHexCharacter(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsHexCharacter(char character)
        => character is >= '0' and <= '9'
            or >= 'a' and <= 'f'
            or >= 'A' and <= 'F';
}
