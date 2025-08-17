using System.Collections.Concurrent;
using Circles.Profiles.Interfaces;

namespace Circles.Profiles.Sdk;

public sealed class DefaultSignatureVerifier : ISignatureVerifier, ISafeBytesVerifier
{
    private readonly IChainApi _chain;
    private readonly EoaSignatureVerifier _eoa;
    private readonly SafeSignatureVerifier _safe;

    private const int CacheCap = 512;

    private readonly ConcurrentDictionary<string, bool> _isContractCache =
        new(StringComparer.OrdinalIgnoreCase);

    public DefaultSignatureVerifier(IChainApi chain)
    {
        _chain = chain ?? throw new ArgumentNullException(nameof(chain));
        _eoa = new EoaSignatureVerifier();
        _safe = new SafeSignatureVerifier(chain);
    }

    public async Task<bool> VerifyAsync(
        byte[] hash, string signerAddress, byte[] signature, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(signature);

        bool emptyAddr = string.IsNullOrWhiteSpace(signerAddress);
        if (emptyAddr)
        {
            throw new ArgumentException("Empty address", nameof(signerAddress));
        }

        bool isContract = await IsContractAsync(signerAddress, ct).ConfigureAwait(false);
        if (isContract)
        {
            return await _safe.VerifyAsync(hash, signerAddress, signature, ct).ConfigureAwait(false);
        }

        return await _eoa.VerifyAsync(hash, signerAddress, signature, ct).ConfigureAwait(false);
    }

    public async Task<bool> Verify1271WithBytesAsync(
        byte[] payloadBytes, string signerAddress, byte[] signature, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payloadBytes);
        ArgumentNullException.ThrowIfNull(signature);

        bool emptyAddr = string.IsNullOrWhiteSpace(signerAddress);
        if (emptyAddr)
        {
            throw new ArgumentException("Empty address", nameof(signerAddress));
        }

        // 1) Try ERC‑1271 "bytes" on the signer unconditionally.
        //    If the signer is a Safe/contract, this returns the magic value.
        //    If it's an EOA, the call just returns non‑magic / empty and we continue.
        bool ok1271Bytes = await _safe.Verify1271WithBytesAsync(
            signerAddress, payloadBytes, signature, ct).ConfigureAwait(false);

        if (ok1271Bytes)
        {
            return true;
        }

        // 2) Fall back to EOA: verify ECDSA over keccak(payloadBytes).
        return await _eoa.Verify1271WithBytesAsync(
            payloadBytes, signerAddress, signature, ct).ConfigureAwait(false);
    }

    private async Task<bool> IsContractAsync(string address, CancellationToken ct)
    {
        bool cachedPresent = _isContractCache.TryGetValue(address, out bool cached);
        if (cachedPresent)
        {
            return cached;
        }

        string code = await _chain.GetCodeAsync(address, ct).ConfigureAwait(false);

        bool isEmpty = string.IsNullOrEmpty(code);
        bool isZero = string.Equals(code, "0x", StringComparison.OrdinalIgnoreCase);
        bool isContract = !(isEmpty || isZero);

        InsertBounded(_isContractCache, address, isContract);
        return isContract;
    }

    private static void InsertBounded(ConcurrentDictionary<string, bool> dict, string key, bool value)
    {
        dict[key] = value;

        bool tooBig = dict.Count > CacheCap * 2;
        if (!tooBig)
        {
            return;
        }

        int removed = 0;
        foreach (var k in dict.Keys)
        {
            if (removed++ >= CacheCap)
            {
                break;
            }

            dict.TryRemove(k, out _);
        }
    }
}