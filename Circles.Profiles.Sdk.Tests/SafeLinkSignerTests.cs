using Circles.Profiles.Interfaces;
using Circles.Profiles.Models.Core;
using Circles.Profiles.Sdk.Utils;
using Moq;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Signer;

namespace Circles.Profiles.Sdk.Tests;

[TestFixture]
public class SafeSignerTests
{
    [Test]
    public async Task Sign_Uses_Safe_Address_And_Verifies()
    {
        var ownerKey = EthECKey.GenerateKey();
        string safeAddr  = "0x1234567890aBcDEF1234567890abCDef12345678";

        ISigner signer = new SafeSigner(safeAddr, ownerKey);
        var ipfs = new Mocks.InMemoryIpfsStore();
        var prof = new Models.Core.Profile();
        var writer = await NamespaceWriter.CreateAsync(prof, "any", ipfs, signer);
        var signed = await writer.AttachExistingCidAsync("hello", "CID-x");

        Assert.That(signed.SignerAddress, Is.EqualTo(safeAddr).IgnoreCase);

        /* verify off-chain via DefaultSignatureVerifier with mocked 1271 happy path */
        var chain = new Mock<IChainApi>();
        chain.Setup(c => c.GetCodeAsync(safeAddr, It.IsAny<CancellationToken>()))
            .ReturnsAsync("0x60006000");
        chain.Setup(c => c.CallIsValidSignatureAsync(
                safeAddr,
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SignatureCallResult(false, "0x1626ba7e".HexToByteArray()));

        var verifier = new DefaultSignatureVerifier(chain.Object);
        byte[] hash  = Sha3.Keccak256Bytes(CanonicalJson.CanonicaliseWithoutSignature(signed).AsSpan());
        bool ok      = await verifier.VerifyAsync(hash, safeAddr, signed.Signature.HexToByteArray());

        Assert.That(ok, Is.True);
    }
}