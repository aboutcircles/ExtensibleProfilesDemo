using Circles.Profiles.Interfaces;
using Circles.Profiles.Models;
using Circles.Profiles.Models.Core;
using Circles.Profiles.Sdk.Tests.Mocks;
using Circles.Profiles.Sdk.Utils;
using Moq;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Signer;

namespace Circles.Profiles.Sdk.Tests;

[TestFixture]
public class NamespaceWriterTests
{
    private static async Task<(Profile, InMemoryIpfsStore, NamespaceWriter)> Setup(string nsKey)
    {
        var profile = new Profile { Name = "t", Description = "d" };
        var ipfs = new InMemoryIpfsStore();
        var writer = await NamespaceWriter.CreateAsync(profile, nsKey, ipfs,
            new EoaSigner(EthECKey.GenerateKey()));

        return (profile, ipfs, writer);
    }

    [Test]
    public async Task AddJsonAsync_AddsLink_AndPinsBlob()
    {
        var (prof, ipfs, w) = await Setup("bob");

        var link = await w.AddJsonAsync("msg-1", """{ "hello": "world" }""");

        Assert.Multiple(() =>
        {
            Assert.That(link.Name, Is.EqualTo("msg-1"));
            Assert.That(prof.Namespaces, Contains.Key("bob"));
            Assert.DoesNotThrowAsync(async () => await ipfs.CatAsync(link.Cid));
        });
    }

    [Test]
    public async Task ChunkRotation_HitsLimit_OpensNewChunk()
    {
        var (prof, _, w) = await Setup("bob");

        for (int i = 0; i < Helpers.ChunkMaxLinks + 1; i++)
            await w.AddJsonAsync($"n{i}", "{}");

        var headCid = prof.Namespaces["bob"]; // index head → latest chunk
        Assert.That(headCid, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task BatchAttach_AddsAllLinks()
    {
        var (prof, _, w) = await Setup("bob");

        var items = new[] { ("a", "cid-A"), ("b", "cid-B"), ("c", "cid-C") };
        var links = await w.AttachCidBatchAsync(items);

        Assert.That(links.Select(l => l.Name), Is.EquivalentTo(new[] { "a", "b", "c" }));
        Assert.That(prof.Namespaces, Contains.Key("bob"));
    }

    [Test]
    public async Task AddJsonBatchAsync_WritesAllLinks_AndUpdatesIndex()
    {
        var profile = new Profile();
        var ipfs = new InMemoryIpfsStore();
        var writer = await NamespaceWriter.CreateAsync(
            profile, "dst", ipfs, new EoaSigner(EthECKey.GenerateKey()));

        var items = Enumerable.Range(0, 5)
            .Select(i => ($"n{i}", $"{{\"v\":{i}}}"))
            .ToArray();

        var links = await writer.AddJsonBatchAsync(items);

        Assert.Multiple(() =>
        {
            Assert.That(links.Select(l => l.Name),
                Is.EquivalentTo(items.Select(t => t.Item1)));

            Assert.That(profile.Namespaces, Contains.Key("dst"));
        });
    }

    [Test]
    public async Task AddJsonBatchAsync_Crosses_ChunkLimit_RotatesOnce()
    {
        var profile = new Profile();
        var ipfs = new InMemoryIpfsStore();
        var writer = await NamespaceWriter.CreateAsync(
            profile, "dst", ipfs, new EoaSigner(EthECKey.GenerateKey()));

        int total = Helpers.ChunkMaxLinks + 10; // forces rotation
        var items = Enumerable.Range(0, total)
            .Select(i => ($"item{i}", "{}"))
            .ToArray();

        await writer.AddJsonBatchAsync(items);

        var idxCid = profile.Namespaces["dst"];
        var idxDoc = await Helpers.LoadIndex(idxCid, ipfs);

        Assert.That(idxDoc.Entries.Count, Is.EqualTo(total));
        Assert.That(idxDoc.Head, Is.Not.Empty);
    }

    [Test]
    public void LoadChunk_InvalidJson_ThrowsWithCid()
    {
        var ipfs = new InMemoryIpfsStore();
        string cid = Task.Run(() =>
                ipfs.AddBytesAsync("not-json"u8.ToArray(), pin: true))
            .Result;

        var ex = Assert.ThrowsAsync<InvalidDataException>(async () =>
            await Helpers.LoadChunk(cid, ipfs));

        Assert.That(ex!.Message, Does.Contain(cid));
    }

    [Test]
    public async Task AcceptSignedLinkAsync_Rejects_WrongNamespace()
    {
        var user = new Profile();
        var ipfs = new InMemoryIpfsStore();
        var signer = new EoaSigner(EthECKey.GenerateKey());

        var writer = await NamespaceWriter.CreateAsync(
            user, /*nsKey*/"0xDappAddr", ipfs, signer);

        var badLink = await LinkSigning.SignAsync(new CustomDataLink
        {
            Name = "settings",
            Cid = "CID‑x",
            ChainId = Helpers.DefaultChainId,
            SignedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Nonce = CustomDataLink.NewNonce(),
            Encrypted = false
        }, new EoaSigner(EthECKey.GenerateKey())); // signerAddress ≠ namespace key

        // signerAddress ≠ namespace key – must throw
        var dummyChain = new Mock<IChainApi>();
        dummyChain.SetupGet(c => c.Id)
            .Returns(Helpers.DefaultChainId);
        dummyChain.Setup(c => c.GetCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("0x");

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            writer.AcceptSignedLinkAsync(badLink, new Profile(), dummyChain.Object));
    }

    [Test]
    public async Task AcceptSignedLinkAsync_HappyPath_WritesLink()
    {
        var dappKey = EthECKey.GenerateKey();
        var dappAddr = dappKey.GetPublicAddress();

        var prof = new Profile();
        var ipfs = new InMemoryIpfsStore();
        var signer = new EoaSigner(EthECKey.GenerateKey()); // sign with dappKey later

        var writer = await NamespaceWriter.CreateAsync(
            prof, dappAddr, ipfs, signer);

        var link = await LinkSigning.SignAsync(new CustomDataLink
        {
            Name = "theme",
            Cid = "CID‑settings",
            ChainId = Helpers.DefaultChainId,
            SignedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Nonce = CustomDataLink.NewNonce(),
            Encrypted = false
        }, new EoaSigner(dappKey));

        // build minimal operator profile with valid key‑fp
        string fp = SigningKeyUtils.ComputeFingerprint(dappKey);
        var dappProfile = new Profile();
        dappProfile.SigningKeys[fp] = new SigningKey
        {
            PublicKey = "0x" + dappKey.GetPubKey(false).ToHex(),
            ValidFrom = 0
        };

        // mock chain so no external RPC
        var chainMock = new Mock<IChainApi>();
        chainMock.SetupGet(c => c.Id)
            .Returns(Helpers.DefaultChainId);
        chainMock.Setup(c => c.GetCodeAsync(dappAddr, It.IsAny<CancellationToken>()))
            .ReturnsAsync("0x");

        await writer.AcceptSignedLinkAsync(link, dappProfile, chainMock.Object);

        var idxCid = prof.Namespaces[dappAddr.ToLowerInvariant()];
        var idxDoc = await Helpers.LoadIndex(idxCid, ipfs);
        Assert.That(idxDoc.Entries, Contains.Key("theme"));
    }
}