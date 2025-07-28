using System.Numerics;
using Circles.Profiles.Interfaces;
using Nethereum.Contracts;
using Nethereum.JsonRpc.Client;
using Nethereum.Web3;

namespace Circles.Profiles.Sdk;

/// <summary>Thin Nethereum‑backed implementation of <see cref="IChainApi"/>.</summary>
public sealed class EthereumChainApi : IChainApi
{
    private readonly Web3 _web3;

    public BigInteger Id { get; }

    public EthereumChainApi(Web3 web3, BigInteger chainId)
    {
        _web3 = web3 ?? throw new ArgumentNullException(nameof(web3));
        Id = chainId;
    }

    public async Task<string> GetCodeAsync(string address, CancellationToken ct = default) =>
        await _web3.Eth.GetCode.SendRequestAsync(address, ct).ConfigureAwait(false);

    public async Task<SignatureCallResult> CallIsValidSignatureAsync(
        string address,
        string abi,
        byte[] dataOrHash,
        byte[] signature,
        CancellationToken ct = default)
    {
        var contract = _web3.Eth.GetContract(abi, address);
        var fn = contract.GetFunction("isValidSignature");

        try
        {
            var rv = await fn.CallAsync<byte[]>(
                new object[] { dataOrHash, signature },
                null,
                ct).ConfigureAwait(false);

            /* Treat empty / short return values as “invalid signature” */
            if (rv.Length < 4)
                return new SignatureCallResult(false, Array.Empty<byte>());

            return new SignatureCallResult(false, rv);
        }
        catch (RpcResponseException ex) when (
            ex.Message.Contains("revert", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("execution reverted", StringComparison.OrdinalIgnoreCase))
        {
            // Solidity revert → “invalid signature”
            return new SignatureCallResult(true, []);
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
}