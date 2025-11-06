using System.Numerics;
using Circles.Profiles.Interfaces;
using Nethereum.ABI.FunctionEncoding; // SmartContractRevertException
using Nethereum.Hex.HexConvertors.Extensions; // HexToByteArray
using Nethereum.JsonRpc.Client;
using Nethereum.Web3;

namespace Circles.Profiles.Sdk;

/// <summary>Thin Nethereum-backed implementation of <see cref="IChainApi"/>.</summary>
public sealed class EthereumChainApi : IChainApi
{
    private readonly Web3 _web3;

    public BigInteger Id { get; }

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

    public async Task<System.Numerics.BigInteger> GetSafeNonceAsync(
        string safeAddress, CancellationToken ct = default)
    {
        const string abi = @"[
      {""constant"":true,""inputs"":[],
       ""name"":""nonce"",""outputs"":[{""name"":"""",""type"":""uint256""}],
       ""stateMutability"":""view"",""type"":""function""}
    ]";

        var contract = _web3.Eth.GetContract(abi, safeAddress);
        var fn = contract.GetFunction("nonce");
        return await fn.CallAsync<System.Numerics.BigInteger>(null, null, ct).ConfigureAwait(false);
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

            // Normal path (node picks default "from")
            var rv = await fn.CallAsync<byte[]>(
                latest,
                dataOrHash,
                signature).ConfigureAwait(false);

            if (rv == null || rv.Length < 4)
            {
                return new SignatureCallResult(Reverted: false, ReturnData: Array.Empty<byte>());
            }

            return new SignatureCallResult(Reverted: false, ReturnData: rv);
        }
        catch (SmartContractRevertException)
        {
            // Treat any revert as "invalid signature" and DO NOT throw.
            return new SignatureCallResult(Reverted: true, ReturnData: Array.Empty<byte>());
        }
        catch (RpcResponseException)
        {
            // Some clients surface reverts this way.
            return new SignatureCallResult(Reverted: true, ReturnData: Array.Empty<byte>());
        }
    }

    public async Task<SignatureCallResult> CallIsValidSignatureAsync(
        string toAddress,
        string abi,
        byte[] dataOrHash,
        byte[] signature,
        string? callFrom,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(toAddress))
        {
            throw new ArgumentException(nameof(toAddress));
        }

        if (string.IsNullOrWhiteSpace(abi))
        {
            throw new ArgumentException(nameof(abi));
        }

        ArgumentNullException.ThrowIfNull(dataOrHash);
        ArgumentNullException.ThrowIfNull(signature);

        var contract = _web3.Eth.GetContract(abi, toAddress);
        var fn = contract.GetFunction("isValidSignature");

        try
        {
            var latest = Nethereum.RPC.Eth.DTOs.BlockParameter.CreateLatest();

            if (callFrom is null)
            {
                var rv = await fn.CallAsync<byte[]>(latest, dataOrHash, signature).ConfigureAwait(false);
                if (rv == null || rv.Length < 4)
                {
                    return new SignatureCallResult(false, []);
                }

                return new SignatureCallResult(false, rv);
            }
            else
            {
                // IMPORTANT: use low-level eth_call to avoid Nethereum picking the params-object[] overload
                // and trying to ABI-encode CallInput as the first function arg.
                var callInput = fn.CreateCallInput(
                    callFrom,
                    new Nethereum.Hex.HexTypes.HexBigInteger(0),
                    new Nethereum.Hex.HexTypes.HexBigInteger(0),
                    dataOrHash, signature);

                var raw = await _web3.Eth.Transactions.Call
                    .SendRequestAsync(callInput, latest)
                    .ConfigureAwait(false);

                var rv = string.IsNullOrEmpty(raw) ? [] : raw.HexToByteArray();

                if (rv.Length < 4)
                {
                    return new SignatureCallResult(false, []);
                }

                return new SignatureCallResult(false, rv);
            }
        }
        catch (SmartContractRevertException)
        {
            return new SignatureCallResult(true, []);
        }
        catch (RpcResponseException ex) when (
            ex.Message.Contains("revert", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("execution reverted", StringComparison.OrdinalIgnoreCase))
        {
            return new SignatureCallResult(true, []);
        }
    }
}