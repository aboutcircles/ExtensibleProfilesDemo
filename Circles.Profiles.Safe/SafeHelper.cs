using System.Numerics;
using Nethereum.ABI;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Util;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;

namespace Circles.Profiles.Safe;

/// <summary>
/// Thin helpers to (a) deploy a new Safe via the official proxy-factory
/// and (b) send a single-sig <c>execTransaction</c>.
/// Only the parts required for the profile E2E are implemented.
/// </summary>
public static class SafeHelper
{
    /* ---------- static addresses (Gnosis Chain, v1.4.1) ---------------- */
    internal const string ProxyFactory =
        "0x4e1dcf7ad4e460cfd30791ccc4f9c8a4f820ec67";

    internal const string SafeSingleton =
        "0x41675C099F32341bf84BFc5382aF534df5C7461a";

    internal const string NameRegistryAddress =
        "0xA27566fD89162cC3D40Cb59c87AAaA49B85F3474";

    const string CompatibilityFallbackHandler
        = "0xfd0732dc9E303F09fCEf3a7388Ad10A83459ec99";

    /* ---------- ABIs ---------------------------------------------------- */
    const string ProxyFactoryAbi = @"[
  { ""type"": ""function"", ""name"": ""createProxyWithNonce"",
    ""inputs"": [
      {""name"": ""_singleton"", ""type"": ""address""},
      {""name"": ""initializer"", ""type"": ""bytes""},
      {""name"": ""saltNonce"", ""type"": ""uint256""}
    ],
    ""outputs"": [{""name"": ""proxy"", ""type"": ""address""}],
    ""stateMutability"": ""nonpayable""
  },
  { ""anonymous"": false, ""type"": ""event"", ""name"": ""ProxyCreation"",
    ""inputs"": [
      {""indexed"": true,  ""name"": ""proxy"",     ""type"": ""address""},
      {""indexed"": false, ""name"": ""singleton"", ""type"": ""address""}
    ]
  }
]";

    // NOTE: include explicit `outputs` to satisfy Nethereum's ABI parser.
    const string SafeAbi = @"[
  { ""type"": ""function"", ""name"": ""setup"", ""stateMutability"": ""payable"",
    ""inputs"": [
      {""name"": ""_owners"", ""type"": ""address[]""},
      {""name"": ""_threshold"", ""type"": ""uint256""},
      {""name"": ""to"", ""type"": ""address""},
      {""name"": ""data"", ""type"": ""bytes""},
      {""name"": ""fallbackHandler"", ""type"": ""address""},
      {""name"": ""paymentToken"", ""type"": ""address""},
      {""name"": ""payment"", ""type"": ""uint256""},
      {""name"": ""paymentReceiver"", ""type"": ""address""}
    ],
    ""outputs"": []
  }
]";

    public static async Task<string> DeploySafe141OnGnosisAsync(
        Web3 web3, string[] owners, uint threshold, CancellationToken ct = default)
    {
        // 1) Owners must be UNIQUE and STRICTLY ascending by raw address
        var ownersSorted = owners
            .Select(Web3.ToChecksumAddress)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (ownersSorted.Length == 0) throw new ArgumentException("No owners");
        if (threshold == 0 || threshold > ownersSorted.Length)
            throw new ArgumentException("Invalid threshold");

        // 2) Build setup data (note the CompatibilityFallbackHandler)
        var safe = web3.Eth.GetContract(SafeAbi, SafeSingleton);
        var setup = safe.GetFunction("setup");
        var setupData = setup.GetData(
            ownersSorted,
            threshold,
            "0x0000000000000000000000000000000000000000", // to
            Array.Empty<byte>(), // data
            CompatibilityFallbackHandler, // fallbackHandler (required in practice on 1.4.1)
            "0x0000000000000000000000000000000000000000", // paymentToken
            0, // payment
            "0x0000000000000000000000000000000000000000" // paymentReceiver
        );

        // 3) Call createProxyWithNonce (salt can be any unique value)
        var factory = web3.Eth.GetContract(ProxyFactoryAbi, ProxyFactory);
        var create = factory.GetFunction("createProxyWithNonce");

        // Cheap deterministic salt derived from initializer
        var saltNonceU64 = new Sha3Keccack().CalculateHashFromHex(setupData)
            .Substring(2, 16)
            .Aggregate(0UL, (acc, c) => acc * 33 + (ulong)c);
        var saltNonce = new BigInteger(saltNonceU64);

        var txHash = await create.SendTransactionAsync(
            web3.TransactionManager.Account.Address, // sender
            new HexBigInteger(1_000_000), // gas
            new HexBigInteger(0), // value
            SafeSingleton, Convert.FromHexString(setupData.Substring(2)), saltNonce // args
        ).ConfigureAwait(false);

        var receipt = await WaitReceiptAsync(web3, txHash, ct).ConfigureAwait(false);

        if (receipt.Status == null || receipt.Status.Value == 0)
            throw new Exception("Safe deployment reverted");

        // 4) Read ProxyCreation event to get the new Safe address
        var creationEvt = factory.GetEvent("ProxyCreation");
        var decoded = creationEvt.DecodeAllEventsForEvent<ProxyCreationEventDTO>(receipt.Logs).SingleOrDefault();
        if (decoded == null) throw new Exception("ProxyCreation event not found in receipt");

        return decoded.Event.Proxy; // the newly created Safe address
    }

    [Event("ProxyCreation")]
    public class ProxyCreationEventDTO : IEventDTO
    {
        [Parameter("address", "proxy", 1, true)]
        public string Proxy { get; set; } = default!;

        [Parameter("address", "singleton", 2, false)]
        public string Singleton { get; set; } = default!;
    }

    /* ---------- single-sig execTransaction (unchanged) ----------------- */
    public static async Task ExecTransactionAsync(
        Web3 web3,
        string safe,
        Account signer,
        string to,
        byte[] data,
        CancellationToken ct = default)
    {
        Console.WriteLine($"[SafeHelpers] ExecTransaction: safe={safe} signer={signer.Address}");

        var txWeb3 = new Web3(signer, web3.Client);
        var executor = new SafeExecutor(txWeb3, safe);

        string tx = await executor.ExecTransactionAsync(to, data, ct: ct);
        Console.WriteLine($"[SafeHelpers]   execTransaction receipt → {tx}");
    }

    /* ---------- util helpers ------------------------------------------- */
    public static async Task FundAsync(
        Web3 web3, Account deployer, string to, decimal xDai, CancellationToken ct = default)
    {
        Console.WriteLine($"[SafeHelpers] Funding {to} with {xDai} xDAI");
        await web3.Eth.GetEtherTransferService()
            .TransferEtherAndWaitForReceiptAsync(to, xDai, cancellationToken: ct);
    }

    public static byte[] EncodeUpdateDigest(byte[] digest)
    {
        var abi = new ABIEncode();
        var selector = Sha3("updateMetadataDigest(bytes32)").Subarray(0, 4);
        return selector.Concat(abi.GetABIEncoded(new ABIValue("bytes32", digest))).ToArray();
    }

    private static byte[] Sha3(string s) =>
        new Sha3Keccack().CalculateHash(s).HexToByteArray();

    private static byte[] Subarray(this byte[] src, int off, int len)
    {
        var dst = new byte[len];
        Buffer.BlockCopy(src, off, dst, 0, len);
        return dst;
    }

    private static async Task<TransactionReceipt> WaitReceiptAsync(
        Web3 web3, string txHash, CancellationToken ct = default) =>
        await web3.TransactionManager.TransactionReceiptService
            .PollForReceiptAsync(txHash, ct);
}