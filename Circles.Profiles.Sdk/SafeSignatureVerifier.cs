using Circles.Profiles.Interfaces;

namespace Circles.Profiles.Sdk
{
    /// <summary>
    /// ERC‑1271 verifier for Gnosis Safe (v1.4.1).
    /// Calls isValidSignature on the SAFE address (its fallback delegates to the CompatibilityFallbackHandler).
    /// Semantics:
    ///   • RAW preimage bytes → ERC‑1271(bytes, bytes)  ← canonical Safe message path
    ///   • 32‑byte input      → ERC‑1271(bytes32, bytes) (wraps to bytes via abi.encode(hash); rarely useful for us)
    /// </summary>
    public sealed class SafeSignatureVerifier
    {
        private readonly IChainApi _chain;

        public SafeSignatureVerifier(IChainApi chain)
        {
            _chain = chain ?? throw new ArgumentNullException(nameof(chain));
        }

        /// <summary>
        /// If <paramref name="dataOrHash"/> is 32 bytes, tries the bytes32 overload (which wraps to bytes with abi.encode(hash)).
        /// If it's not 32 bytes, treats it as RAW payload bytes and calls the bytes overload (canonical Safe path).
        /// </summary>
        public async Task<bool> VerifyAsync(
            byte[] dataOrHash,
            string safeAddress,
            byte[] signature,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(dataOrHash);
            ArgumentNullException.ThrowIfNull(signature);

            bool emptyAddr = string.IsNullOrWhiteSpace(safeAddress);
            if (emptyAddr)
            {
                throw new ArgumentException("Empty address", nameof(safeAddress));
            }

            bool isHash32 = dataOrHash.Length == 32;
            if (isHash32)
            {
                // First try the bytes32 overload
                bool ok32 = await Try1271Async(
                    EthereumChainApi.ERC1271_BYTES32_ABI,
                    EthereumChainApi.ERC1271_MAGIC_VALUE_BYTES32,
                    safeAddress,
                    dataOrHash,
                    signature,
                    ct).ConfigureAwait(false);

                if (ok32)
                {
                    return true;
                }

                // Fallback: some wallets only implement the bytes overload reliably.
                return await Try1271Async(
                    EthereumChainApi.ERC1271_BYTES_ABI,
                    EthereumChainApi.ERC1271_MAGIC_VALUE_BYTES,
                    safeAddress,
                    dataOrHash,
                    signature,
                    ct).ConfigureAwait(false);
            }

            return await Try1271Async(
                EthereumChainApi.ERC1271_BYTES_ABI,
                EthereumChainApi.ERC1271_MAGIC_VALUE_BYTES,
                safeAddress,
                dataOrHash,
                signature,
                ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Explicit bytes path (preferred for Safe messages).
        /// </summary>
        public Task<bool> Verify1271WithBytesAsync(
            string safeAddress,
            byte[] data,
            byte[] signature,
            CancellationToken ct = default) =>
            Try1271Async(
                EthereumChainApi.ERC1271_BYTES_ABI,
                EthereumChainApi.ERC1271_MAGIC_VALUE_BYTES,
                safeAddress,
                data,
                signature,
                ct);

        private async Task<bool> Try1271Async(
            string abi,
            byte[] magic,
            string contract,
            byte[] dataOrHash,
            byte[] sig,
            CancellationToken ct)
        {
            var resp = await _chain.CallIsValidSignatureAsync(contract, abi, dataOrHash, sig, ct)
                .ConfigureAwait(false);

            bool returnedMagic =
                !resp.Reverted &&
                resp.ReturnData is { Length: >= 4 } &&
                resp.ReturnData.Take(4).SequenceEqual(magic);

            if (returnedMagic)
            {
                return true;
            }

            bool looksEcdsa65 = sig.Length == 65;
            if (!looksEcdsa65)
            {
                return false;
            }

            var toggled = (byte[])sig.Clone();
            bool vIs27or28 = toggled[64] is 27 or 28;
            bool vIs31or32 = toggled[64] is 31 or 32;

            if (vIs27or28)
            {
                toggled[64] += 4;
            }
            else if (vIs31or32)
            {
                toggled[64] -= 4;
            }
            else
            {
                return false;
            }

            var resp2 = await _chain.CallIsValidSignatureAsync(contract, abi, dataOrHash, toggled, ct)
                .ConfigureAwait(false);

            bool returnedMagic2 =
                !resp2.Reverted &&
                resp2.ReturnData is { Length: >= 4 } &&
                resp2.ReturnData.Take(4).SequenceEqual(magic);

            return returnedMagic2;
        }
    }
}