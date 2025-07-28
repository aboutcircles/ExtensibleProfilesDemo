using System.Numerics;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using Nethereum.Model;
using Nethereum.Signer;
using Nethereum.Web3;

namespace Circles.Profiles.Sdk;

/// <summary>
/// Executes a single‑sig <c>execTransaction</c> on a Gnosis Safe.
/// Assumes a **single owner** Safe, <c>operation = CALL</c>,
/// and that the owner account (EOA) is the <see cref="Web3.TransactionManager"/> account.
/// </summary>
public sealed class GnosisSafeExecutor
{
    /* ───────────── embedded ABI (v1.3.0) ───────────── */
    private const string SafeAbi = """
                                   [
                                     { "type":"function","name":"nonce",
                                       "inputs":[],"outputs":[{ "type":"uint256" }],
                                       "stateMutability":"view" },
                                     { "type":"function","name":"getTransactionHash",
                                       "inputs":[
                                         { "type":"address","name":"to" },
                                         { "type":"uint256","name":"value" },
                                         { "type":"bytes","name":"data" },
                                         { "type":"uint8","name":"operation" },
                                         { "type":"uint256","name":"safeTxGas" },
                                         { "type":"uint256","name":"baseGas" },
                                         { "type":"uint256","name":"gasPrice" },
                                         { "type":"address","name":"gasToken" },
                                         { "type":"address","name":"refundReceiver" },
                                         { "type":"uint256","name":"nonce" }
                                       ],
                                       "outputs":[{ "type":"bytes32" }],
                                       "stateMutability":"view" },
                                     { "type":"function","name":"execTransaction",
                                       "inputs":[
                                         { "type":"address","name":"to" },
                                         { "type":"uint256","name":"value" },
                                         { "type":"bytes","name":"data" },
                                         { "type":"uint8","name":"operation" },
                                         { "type":"uint256","name":"safeTxGas" },
                                         { "type":"uint256","name":"baseGas" },
                                         { "type":"uint256","name":"gasPrice" },
                                         { "type":"address","name":"gasToken" },
                                         { "type":"address","name":"refundReceiver" },
                                         { "type":"bytes","name":"signatures" }
                                       ],
                                       "outputs":[{ "type":"bool" }],
                                       "stateMutability":"payable" }
                                   ]
                                   """;

    /* ───────────────────────── fields ───────────────────────── */
    private readonly Web3 _web3;
    private readonly Contract _safe;

    public GnosisSafeExecutor(Web3 web3, string safeAddress)
    {
        _web3 = web3 ?? throw new ArgumentNullException(nameof(web3));
        _safe = _web3.Eth.GetContract(SafeAbi, safeAddress);
    }

    /// <summary>
    /// Executes <paramref name="data"/> on <paramref name="to"/> via the Safe
    /// and waits for the receipt.
    /// </summary>
    /// <param name="to">Target contract / EOA.</param>
    /// <param name="data">Calldata for the target.</param>
    /// <param name="value">Wei to forward (default 0).</param>
    /// <param name="safeTxGas">Inner tx gas (default 150 000 units).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<string> ExecTransactionAsync(
        string to,
        byte[] data,
        BigInteger value = default,
        BigInteger safeTxGas = default,
        CancellationToken ct = default)
    {
        if (safeTxGas == default) safeTxGas = new BigInteger(150_000);

        const byte   CALL      = 0;
        const string ZERO_ADDR = "0x0000000000000000000000000000000000000000";
        BigInteger   zero      = BigInteger.Zero;

        /* ─── 1) obtain nonce + hash ─────────────────────────── */
        var nonceFn = _safe.GetFunction("nonce");
        BigInteger nonce = await nonceFn.CallAsync<BigInteger>().ConfigureAwait(false);

        var hashFn = _safe.GetFunction("getTransactionHash");
        byte[] txHash = await hashFn.CallAsync<byte[]>(
            to, value, data, CALL,
            safeTxGas, zero, zero,
            ZERO_ADDR, ZERO_ADDR, nonce).ConfigureAwait(false);

        /* ─── 2) EOA signs hash ─────────────────────────────── */
        var acct = (Nethereum.Web3.Accounts.Account)_web3.TransactionManager.Account;
        var key  = new EthECKey(acct.PrivateKey);
        var sig  = key.SignAndCalculateV(txHash);

        // 65‑byte packed (R || S || V)
        byte[] sigBytes = sig.To64ByteArray().Concat([sig.V[0]]).ToArray();

        /* ─── 3) execTransaction ────────────────────────────── */
        var execFn = _safe.GetFunction("execTransaction");
        var receipt = await execFn.SendTransactionAndWaitForReceiptAsync(
            acct.Address,
            new HexBigInteger(600_000),          // outer tx gas
            new HexBigInteger(0),                // value
            ct,
            to, value, data, CALL,
            safeTxGas, zero, zero,
            ZERO_ADDR, ZERO_ADDR,
            sigBytes).ConfigureAwait(false);

        return receipt.TransactionHash;
    }
}
