using Circles.Sdk;
using Nethereum.ABI.FunctionEncoding;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.JsonRpc.Client;
using Nethereum.Web3;

namespace Circles.Profiles.Sdk;

file static class Abi
{
    public const string ContractAbi = @"[
                                           {
                                             ""type"": ""function"",
                                             ""name"": ""updateMetadataDigest"",
                                             ""inputs"": [
                                               {
                                                 ""name"": ""_metadataDigest"",
                                                 ""type"": ""bytes32"",
                                                 ""internalType"": ""bytes32""
                                               }
                                             ],
                                             ""outputs"": [],
                                             ""stateMutability"": ""nonpayable""
                                           }, {
                                             ""type"": ""function"",
                                             ""name"": ""getMetadataDigest"",
                                             ""inputs"": [
                                               {
                                                 ""name"": ""_avatar"",
                                                 ""type"": ""address"",
                                                 ""internalType"": ""address""
                                               }
                                             ],
                                             ""outputs"": [
                                               {
                                                 ""name"": """",
                                                 ""type"": ""bytes32"",
                                                 ""internalType"": ""bytes32""
                                               }
                                             ],
                                             ""stateMutability"": ""view""
                                           }
                                         ]";

    public const string ContractAddress = "0xA27566fD89162cC3D40Cb59c87AAaA49B85F3474";
}

/* DTO used by Nethereum */
[Function("updateMetadataDigest")]
file sealed class UpdateDigest : FunctionMessage
{
    [Parameter("bytes32", "metadataDigest", 1)]
    public byte[] MetadataDigest { get; set; } = [];
}

public sealed class NameRegistry : INameRegistry
{
    private readonly Web3 _web3;
    private readonly Contract _contract;

    public NameRegistry(string signerPrivKey, string rpcUrl)
    {
        var acct = new Nethereum.Web3.Accounts.Account(signerPrivKey);
        _web3 = new Web3(acct, rpcUrl);
        _contract = _web3.Eth.GetContract(Abi.ContractAbi, Abi.ContractAddress);
    }

    public async Task<string?> GetProfileCidAsync(string avatar, CancellationToken ct = default)
    {
        var func = _contract.GetFunction("getMetadataDigest");

        var digest = await func.CallAsync<byte[]>(avatar);
        return digest.All(b => b == 0) ? null : CidConverter.DigestToCid(digest);
    }

    public Task<string?> UpdateProfileCidAsync(
        string avatar,
        byte[] metadataDigest32,
        CancellationToken ct = default)
        => UpdateProfileCidAsync(avatar, (ReadOnlyMemory<byte>)metadataDigest32, ct);

    public async Task<string?> UpdateProfileCidAsync(string avatar,
        ReadOnlyMemory<byte> digest32,
        CancellationToken ct = default)
    {
        var handler = _web3.Eth.GetContractTransactionHandler<UpdateDigest>();
        var msg = new UpdateDigest
        {
            MetadataDigest = digest32.ToArray(),
            FromAddress = avatar
        };

        var receipt = await handler.SendRequestAndWaitForReceiptAsync(
            Abi.ContractAddress, msg, ct);
        return receipt.TransactionHash;
    }
}