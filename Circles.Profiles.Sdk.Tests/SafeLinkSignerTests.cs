using Circles.Profiles.Interfaces;
using Circles.Profiles.Models;
using Moq;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Signer;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;

namespace Circles.Profiles.Sdk.Tests;

[TestFixture]
public class SafeLinkSignerTests
{
    [Test]
    public async Task Sign_Uses_Safe_Address_And_Verifies()
    {
        string ownerPriv = EthECKey.GenerateKey().GetPrivateKey();
        string safeAddr  = "0x1234567890aBcDEF1234567890abCDef12345678";

        Account account = new Account(ownerPriv);
        Web3 web3 = new Web3(account, "https://rpc.aboutcircles.com");
        IChainApi chainApi = new EthereumChainApi(web3, 100);
        
        var signer = new SafeLinkSigner(safeAddr, chainApi);
        var link   = new CustomDataLink { Name = "hello", Cid = "CID-x" };
        var signed = signer.Sign(link, ownerPriv);

        Assert.That(signed.SignerAddress, Is.EqualTo(safeAddr));

        /* verify off‑chain via DefaultSignatureVerifier with mocked 1271 happy path */
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