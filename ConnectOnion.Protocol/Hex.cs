using System;

namespace ConnectOnion.Protocol;

public static class Hex
{
    public static string ToLowerString(ReadOnlySpan<byte> bytes)
        => Convert.ToHexStringLower(bytes);
}
