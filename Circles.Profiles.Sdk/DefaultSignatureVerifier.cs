using System.Collections.Concurrent;
using Circles.Profiles.Interfaces;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Signer;

namespace Circles.Profiles.Sdk;

/// <inheritdoc cref="ISignatureVerifier"/>
public sealed class DefaultSignatureVerifier : ISignatureVerifier
{
    /* ───────────── constants ───────────── */

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

    /* ───────────── per‑instance caches ───────────── */

    private const int CacheCap = 512; // soft upper bound per verifier instance

    // address → true(contract) / false(EOA)
    private readonly ConcurrentDictionary<string, bool> _codeType =
        new(StringComparer.OrdinalIgnoreCase);

    private enum MagicVariant
    {
        Unknown = 0,
        Uses32 = 1,
        UsesBytes = 2
    }

    // address → last variant that returned magic
    private readonly ConcurrentDictionary<string, MagicVariant> _magic =
        new(StringComparer.OrdinalIgnoreCase);

    /* ───────────── fields ───────────── */

    private readonly IChainApi _chain;

    public DefaultSignatureVerifier(IChainApi chainApi)
        => _chain = chainApi ?? throw new ArgumentNullException(nameof(chainApi));

    /* ───────────── public API ───────────── */

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

        /* -------- 1) EOA or contract? (cached) -------- */
        bool isContract;
        if (_codeType.TryGetValue(signerAddress, out var cached))
        {
            isContract = cached;
        }
        else
        {
            string code = await _chain.GetCodeAsync(signerAddress, ct).ConfigureAwait(false);
            isContract = !string.IsNullOrEmpty(code) && code != "0x";
            InsertBounded(_codeType, signerAddress, isContract);
        }

        /* -------- 2) verify -------- */
        return isContract
            ? await VerifyContractAsync(hash, signerAddress, signature, ct).ConfigureAwait(false)
            : VerifyEoa(hash, signerAddress, signature);
    }

    /* ───────────── helpers ───────────── */

    #region EOA

    private static bool VerifyEoa(byte[] hash, string signerAddress, byte[] signature)
    {
        var sigHex = "0x" + signature.ToHex();
        var sig = EthECDSASignatureFactory.ExtractECDSASignature(sigHex);

        if (!sig.IsLowS) return false; // EIP‑2 guard

        string? recovered = EthECKey.RecoverFromSignature(sig, hash)?.GetPublicAddress();
        return recovered?.Equals(signerAddress, StringComparison.OrdinalIgnoreCase) == true;
    }

    #endregion

    #region Contract / ERC‑1271

    private async Task<bool> VerifyContractAsync(
        byte[] hash,
        string contract,
        byte[] signature,
        CancellationToken ct)
    {
        _magic.TryGetValue(contract, out var pref);

        // decide call order based on previous success
        if (pref == MagicVariant.Uses32 &&
            await Try1271Async(AbiBytes32, Magic32, contract, hash, signature, ct))
            return true;

        if (pref == MagicVariant.UsesBytes &&
            await Try1271Async(AbiBytes, Magic, contract, hash, signature, ct))
            return true;

        // fallbacks
        if (pref != MagicVariant.Uses32 &&
            await Try1271Async(AbiBytes32, Magic32, contract, hash, signature, ct))
            return true;

        if (pref != MagicVariant.UsesBytes &&
            await Try1271Async(AbiBytes, Magic, contract, hash, signature, ct))
            return true;

        return false;
    }

    private async Task<bool> Try1271Async(
        string abi,
        byte[] magic,
        string contract,
        byte[] dataOrHash,
        byte[] sig,
        CancellationToken ct)
    {
        SignatureCallResult call = await _chain
            .CallIsValidSignatureAsync(contract, abi, dataOrHash, sig, ct)
            .ConfigureAwait(false);

        if (call.Reverted) return false;

        bool ok = call.ReturnData.Length == 4 && call.ReturnData.SequenceEqual(magic);
        if (ok)
        {
            var v = ReferenceEquals(magic, Magic32)
                ? MagicVariant.Uses32
                : MagicVariant.UsesBytes;
            InsertBounded(_magic, contract, v);
        }

        return ok;
    }

    #endregion

    /* ───────────── generic bounded‑insert ───────────── */

    private static void InsertBounded<T>(
        ConcurrentDictionary<string, T> dict,
        string key,
        T value)
    {
        dict[key] = value;

        if (dict.Count <= CacheCap * 2) return; // soft cap – no lock

        int removed = 0;
        foreach (var k in dict.Keys)
        {
            if (removed++ >= CacheCap) break;
            dict.TryRemove(k, out _);
        }
    }
}