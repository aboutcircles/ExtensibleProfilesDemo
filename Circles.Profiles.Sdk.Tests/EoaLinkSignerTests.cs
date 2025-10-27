using Circles.Profiles.Models.Core;
using Nethereum.Signer;

namespace Circles.Profiles.Sdk.Tests;

[TestFixture]
public class EoaSignerTests
{
    private readonly EthECKey _key = EthECKey.GenerateKey();

    private static CustomDataLink MakeLink() => new()
    {
        Name = "greeting",
        Cid = "CID-dummy"
    };

    [Test]
    public async Task Sign_Populates_Address_And_Signature()
    {
        var ipfs = new Mocks.InMemoryIpfsStore();
        var prof = new Models.Core.Profile();
        var signer = new EoaSigner(_key);
        var writer = await NamespaceWriter.CreateAsync(prof, "dst", ipfs, signer);

        // use AttachExistingCidAsync to produce a signed link
        var signed = await writer.AttachExistingCidAsync(MakeLink().Name, MakeLink().Cid);

        Assert.That(signed.Signature, Does.StartWith("0x"));
        Assert.That(signed.SignerAddress, Is.EqualTo(signer.Address).IgnoreCase);
        Assert.That(signed.Signature.Length, Is.EqualTo(132)); // 65 bytes hex + 0x
        Assert.That(signed.SignerAddress.Length, Is.EqualTo(42)); // 20 bytes hex + 0x
    }
}