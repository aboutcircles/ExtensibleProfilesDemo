using System.Numerics;
using Circles.Profiles.Interfaces;
using Circles.Profiles.Sdk.Utils;
using Moq;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Model;
using Nethereum.Signer;

namespace Circles.Profiles.Sdk.Tests;

[TestFixture]
public class DefaultSignatureVerifierTests
{
    private static byte[] Keccak(string msg) =>
        new Nethereum.Util.Sha3Keccack().CalculateHash(msg).HexToByteArray();

    [Test]
    public async Task EoaSignature_IsValid()
    {
        var key = EthECKey.GenerateKey();
        byte[] hash = Keccak("test");
        var sigStruct = key.SignAndCalculateV(hash);
        byte[] sig = sigStruct.To64ByteArray().Concat([sigStruct.V[0]]).ToArray();
        string addr = key.GetPublicAddress();

        var chain = new Mock<IChainApi>(MockBehavior.Strict);
        chain.Setup(c => c.GetCodeAsync(addr, It.IsAny<CancellationToken>()))
            .ReturnsAsync("0x");

        var verifier = new DefaultSignatureVerifier(chain.Object);
        bool ok = await verifier.VerifyAsync(hash, addr, sig);

        Assert.That(ok, Is.True);
    }

    [Test]
    public async Task Contract_Wallet_ReturnsTrue_OnMagic()
    {
        string contract = "0xCcCCccccCCCCcCCCCCCcCcCccCcCCCcCcccccccC";
        byte[] hash = Keccak("payload");
        byte[] sig = Enumerable.Range(1, 65).Select(b => (byte)b).ToArray();

        var chain = new Mock<IChainApi>(MockBehavior.Strict);
        chain.Setup(c => c.GetCodeAsync(contract, It.IsAny<CancellationToken>()))
            .ReturnsAsync("0x60006000");
        chain.Setup(c => c.Id)
            .Returns(new BigInteger(100));

        chain.Setup(c => c.CallIsValidSignatureAsync(
                contract,
                It.Is<string>(a => a.Contains("bytes32")),
                It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SignatureCallResult(false, "0x1626ba7e".HexToByteArray()));

        var verifier = new DefaultSignatureVerifier(chain.Object);
        bool ok = await verifier.VerifyAsync(hash, contract, sig);

        Assert.That(ok, Is.True);
        chain.VerifyAll();
    }

    [Test]
    public async Task Fallback_ToBytesVariant_When_First_Reverts()
    {
        string contract = "0x000000000000000000000000000000000000dEaD";
        byte[] hash = Keccak("abc");
        byte[] sig = Enumerable.Range(10, 65).Select(b => (byte)b).ToArray();

        var chain = new Mock<IChainApi>(MockBehavior.Strict);

        chain.Setup(c => c.GetCodeAsync(contract, It.IsAny<CancellationToken>()))
            .ReturnsAsync("0x60006000");
        chain.Setup(c => c.Id)
            .Returns(new BigInteger(100));

        // bytes32 path: simulate revert → forces fallback to "bytes"
        chain.Setup(c => c.CallIsValidSignatureAsync(
                contract,
                It.Is<string>(a => a.Contains("bytes32")),
                It.IsAny<byte[]>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SignatureCallResult(true, Array.Empty<byte>()));

        // bytes path: return MAGIC so verification succeeds
        chain.Setup(c => c.CallIsValidSignatureAsync(
                contract,
                It.Is<string>(a => a.Contains("\"bytes\"") && !a.Contains("bytes32")),
                It.IsAny<byte[]>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SignatureCallResult(false, "0x20c13b0b".HexToByteArray()));

        var verifier = new DefaultSignatureVerifier(chain.Object);
        bool ok = await verifier.VerifyAsync(hash, contract, sig);

        Assert.That(ok, Is.True);

        // must hit bytes path exactly once
        chain.Verify(c => c.CallIsValidSignatureAsync(
                contract,
                It.Is<string>(s => s.Contains("\"bytes\"") && !s.Contains("bytes32")),
                It.IsAny<byte[]>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public void EoaSignature_Invalid_Throws()
    {
        var key = EthECKey.GenerateKey();
        byte[] hash = Keccak("invalid");
        byte[] sig = new byte[65]; // zeroed – definitely invalid
        string addr = key.GetPublicAddress();

        var chain = new Mock<IChainApi>(MockBehavior.Strict);
        chain.Setup(c => c.GetCodeAsync(addr, It.IsAny<CancellationToken>()))
            .ReturnsAsync("0x"); // looks like EOA

        var verifier = new DefaultSignatureVerifier(chain.Object);

        Assert.ThrowsAsync<ArgumentException>(() => verifier.VerifyAsync(hash, addr, sig));
    }

    [Test]
    public async Task Contract_Wallet_ReturnsFalse_On_NonMagic()
    {
        string contract = "0xCcCCccccCCCCcCCCCCCcCcCccCcCCCcCcccccccC";
        byte[] hash = Keccak("bad‑sig");
        byte[] sig = Enumerable.Repeat((byte)7, 65).ToArray();

        var chain = new Mock<IChainApi>(MockBehavior.Strict);
        chain.Setup(c => c.GetCodeAsync(contract, It.IsAny<CancellationToken>()))
            .ReturnsAsync("0x60006000"); // contract code present
        chain.Setup(c => c.Id)
            .Returns(new BigInteger(100));
        chain.Setup(c => c.CallIsValidSignatureAsync(
                contract,
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SignatureCallResult(false, new byte[] { 0xde, 0xad, 0xbe, 0xef }));

        var verifier = new DefaultSignatureVerifier(chain.Object);
        bool ok = await verifier.VerifyAsync(hash, contract, sig);

        Assert.That(ok, Is.False);
    }

    [Test]
    public async Task Both_1271_Variants_Revert_ReturnsFalse()
    {
        string contract = "0x000000000000000000000000000000000000dEaD";
        byte[] hash = Keccak("revert‑test");
        byte[] sig = Enumerable.Repeat((byte)9, 65).ToArray();

        var chain = new Mock<IChainApi>(MockBehavior.Strict);
        chain.Setup(c => c.GetCodeAsync(contract, It.IsAny<CancellationToken>()))
            .ReturnsAsync("0x60006000");
        chain.Setup(c => c.Id)
            .Returns(new BigInteger(100));
        chain.Setup(c => c.CallIsValidSignatureAsync(
                contract,
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SignatureCallResult(true, Array.Empty<byte>()));

        var verifier = new DefaultSignatureVerifier(chain.Object);
        bool ok = await verifier.VerifyAsync(hash, contract, sig);

        Assert.That(ok, Is.False);
    }

    [Test]
    public async Task HighS_Signature_IsRejected()
    {
        var key = EthECKey.GenerateKey();
        byte[] hash = Sha3.Keccak256Bytes("malleability"u8);

        /* canonical (low‑S) signature */
        var lowSig = key.SignAndCalculateV(hash);
        byte[] sig64 = lowSig.To64ByteArray();

        /* bump S into the upper half‑order by flipping the MSB */
        sig64[32] |= 0x80; // first byte of S (big‑endian)

        /* append V to get the 65‑byte wire format */
        byte[] sigHighS = sig64.Concat([lowSig.V[0]]).ToArray();

        var chain = new Mock<IChainApi>(MockBehavior.Strict);
        chain.Setup(c => c.GetCodeAsync(key.GetPublicAddress(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("0x");

        var verifier = new DefaultSignatureVerifier(chain.Object);
        bool ok = await verifier.VerifyAsync(hash, key.GetPublicAddress(), sigHighS);

        Assert.That(ok, Is.False, "high‑S signature must be rejected");
    }
}