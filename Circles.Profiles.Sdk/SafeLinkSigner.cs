using System.Numerics;
using Circles.Profiles.Interfaces;
using Circles.Profiles.Models.Core;
using Circles.Profiles.Sdk.Utils;
using Nethereum.ABI;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Model;
using Nethereum.Signer;

namespace Circles.Profiles.Sdk;

/// <summary>
/// Produces an ECDSA signature with <c>signerAddress == SafeAddress</c>
/// while still using the *owner* EOA key for the cryptographic proof.
/// The resulting link passes on‑chain ERC‑1271 checks for the Safe.
/// </summary>
public sealed class SafeLinkSigner : ILinkSigner
{
    private readonly string _safe;
    private readonly IChainApi _chain;

    private static readonly ABIEncode Abi = new();

    // IMPORTANT: Safe expects "SafeMessage(bytes message)"
    private static readonly byte[] SafeMsgTypeHash =
        Sha3.Keccak256Bytes("SafeMessage(bytes message)"u8);

    private static readonly byte[] DomainTypeHash =
        Sha3.Keccak256Bytes("EIP712Domain(uint256 chainId,address verifyingContract)"u8);

    public SafeLinkSigner(string safeAddress, IChainApi chain)
    {
        bool isEmpty = string.IsNullOrWhiteSpace(safeAddress);
        if (isEmpty)
        {
            throw new ArgumentException(nameof(safeAddress));
        }

        _safe = safeAddress;
        _chain = chain;
    }

    public CustomDataLink Sign(CustomDataLink link, string ownerPrivKeyHex)
    {
        var ownerKey = new EthECKey(ownerPrivKeyHex);
        link = link with { SignerAddress = _safe };

        // keccak(canonical JSON w/o signature)
        byte[] payloadHash = Sha3.Keccak256Bytes(
            CanonicalJson.CanonicaliseWithoutSignature(link));

        // EIP‑712 Safe message hash for the *payload bytes* (via their keccak)
        byte[] safeHash = ComputeSafeHash(payloadHash, _chain.Id, _safe);

        var sig = ownerKey.SignAndCalculateV(safeHash);
        byte[] bytes = sig.To64ByteArray().Concat([sig.V[0]]).ToArray();

        return link with { Signature = "0x" + bytes.ToHex() };
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
    /// Computes keccak(0x1901 || domainSeparator || keccak(SafeMessage(bytes message) || keccak(payloadBytes))).
    /// The <paramref name="payloadHash"/> is keccak(payloadBytes).
    /// </summary>
    public static byte[] ComputeSafeHash(byte[] payloadHash, BigInteger chainId, string safe)
    {
        // keccak256(SafeMessage(bytes message) || keccak(payloadBytes))
        byte[] safeMsg = Sha3.Keccak256Bytes(
            SafeMsgTypeHash.Concat(payloadHash).ToArray());

        // keccak256(0x1901 || domainSeparator || safeMsg)
        byte[] domain = BuildDomainSeparator(chainId, safe);
        return Sha3.Keccak256Bytes(new byte[] { 0x19, 0x01 }
            .Concat(domain)
            .Concat(safeMsg)
            .ToArray());
    }
}