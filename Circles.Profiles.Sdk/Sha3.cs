using Nethereum.Util;

namespace Circles.Profiles.Sdk;

/// <summary>Very small façade so call-sites can stay terse.</summary>
public static class Sha3
{
    private static readonly Sha3Keccack Keccak = new();

    public static byte[] Keccak256Bytes(ReadOnlySpan<byte> data) =>
        Keccak.CalculateHash(data.ToArray());
}