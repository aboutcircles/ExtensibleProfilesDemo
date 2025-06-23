using Circles.Profiles.Models;
using Circles.Profiles.Sdk.Tests.Mocks;

namespace Circles.Profiles.Sdk.Tests;

[TestFixture]
public class NamespaceWriterTests
{
    private readonly string _priv = Nethereum.Signer.EthECKey.GenerateKey().GetPrivateKey();

    private static (Profile, InMemoryIpfsStore, NamespaceWriter) Setup(string nsKey,
        string priv)
    {
        var profile = new Profile { Name = "t", Description = "d" };
        var ipfs = new InMemoryIpfsStore();
        var writer = new NamespaceWriter(profile, nsKey, ipfs,
            new DefaultLinkSigner());
        return (profile, ipfs, writer);
    }

    [Test]
    public async Task AddJsonAsync_AddsLink_AndPinsBlob()
    {
        var (prof, ipfs, w) = Setup("bob", _priv);

        var link = await w.AddJsonAsync("msg-1", """{ "hello": "world" }""", _priv);

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
        var (prof, _, w) = Setup("bob", _priv);

        for (int i = 0; i < Helpers.ChunkMaxLinks + 1; i++)
            await w.AddJsonAsync($"n{i}", "{}", _priv);

        var headCid = prof.Namespaces["bob"]; // index head → latest chunk
        Assert.That(headCid, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task BatchAttach_AddsAllLinks()
    {
        var (prof, _, w) = Setup("bob", _priv);

        var items = new[] { ("a", "cid-A"), ("b", "cid-B"), ("c", "cid-C") };
        var links = await w.AttachCidBatchAsync(items, _priv);

        Assert.That(links.Select(l => l.Name), Is.EquivalentTo(new[] { "a", "b", "c" }));
        Assert.That(prof.Namespaces, Contains.Key("bob"));
    }
}