using Circles.Profiles.Models;
using Nethereum.Signer;

namespace Circles.Profiles.Sdk.Tests;

[TestFixture]
public class DefaultLinkSignerTests
{
    private readonly string _priv = EthECKey.GenerateKey().GetPrivateKey();
    private readonly DefaultLinkSigner _sut = new();

    private CustomDataLink MakeLink() => new()
    {
        Name = "greeting",
        Cid = "CID-dummy"
    };

    [Test]
    public void Sign_Populates_Address_And_Signature()
    {
        var signed = _sut.Sign(MakeLink(), _priv);

        Assert.That(signed.Signature, Does.StartWith("0x"));
        Assert.That(signed.SignerAddress, Does.StartWith("0x"));
        Assert.That(signed.Signature.Length, Is.EqualTo(132)); // 65 bytes hex + 0x
        Assert.That(signed.SignerAddress.Length, Is.EqualTo(42)); // 20 bytes hex + 0x
    }

    [Test]
    public void Verify_ValidSignature_IsTrue()
    {
        var signed = _sut.Sign(MakeLink(), _priv);
        Assert.That(_sut.Verify(signed), Is.True);
    }

    [Test]
    public void Verify_ModifiedPayload_IsFalse()
    {
        var signed = _sut.Sign(MakeLink(), _priv) with { Cid = "CID-evil" };
        Assert.That(_sut.Verify(signed), Is.False);
    }

    [Test]
    public void Verify_MalformedSignature_IsFalse()
    {
        var bogus = _sut.Sign(MakeLink(), _priv) with { Signature = "0x1234" };
        Assert.That(_sut.Verify(bogus), Is.False);
    }
}