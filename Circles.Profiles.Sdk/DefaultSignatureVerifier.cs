using Circles.Profiles.Interfaces;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Signer;

namespace Circles.Profiles.Sdk;

/// <inheritdoc cref="ISignatureVerifier"/>
public sealed class DefaultSignatureVerifier : ISignatureVerifier
{
    // magic return values – big‑endian byte layout, exactly as Solidity returns them
    private static readonly byte[] Magic32 = { 0x16, 0x26, 0xBA, 0x7E };
    private static readonly byte[] Magic = { 0x20, 0xC1, 0x3B, 0x0B };

    private const string AbiBytes32 = @"[
        {""inputs"":[{""type"":""bytes32""},{""type"":""bytes""}],
         ""name"":""isValidSignature"",""outputs"":[{""type"":""bytes4""}],
         ""stateMutability"":""view"",""type"":""function""}
    ]";

    private const string AbiBytes = @"[
        {""inputs"":[{""type"":""bytes""},{""type"":""bytes""}],
         ""name"":""isValidSignature"",""outputs"":[{""type"":""bytes4""}],
         ""stateMutability"":""view"",""type"":""function""}
    ]";

    private readonly IChainApi _chain;

    public DefaultSignatureVerifier(IChainApi chainApi)
        => _chain = chainApi ?? throw new ArgumentNullException(nameof(chainApi));

    public async Task<bool> VerifyAsync(
        byte[] hash,
        string signerAddress,
        byte[] signature,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(signature);
        if (string.IsNullOrWhiteSpace(signerAddress))
            throw new ArgumentException("Empty address", nameof(signerAddress));

        string code = await _chain.GetCodeAsync(signerAddress, ct).ConfigureAwait(false);
        bool isContract = !string.IsNullOrEmpty(code) && code != "0x";

        if (!isContract)
        {
            string sigHex = "0x" + signature.ToHex();
            var sig = EthECDSASignatureFactory.ExtractECDSASignature(sigHex);

            /* ---- EIP‑2 / Safe high‑S malleability guard ---- */
            if (!sig.IsLowS) return false;

            string? recovered = EthECKey.RecoverFromSignature(sig, hash)?.GetPublicAddress();
            return recovered?.Equals(signerAddress, StringComparison.OrdinalIgnoreCase) == true;
        }

        // ERC‑1271: bytes32 first; if that fails the caller can decide to try the bytes variant.
        if (await Try1271Async(AbiBytes32, Magic32, signerAddress, hash, signature, ct))
            return true;

        return await Try1271Async(AbiBytes, Magic, signerAddress, hash, signature, ct);
    }

    private async Task<bool> Try1271Async(
        string abi,
        byte[] magicBe,
        string contract,
        byte[] dataOrHash,
        byte[] sig,
        CancellationToken ct)
    {
        SignatureCallResult call = await _chain
            .CallIsValidSignatureAsync(contract, abi, dataOrHash, sig, ct)
            .ConfigureAwait(false);

        if (call.Reverted)
        {
            return false; // explicit “invalid”
        }

        var rv = call.ReturnData;
        return rv.Length == 4 && rv.SequenceEqual(magicBe);;
    }
}