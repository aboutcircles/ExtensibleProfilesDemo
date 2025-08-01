using Circles.Profiles.Interfaces;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Web3;

namespace Circles.Profiles.Sdk;

file static class Abi
{
    public const string ContractAbi = """
                                      [
                                        {"type":"function","name":"updateMetadataDigest",
                                         "inputs":[{"type":"bytes32","name":"_metadataDigest"}],
                                         "outputs":[],"stateMutability":"nonpayable"},
                                        {"type":"function","name":"getMetadataDigest",
                                         "inputs":[{"type":"address","name":"_avatar"}],
                                         "outputs":[{"type":"bytes32"}],
                                         "stateMutability":"view"}
                                      ]
                                      """;

    public const string ContractAddress = "0xA27566fD89162cC3D40Cb59c87AAaA49B85F3474";
}

[Function("updateMetadataDigest")]
file sealed class UpdateDigest : FunctionMessage
{
    [Parameter("bytes32", "metadataDigest", 1)]
    public byte[] MetadataDigest { get; set; } = [];
}

/// <summary>
/// On‑chain name‑registry client with a guard that prevents
/// “EOA tries to update Safe profile” foot‑guns. 
/// </summary>
public sealed class NameRegistry : INameRegistry
{
    private readonly Web3 _web3;
    private readonly Contract _contract;
    private readonly string _signer;

    public NameRegistry(string signerPrivKey, string rpcUrl)
    {
        var acct = new Nethereum.Web3.Accounts.Account(signerPrivKey);
        _signer = acct.Address;
        _web3 = new Web3(acct, rpcUrl);
        _contract = _web3.Eth.GetContract(Abi.ContractAbi, Abi.ContractAddress);
    }

    /* ───────────────── read ───────────────── */

    public async Task<string?> GetProfileCidAsync(string avatar, CancellationToken _ = default)
    {
        var fn = _contract.GetFunction("getMetadataDigest");
        var digest = await fn.CallAsync<byte[]>(avatar);
        return digest.All(b => b == 0) ? null : CidConverter.DigestToCid(digest);
    }

    /* ───────────────── write (EOA path) ───────────────── */

    public Task<string?> UpdateProfileCidAsync(string avatar, byte[] digest32,
        CancellationToken ct = default) =>
        UpdateProfileCidAsync(avatar, (ReadOnlyMemory<byte>)digest32, ct);

    public async Task<string?> UpdateProfileCidAsync(string avatar,
        ReadOnlyMemory<byte> digest32,
        CancellationToken ct = default)
    {
        if (!avatar.Equals(_signer, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"avatar ({avatar}) must equal tx‑signer ({_signer}). " +
                "For Safe accounts wrap the call in execTransaction.");

        var handler = _web3.Eth.GetContractTransactionHandler<UpdateDigest>();
        var tx = new UpdateDigest
        {
            MetadataDigest = digest32.ToArray(),
            FromAddress = _signer
        };

        var receipt = await handler.SendRequestAndWaitForReceiptAsync(
            Abi.ContractAddress, tx, ct);
        return receipt.TransactionHash;
    }
}