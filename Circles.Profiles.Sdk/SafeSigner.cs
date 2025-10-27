using System.Numerics;
using Circles.Profiles.Interfaces;
using Circles.Profiles.Sdk.Utils;
using Nethereum.ABI;
using Nethereum.Model;
using Nethereum.Signer;

namespace Circles.Profiles.Sdk;

/// <summary>
/// Gnosis Safe signer: produces a signature that verifies for the Safe address
/// via ERC-1271(bytes,bytes) on the Safe.
/// </summary>
public sealed class SafeSigner : ISigner
{
    private static readonly ABIEncode Abi = new();

    private static readonly byte[] SafeMsgTypeHash =
        Sha3.Keccak256Bytes("SafeMessage(bytes message)"u8);

    private static readonly byte[] DomainTypeHash =
        Sha3.Keccak256Bytes("EIP712Domain(uint256 chainId,address verifyingContract)"u8);

    private readonly EthECKey _owner;

    public SafeSigner(string safeAddress, EthECKey ownerKey)
    {
        if (string.IsNullOrWhiteSpace(safeAddress))
        {
            throw new ArgumentException(nameof(safeAddress));
        }

        Address = safeAddress.Trim().ToLowerInvariant();
        _owner = ownerKey ?? throw new ArgumentNullException(nameof(ownerKey));
    }

    public string Address { get; }

    public Task<byte[]> SignAsync(
        ReadOnlyMemory<byte> canonicalPayload,
        long chainId,
        CancellationToken ct = default)
    {
        byte[] payloadKeccak = Sha3.Keccak256Bytes(canonicalPayload.Span);
        byte[] safeHash = ComputeSafeHash(payloadKeccak, new BigInteger(chainId), Address);

        var sig = _owner.SignAndCalculateV(safeHash);
        byte[] bytes = sig.To64ByteArray().Concat([sig.V[0]]).ToArray();
        return Task.FromResult(bytes);
    }

    public static byte[] BuildDomainSeparator(BigInteger chainId, string safe)
    {
        var encoded = Abi.GetABIEncoded(
            new ABIValue("bytes32", DomainTypeHash),
            new ABIValue("uint256", chainId),
            new ABIValue("address", safe));

        return Sha3.Keccak256Bytes(encoded);
    }

    /// <summary>
    /// Computes keccak(0x1901 || domainSeparator || keccak(SafeMessage(bytes) || keccak(payload))).
    /// <paramref name="payloadHash"/> must be keccak(payloadBytes).
    /// </summary>
    public static byte[] ComputeSafeHash(byte[] payloadHash, BigInteger chainId, string safe)
    {
        byte[] safeMsg = Sha3.Keccak256Bytes(SafeMsgTypeHash.Concat(payloadHash).ToArray());
        byte[] domain = BuildDomainSeparator(chainId, safe);
        return Sha3.Keccak256Bytes(new byte[] { 0x19, 0x01 }
            .Concat(domain)
            .Concat(safeMsg)
            .ToArray());
    }
}
