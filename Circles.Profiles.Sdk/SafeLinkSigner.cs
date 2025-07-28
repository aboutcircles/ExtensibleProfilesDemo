using System.Numerics;
using Circles.Profiles.Interfaces;
using Circles.Profiles.Models;
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
    private static readonly byte[] SafeMsgTypeHash =
        Sha3.Keccak256Bytes("SafeMessage(bytes)"u8);

    public SafeLinkSigner(string safeAddress, IChainApi chain)
    {
        if (string.IsNullOrWhiteSpace(safeAddress))
            throw new ArgumentException(nameof(safeAddress));

        _safe = safeAddress;
        _chain = chain;
    }

    public CustomDataLink Sign(CustomDataLink link, string ownerPrivKeyHex)
    {
        var ownerKey = new EthECKey(ownerPrivKeyHex);
        link = link with { SignerAddress = _safe };

        /* --- hashes --- */
        byte[] payloadHash = Sha3.Keccak256Bytes(
            CanonicalJson.CanonicaliseWithoutSignature(link));

        byte[] safeTxHash = Sha3.Keccak256Bytes(
            SafeMsgTypeHash.Concat(payloadHash).ToArray());

        byte[] domainSeparator = BuildDomainSeparator(_chain.Id, _safe);

        byte[] safeHash = Sha3.Keccak256Bytes(
            new byte[] { 0x19, 0x01 }
                .Concat(domainSeparator)
                .Concat(safeTxHash)
                .ToArray());

        var sig = ownerKey.SignAndCalculateV(safeHash);
        byte[] bytes = sig.To64ByteArray().Concat([sig.V[0]]).ToArray();

        return link with { Signature = "0x" + bytes.ToHex() };
    }

    private static byte[] BuildDomainSeparator(BigInteger chainId, string safe)
    {
        var typeHash = Sha3
            .Keccak256Bytes("EIP712Domain(uint256 chainId,address verifyingContract)"u8);

        var encoded = Abi.GetABIEncoded(
            new ABIValue("bytes32", typeHash),
            new ABIValue("uint256", chainId),
            new ABIValue("address", safe));

        return Sha3.Keccak256Bytes(encoded);
    }
}