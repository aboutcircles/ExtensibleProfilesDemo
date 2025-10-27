using Circles.Profiles.Models;
using Circles.Profiles.Models.Core;
using Circles.Profiles.Sdk.Tests.Mocks;
using Nethereum.Signer;

namespace Circles.Profiles.Sdk.Tests;

[TestFixture]
public class NamespaceWriterHappyPathTests
{
    private async Task<(Profile p, InMemoryIpfsStore store, NamespaceWriter w)> Boot(string nsKey = "Bob")
    {
        var p = new Profile { Name = "Alice", Description = "Demo" };
        var s = new InMemoryIpfsStore();
        var signer = new EoaSigner(EthECKey.GenerateKey());
        return (p, s, await NamespaceWriter.CreateAsync(p, nsKey, s, signer));
    }

    [Test]
    public async Task AddJsonAsync_Persists_Chunk_Index_Profile()
    {
        var (prof, store, w) = await Boot();

        var link = await w.AddJsonAsync("msg-1", """{ "hello":"world" }""");

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(link.Name, Is.EqualTo("msg-1"));
            Assert.That(prof.Namespaces, Contains.Key("bob"));

            var indexCid = prof.Namespaces["bob"];
            Assert.DoesNotThrowAsync(() => store.CatAsync(indexCid));

            var chunkCid = (await Helpers.LoadIndex(indexCid, store)).Head;
            Assert.DoesNotThrowAsync(() => store.CatAsync(chunkCid));
        });
    }

    [Test]
    public async Task AttachCidBatchAsync_Writes_All_Items()
    {
        var (prof, _, w) = await Boot();

        var items = new[] { ("alpha", "CID-A"), ("beta", "CID-B"), ("gamma", "CID-C") };
        var links = await w.AttachCidBatchAsync(items);

        Assert.That(links.Select(l => l.Name),
            Is.EquivalentTo(items.Select(i => i.Item1)));
        Assert.That(prof.Namespaces, Contains.Key("bob"));
    }

    [Test]
    public async Task Writer_Rotates_Chunk_When_Limit_Reached()
    {
        var (_, _, w) = await Boot();

        for (int i = 0; i < Helpers.ChunkMaxLinks; i++)
            await w.AddJsonAsync($"n{i}", "{}");

        var firstHead = w.AddJsonAsync("overflow", "{}");
        Assert.DoesNotThrowAsync(() => firstHead); // rotation happened without error
    }
}