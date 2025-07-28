using System.Numerics;
using Nethereum.ABI;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Util;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using Circles.Profiles.Sdk;

namespace Circles.RealSafeE2E;

/// <summary>
/// Thin helpers to (a) deploy a new Safe via the official proxy‑factory
/// and (b) send a single‑sig <c>execTransaction</c>.
/// Only the parts required for the profile E2E are implemented.
/// </summary>
internal static class SafeHelpers
{
    /* ---------- static addresses (Gnosis Chain, v1.3.0) ---------------- */
    internal const string ProxyFactory =
        "0xa6B71E26C5e0845f74c812102Ca7114b6a896AB2";

    internal const string SafeSingleton =
        "0x3e5c63644e683549055b9be8653de26e0b4cd36e";

    internal const string NameRegistryAddress =
        "0xA27566fD89162cC3D40Cb59c87AAaA49B85F3474";

    /* ---------- ABIs ---------------------------------------------------- */

    private const string ProxyFactoryAbi =
        """[ { "type":"function", "name":"createProxy", "inputs":[ { "name":"_singleton","type":"address" }, { "name":"initializer","type":"bytes" } ], "outputs":[{ "name":"proxy","type":"address" }], "stateMutability":"nonpayable" }, { "anonymous":false,"type":"event","name":"ProxyCreation", "inputs":[ { "indexed":true,"name":"proxy","type":"address" }, { "indexed":false,"name":"singleton","type":"address" } ] } ]""";

    private const string SafeAbi = """
                                   [
                                     {
                                       "type": "function",
                                       "name": "setup",
                                       "inputs": [
                                         { "type": "address[]", "name": "_owners" },
                                         { "type": "uint256",   "name": "_threshold" },
                                         { "type": "address",   "name": "to" },
                                         { "type": "bytes",     "name": "data" },
                                         { "type": "address",   "name": "fallbackHandler" },
                                         { "type": "address",   "name": "paymentToken" },
                                         { "type": "uint256",   "name": "payment" },
                                         { "type": "address",   "name": "paymentReceiver" }
                                       ],
                                       "outputs": [],
                                       "stateMutability": "payable"
                                     },
                                     {
                                       "type": "function",
                                       "name": "nonce",
                                       "stateMutability": "view",
                                       "inputs": [],
                                       "outputs": [ { "type": "uint256" } ]
                                     }
                                   ]
                                   """;

    /* ---------- Safe deployment ---------------------------------------- */

    internal static async Task<string> DeploySafeAsync(
        Web3 web3,
        string[] owners,
        uint threshold,
        CancellationToken ct = default)
    {
        Console.WriteLine($"[SafeHelpers] Deploying Safe – owners={string.Join(',', owners)}");

        var factory = web3.Eth.GetContract(ProxyFactoryAbi, ProxyFactory);

        var setupFn = web3.Eth.GetContract(SafeAbi, SafeSingleton).GetFunction("setup");
        byte[] setupCalldata = setupFn.GetData(
            owners, threshold,
            "0x0000000000000000000000000000000000000000", Array.Empty<byte>(),
            "0x0000000000000000000000000000000000000000",
            "0x0000000000000000000000000000000000000000",
            BigInteger.Zero,
            "0x0000000000000000000000000000000000000000").HexToByteArray();

        var createProxy = factory.GetFunction("createProxy");
        string txHash = await createProxy.SendTransactionAsync(
            from: owners[0],
            gas: new HexBigInteger(3_000_000),
            value: new HexBigInteger(0),
            functionInput: new object[] { SafeSingleton, setupCalldata });

        Console.WriteLine($"[SafeHelpers] createProxy tx → {txHash}");
        var receipt = await WaitReceiptAsync(web3, txHash, ct);

        var proxyDeployedLog = receipt.Logs.Single(o =>
            ((string)o.Topics[0]) == "0x4f51faf6c4561ff95f067657e43439f0f856d97c04d9ec9070a6199ad418e235");

        var dataHex = proxyDeployedLog.Data;
        byte[] dataBytes = dataHex.HexToByteArray();

        byte[] proxyAddressBytes = dataBytes.Skip(12).Take(20).ToArray();
        string proxy = "0x" + proxyAddressBytes.ToHex();

        Console.WriteLine($"[SafeHelpers] Deployed Safe @ {proxy}");
        return proxy;
    }

    /* ---------- single‑sig execTransaction ----------------------------- */
    /// <summary>
    /// Wrapper that delegates to <see cref="GnosisSafeExecutor"/>
    /// (now the canonical implementation in the SDK).
    /// </summary>
    internal static async Task ExecTransactionAsync(
        Web3 web3,
        string safe,
        Account signer,
        string to,
        byte[] data,
        CancellationToken ct = default)
    {
        Console.WriteLine($"[SafeHelpers] ExecTransaction: safe={safe} signer={signer.Address}");

        // we need a Web3 instance whose tx‑signer is the Safe owner EOA
        var txWeb3 = new Web3(signer, web3.Client);
        var executor = new GnosisSafeExecutor(txWeb3, safe);

        string tx = await executor.ExecTransactionAsync(to, data, ct: ct);
        Console.WriteLine($"[SafeHelpers]   execTransaction receipt → {tx}");
    }

    /* ---------- util helpers (unchanged) ------------------------------- */

    internal static async Task FundAsync(
        Web3 web3, Account deployer, string to, double xDai, CancellationToken ct = default)
    {
        Console.WriteLine($"[SafeHelpers] Funding {to} with {xDai} xDAI");
        await web3.Eth.GetEtherTransferService()
            .TransferEtherAndWaitForReceiptAsync(to, (decimal)xDai, cancellationToken: ct);
    }

    internal static byte[] EncodeUpdateDigest(byte[] digest)
    {
        var abi = new ABIEncode();
        var selector = sha3("updateMetadataDigest(bytes32)").Subarray(0, 4);
        return selector.Concat(abi.GetABIEncoded(new ABIValue("bytes32", digest))).ToArray();
    }

    private static byte[] sha3(string s) =>
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