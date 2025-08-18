using System.Numerics;
using Circles.Profiles.Models;
using Circles.Profiles.Safe;
using Circles.Profiles.Sdk;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Signer;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;

namespace Circles.RealSafeE2E;

[TestFixture]
public sealed class SafeSignature_StandaloneTests
{
    private const string Rpc = "https://rpc.aboutcircles.com";
    private const int ChainId = 100; // Gnosis Chain (0x64)

    private Web3 _web3 = null!;
    private Account _deployer = null!;
    private Account _owner = null!;
    private string _safe = string.Empty;

    private EthereumChainApi _chain = null!;
    private SafeLinkSigner _signer = null!;

    private byte[] _payloadBytes = [];
    private byte[] _payloadKeccak = [];
    private byte[] _safeMessageHash = [];
    private byte[] _signature = [];

    [OneTimeSetUp]
    public async Task BootAsync()
    {
        var privateKey = Environment.GetEnvironmentVariable("PRIVATE_KEY") ??
                         throw new ArgumentException("The PRIVATE_KEY environment variable is not set");

        _deployer = new Account(privateKey, ChainId);
        _web3 = new Web3(_deployer, Rpc);

        var ownerKey = EthECKey.GenerateKey();
        _owner = new Account(ownerKey.GetPrivateKey(), ChainId);
        await SafeHelper.FundAsync(_web3, _deployer, _owner.Address, 0.001);

        _safe = await SafeHelper.DeploySafe141OnGnosisAsync(_web3, [_deployer.Address, _owner.Address],
            threshold: 1);

        _chain = new EthereumChainApi(_web3, new BigInteger(ChainId));
        _signer = new SafeLinkSigner(_safe, _chain);

        var draft = new CustomDataLink
        {
            Name = "mini-proof",
            Cid = "Qm111111111111111111111111111111111111111111",
            Encrypted = false,
            ChainId = ChainId,
            SignedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Nonce = CustomDataLink.NewNonce()
        };
        var signed = _signer.Sign(draft, _owner.PrivateKey);

        _payloadBytes = CanonicalJson.CanonicaliseWithoutSignature(signed);
        _payloadKeccak = Sha3.Keccak256Bytes(_payloadBytes);
        _safeMessageHash = SafeLinkSigner.ComputeSafeHash(_payloadKeccak, _chain.Id, _safe);
        _signature = signed.Signature.HexToByteArray();

        await TestContext.Out.WriteLineAsync($"safe      : {_safe}");
        await TestContext.Out.WriteLineAsync($"owner EOA : {_owner.Address}");
        await TestContext.Out.WriteLineAsync($"payloadKeccak     : 0x{_payloadKeccak.ToHex()}");
        await TestContext.Out.WriteLineAsync($"safeMessageHash   : 0x{_safeMessageHash.ToHex()}");
        await TestContext.Out.WriteLineAsync($"signature (65b)   : 0x{_signature.ToHex()}");
    }

    [Test]
    public async Task RawBytes_via_SafeFallback_succeeds()
    {
        bool ok = await Call1271MagicAsync(
            to: _safe,
            abi: EthereumChainApi.ERC1271_BYTES_ABI,
            dataOrHash: _payloadBytes,
            signature: _signature);

        Assert.That(ok, Is.True, "Safe fallback → handler should accept the owner EOA signature for raw bytes");
    }

    [Test]
    public async Task SafeSignatureVerifier_Bytes_succeeds()
    {
        var safeVerifier = new SafeSignatureVerifier(_chain);
        bool ok = await safeVerifier.VerifyAsync(_payloadBytes, _safe, _signature);
        Assert.That(ok, Is.True);
    }

    [Test]
    public async Task Bytes32_with_PayloadKeccak_fails()
    {
        var safeVerifier = new SafeSignatureVerifier(_chain);
        bool ok = await safeVerifier.VerifyAsync(_payloadKeccak, _safe, _signature); // 32-byte input → bytes32 path
        Assert.That(ok, Is.False);
    }

    [Test]
    public async Task Bytes32_with_SafeMessageHash_fails()
    {
        var safeVerifier = new SafeSignatureVerifier(_chain);
        bool ok = await safeVerifier.VerifyAsync(_safeMessageHash, _safe, _signature); // 32-byte input → bytes32 path
        Assert.That(ok, Is.False);
    }

    private async Task<bool> Call1271MagicAsync(
        string to,
        string abi,
        byte[] dataOrHash,
        byte[] signature)
    {
        var res = await _chain.CallIsValidSignatureAsync(to, abi, dataOrHash, signature);
        bool looksMagic = !res.Reverted && res.ReturnData is { Length: >= 4 } && (
            (ReferenceEquals(abi, EthereumChainApi.ERC1271_BYTES_ABI) &&
             res.ReturnData.SequenceEqual(EthereumChainApi.ERC1271_MAGIC_VALUE_BYTES)) ||
            (ReferenceEquals(abi, EthereumChainApi.ERC1271_BYTES32_ABI) &&
             res.ReturnData.SequenceEqual(EthereumChainApi.ERC1271_MAGIC_VALUE_BYTES32))
        );
        return looksMagic;
    }
}