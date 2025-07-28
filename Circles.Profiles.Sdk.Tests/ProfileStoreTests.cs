using Circles.Profiles.Interfaces;
using Circles.Profiles.Models;
using Circles.Profiles.Sdk.Tests.Mocks;
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

    [Test]
    public async Task SaveAsync_Passes_Correct_Digest_To_Registry()
    {
        var ipfs = new InMemoryIpfsStore();

        // strict mock so *only* the expected digest is accepted
        var regMock = new Mock<INameRegistry>(MockBehavior.Strict);

        byte[]? digestSeen = null;

        regMock.Setup(r => r.UpdateProfileCidAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, byte[], CancellationToken>((_, d, _) => digestSeen = d)
            .ReturnsAsync("TX-MOCK");

        var store = new ProfileStore(ipfs, regMock.Object);
        var profile = new Profile { Name = "Severe", Description = "digest‑check" };

        (_, string cid) = await store.SaveAsync(profile, _priv);

        var expectedDigest = CidConverter.CidToDigest(cid);

        Assert.That(digestSeen, Is.EqualTo(expectedDigest), "registry received wrong digest");
        regMock.VerifyAll();
    }
    
    [Test]
    public async Task FindAsync_NoProfileCid_ReturnsNull()
    {
        var ipfsMock = new Mock<IIpfsStore>(MockBehavior.Strict); // must stay unused
        var regMock  = new Mock<INameRegistry>(MockBehavior.Strict);

        regMock.Setup(r => r.GetProfileCidAsync("0xavatar",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var store = new ProfileStore(ipfsMock.Object, regMock.Object);
        Profile? p = await store.FindAsync("0xavatar");

        Assert.That(p, Is.Null);
        regMock.VerifyAll();
    }
}