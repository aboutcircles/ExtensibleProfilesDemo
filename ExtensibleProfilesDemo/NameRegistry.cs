using Nethereum.ABI.FunctionEncoding;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.JsonRpc.Client;
using Nethereum.Web3;

namespace ExtensibleProfilesDemo;

[Function("updateMetadataDigest")]
public class UpdateMetadataDigestFunction : FunctionMessage
{
    [Parameter("bytes32", "metadataDigest", 2)]
    public byte[] MetadataDigest { get; set; } = [];
}

internal sealed class NameRegistry
{
    private readonly Web3 _web3;
    private readonly Contract _contract;

    public NameRegistry(string privateKey)
    {
        var acct = new Nethereum.Web3.Accounts.Account(privateKey);
        _web3 = new Web3(acct, Config.RpcUrl);
        _contract = _web3.Eth.GetContract(Config.NameRegistryAbi, Config.NameRegistryAddress);
    }

    /// <summary>
    /// Tries the on-chain call up to three times (with back-off) before giving up.
    /// </summary>
    public async Task<string?> GetProfileCidAsync(string userAddress)
    {
        var func = _contract.GetFunction("getMetadataDigest");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                byte[] dig = await func.CallAsync<byte[]>(userAddress)
                    .ConfigureAwait(false);

                return dig.All(b => b == 0)
                    ? null
                    : CidConverter.DigestToCid(dig);
            }
            catch (RpcClientTimeoutException) when (attempt < 2)
            {
                // exponential back-off – 1 s, 2 s
                await Task.Delay(TimeSpan.FromSeconds(1 << attempt));
            }
        }

        // last attempt failed → re-throw to caller
        throw new TimeoutException("Timeout waiting for profile cid from chain rpc");
    }

    public async Task<string?> UpdateProfileCidAsync(byte[] digest32)
    {
        var handler = _web3.Eth.GetContractTransactionHandler<UpdateMetadataDigestFunction>();

        var tx = new UpdateMetadataDigestFunction
        {
            MetadataDigest = digest32,
            FromAddress = _web3.TransactionManager.Account.Address
        };

        try
        {
            var receipt = await handler.SendRequestAndWaitForReceiptAsync(
                Config.NameRegistryAddress, tx);

            Console.WriteLine($"Tx mined in block {receipt.BlockNumber.Value}");
            return receipt.TransactionHash;
        }
        catch (SmartContractRevertException e)
        {
            Console.WriteLine($"⚠️  Contract revert: {e.RevertMessage}");
            return null;
        }
    }
}