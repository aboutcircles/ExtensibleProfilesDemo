using Circles.Profiles.Models;
using Circles.Profiles.Sdk.Tests.Mocks;

namespace Circles.Profiles.Sdk.Tests;

[TestFixture]
public class NamespaceWriterHappyPathTests
{
    private readonly string _priv = Nethereum.Signer.EthECKey.GenerateKey().GetPrivateKey();

    private (Profile p, InMemoryIpfsStore store, NamespaceWriter w) Boot(string nsKey = "Bob")
    {
        var p = new Profile { Name = "Alice", Description = "Demo" };
        var s = new InMemoryIpfsStore();
        return (p, s, new NamespaceWriter(p, nsKey, s, new DefaultLinkSigner()));
    }

    [Test]
    public async Task AddJsonAsync_Persists_Chunk_Index_Profile()
    {
        var (prof, store, w) = Boot();

        var link = await w.AddJsonAsync("msg-1", """{ "hello":"world" }""", _priv);

        Assert.Multiple(async () =>
        {
            Assert.That(link.Name, Is.EqualTo("msg-1"));
            Assert.That(prof.Namespaces, Contains.Key("bob"));

            // chunk and index must be fetchable from "IPFS"
            var indexCid = prof.Namespaces["bob"];
            Assert.DoesNotThrowAsync(() => store.CatAsync(indexCid));

            var chunkCid = (await Helpers.LoadIndex(indexCid, store)).Head;
            Assert.DoesNotThrowAsync(() => store.CatAsync(chunkCid));
        });
    }

    [Test]
    public async Task AttachCidBatchAsync_Writes_All_Items()
    {
        var (prof, _, w) = Boot();

        var items = new[]
        {
            ("alpha", "CID-A"), ("beta", "CID-B"), ("gamma", "CID-C")
        };
        var links = await w.AttachCidBatchAsync(items, _priv);

        Assert.That(links.Select(l => l.Name),
            Is.EquivalentTo(items.Select(i => i.Item1)));
        Assert.That(prof.Namespaces, Contains.Key("bob"));
    }

    [Test]
    public async Task Writer_Rotates_Chunk_When_Limit_Reached()
    {
        var (_, _, w) = Boot();

        for (int i = 0; i < Helpers.ChunkMaxLinks; i++)
            await w.AddJsonAsync($"n{i}", "{}", _priv);

        var firstHead = w.AddJsonAsync("overflow", "{}", _priv);
        Assert.DoesNotThrowAsync(() => firstHead); // rotation happened without error
    }
}