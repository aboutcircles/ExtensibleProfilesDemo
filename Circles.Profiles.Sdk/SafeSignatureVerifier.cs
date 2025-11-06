using Circles.Profiles.Interfaces;

namespace Circles.Profiles.Sdk
{
    /// <summary>
    /// ERC-1271 verifier for Gnosis Safe (v1.4.1).
    /// We only support the canonical Safe path:
    ///   • RAW preimage bytes → ERC-1271(bytes, bytes)
    /// The Safe’s CompatibilityFallbackHandler checks the EIP-712 SafeMessage
    /// domain (chainId + verifyingContract) against the signed payload bytes.
    /// </summary>
    public sealed class SafeSignatureVerifier
    {
        private readonly IChainApi _chain;

        public SafeSignatureVerifier(IChainApi chain)
        {
            _chain = chain ?? throw new ArgumentNullException(nameof(chain));
        }

        /// <summary>
        /// Preferred Safe path: ERC-1271(bytes,bytes) over the raw canonical payload bytes.
        /// Forces <c>eth_call.from = &lt;safe&gt;</c> for Safe v1.4.1.
        /// </summary>
        public async Task<bool> Verify1271WithBytesAsync(
            string contract,
            byte[] payloadBytes,
            byte[] signature,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(payloadBytes);
            ArgumentNullException.ThrowIfNull(signature);
            if (string.IsNullOrWhiteSpace(contract)) { throw new ArgumentException(nameof(contract)); }

            var res = await _chain.CallIsValidSignatureAsync(
                toAddress: contract,
                abi: EthereumChainApi.ERC1271_BYTES_ABI,
                dataOrHash: payloadBytes,
                signature: signature,
                callFrom: contract, // Safe v1.4.1 prefers msg.sender = safe
                ct: ct).ConfigureAwait(false);

            if (!res.Reverted && res.ReturnData.AsSpan().Length >= 4 &&
                res.ReturnData.AsSpan(0, 4).SequenceEqual(EthereumChainApi.ERC1271_MAGIC_VALUE_BYTES))
            {
                return true;
            }

            return false;
        }
    }
}