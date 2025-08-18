using System.Numerics;
using Circles.Profiles.Interfaces;
using Nethereum.JsonRpc.Client;
using Nethereum.Web3;

namespace Circles.Profiles.Sdk;

/// <summary>Thin Nethereum‑backed implementation of <see cref="IChainApi"/>.</summary>
public sealed class EthereumChainApi : IChainApi
{
    private readonly Web3 _web3;

    public BigInteger Id { get; }

    // Canonical ERC-1271 ABIs with explicit outputs (Nethereum requires this)
    public const string ERC1271_BYTES32_ABI = @"[
  { ""type"": ""function"", ""name"": ""isValidSignature"", ""stateMutability"": ""view"",
    ""inputs"": [
      {""name"":""_hash"",""type"":""bytes32""},
      {""name"":""_signature"",""type"":""bytes""}
    ],
    ""outputs"": [{""name"":""magicValue"",""type"":""bytes4""}]
  }
]";

    public const string ERC1271_BYTES_ABI = @"[
  { ""type"": ""function"", ""name"": ""isValidSignature"", ""stateMutability"": ""view"",
    ""inputs"": [
      {""name"":""_data"",""type"":""bytes""},
      {""name"":""_signature"",""type"":""bytes""}
    ],
    ""outputs"": [{""name"":""magicValue"",""type"":""bytes4""}]
  }
]";

    public static readonly byte[] ERC1271_MAGIC_VALUE_BYTES32 = new byte[] { 0x16, 0x26, 0xBA, 0x7E }; // 0x1626ba7e
    public static readonly byte[] ERC1271_MAGIC_VALUE_BYTES = new byte[] { 0x20, 0xC1, 0x3B, 0x0B }; // 0x20c13b0b

    public EthereumChainApi(Web3 web3, BigInteger chainId)
    {
        _web3 = web3 ?? throw new ArgumentNullException(nameof(web3));
        Id = chainId;
    }

    public async Task<string> GetCodeAsync(string address, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException(nameof(address));

        // Explicit "latest" to satisfy nodes that require 2 params for eth_getCode
        var latest = Nethereum.RPC.Eth.DTOs.BlockParameter.CreateLatest();

        // Use the overload that takes the BlockParameter; ignore ct here (node param shape is the key issue)
        var code = await _web3.Eth.GetCode
            .SendRequestAsync(address, latest)
            .ConfigureAwait(false);

        // Normalize empty → "0x" for callers that just check emptiness
        return string.IsNullOrEmpty(code) ? "0x" : code;
    }

    public async Task<SignatureCallResult> CallIsValidSignatureAsync(
        string address,
        string abi,
        byte[] dataOrHash,
        byte[] signature,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new ArgumentException(nameof(address));
        }

        if (string.IsNullOrWhiteSpace(abi))
        {
            throw new ArgumentException(nameof(abi));
        }

        ArgumentNullException.ThrowIfNull(dataOrHash);
        ArgumentNullException.ThrowIfNull(signature);

        var contract = _web3.Eth.GetContract(abi, address);
        var fn = contract.GetFunction("isValidSignature");

        try
        {
            var latest = Nethereum.RPC.Eth.DTOs.BlockParameter.CreateLatest();

            var rv = await fn.CallAsync<byte[]>(
                    latest,
                    dataOrHash,
                    signature)
                .ConfigureAwait(false);

            if (rv == null || rv.Length < 4)
            {
                return new SignatureCallResult(Reverted: false, ReturnData: []);
            }

            return new SignatureCallResult(Reverted: false, ReturnData: rv);
        }
        catch (RpcResponseException)
        {
            // Treat any eth_call execution error (including generic "VM execution error.: eth_call")
            // as an ERC-1271 "invalid signature" result. Never throw on invalid.
            return new SignatureCallResult(Reverted: true, ReturnData: []);
        }
    }

    public async Task<BigInteger> GetSafeNonceAsync(
        string safeAddress, CancellationToken ct = default)
    {
        const string abi = @"[
      {""constant"":true,""inputs"":[],
       ""name"":""nonce"",""outputs"":[{""name"":"""",""type"":""uint256""}],
       ""stateMutability"":""view"",""type"":""function""}
    ]";

        var contract = _web3.Eth.GetContract(abi, safeAddress);
        var fn = contract.GetFunction("nonce");
        return await fn.CallAsync<BigInteger>(null, null, ct).ConfigureAwait(false);
    }

    public async Task<SignatureCallResult> CallIsValidSignatureAsync(
        string toAddress,
        string abi,
        byte[] dataOrHash,
        byte[] signature,
        string? callFrom,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(toAddress)) throw new ArgumentException(nameof(toAddress));
        if (string.IsNullOrWhiteSpace(abi)) throw new ArgumentException(nameof(abi));
        ArgumentNullException.ThrowIfNull(dataOrHash);
        ArgumentNullException.ThrowIfNull(signature);

        var contract = _web3.Eth.GetContract(abi, toAddress);
        var fn = contract.GetFunction("isValidSignature");

        try
        {
            var latest = Nethereum.RPC.Eth.DTOs.BlockParameter.CreateLatest();

            if (callFrom is null)
            {
                // Original path: node fills default "from"
                var rv = await fn.CallAsync<byte[]>(latest, dataOrHash, signature).ConfigureAwait(false);
                if (rv == null || rv.Length < 4) return new SignatureCallResult(false, Array.Empty<byte>());
                return new SignatureCallResult(false, rv);
            }
            else
            {
                // Safe 1.4.1 path: force eth_call.from = <safe>
                var callInput = fn.CreateCallInput(
                    callFrom,
                    new Nethereum.Hex.HexTypes.HexBigInteger(0),
                    new Nethereum.Hex.HexTypes.HexBigInteger(0),
                    dataOrHash, signature);

                var rv = await fn.CallAsync<byte[]>(callInput, latest).ConfigureAwait(false);
                if (rv == null || rv.Length < 4) return new SignatureCallResult(false, Array.Empty<byte>());
                return new SignatureCallResult(false, rv);
            }
        }
        catch (RpcResponseException ex) when (
            ex.Message.Contains("revert", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("execution reverted", StringComparison.OrdinalIgnoreCase))
        {
            return new SignatureCallResult(true, Array.Empty<byte>());
        }
    }
}