using Circles.Profiles.Models;
using Circles.Profiles.Sdk.Tests.Mocks;
using Circles.Sdk;
using Moq;

namespace Circles.Profiles.Sdk.Tests;

[TestFixture]
public class ProfileStoreTests
{
    private readonly string _priv = Nethereum.Signer.EthECKey.GenerateKey().GetPrivateKey();

    [Test]
    public async Task FindAsync_Returns_Profile_From_Ipfs_Cid_In_Registry()
    {
        var ipfs = new InMemoryIpfsStore();
        var profile = new Profile { Name = "A", Description = "B" };
        var cid = await ipfs.AddJsonAsync(System.Text.Json.JsonSerializer.Serialize(profile));

        var regMock = NameRegistryMock.WithProfileCid("0xavatar", cid);
        var store = new ProfileStore(ipfs, regMock.Object);

        var loaded = await store.FindAsync("0xavatar");

        Assert.That(loaded?.Name, Is.EqualTo("A"));
        regMock.Verify(r => r.GetProfileCidAsync("0xavatar",
                It.IsAny<CancellationToken>()),
            Times.Once);
        regMock.VerifyNoOtherCalls();
    }

    [Test]
    public async Task SaveAsync_Uploads_And_Updates_Registry()
    {
        var ipfs = new InMemoryIpfsStore();
        var regMock = new Mock<INameRegistry>(MockBehavior.Strict);
        regMock.Setup(r => r.UpdateProfileCidAsync(It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("TX-MOCK");

        var store = new ProfileStore(ipfs, regMock.Object);

        var p = new Profile
        {
            Namespaces = { },
            SigningKeys =
            {
                ["fp"] = new SigningKey { PublicKey = "0xPubKey", ValidFrom = 1 }
            }
        };

        var (_, resultingCid) = await store.SaveAsync(p, _priv);

        Assert.That(resultingCid, Is.Not.Null.And.Not.Empty);

        var expectedAddr = new Nethereum.Signer.EthECKey(_priv).GetPublicAddress();
        regMock.Verify(r => r.UpdateProfileCidAsync(expectedAddr,
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}