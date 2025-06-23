using Nethereum.Util;

namespace Circles.Profiles.Sdk;

/// <summary>Very small façade so call-sites can stay terse.</summary>
internal static class Sha3
{
    private static readonly Sha3Keccack _keccak = new();

    internal static byte[] Keccak256Bytes(ReadOnlySpan<byte> data) =>
        _keccak.CalculateHash(data.ToArray());
}