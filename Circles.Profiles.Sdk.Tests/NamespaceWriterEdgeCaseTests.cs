using Circles.Profiles.Models;
using Circles.Profiles.Sdk.Tests.Mocks;

namespace Circles.Profiles.Sdk.Tests;

[TestFixture]
public class NamespaceWriterEdgeCaseTests
{
    private readonly string _priv = Nethereum.Signer.EthECKey.GenerateKey().GetPrivateKey();

    private static (Profile p, InMemoryIpfsStore store, NamespaceWriter w)
        Boot(string nsKey, string priv)
    {
        var p = new Profile { Name = "X", Description = "Y" };
        var s = new InMemoryIpfsStore();
        var w = new NamespaceWriter(p, nsKey, s, new DefaultLinkSigner());
        return (p, s, w);
    }

    [Test]
    public async Task DuplicateLogicalName_Replaces_Older_Link()
    {
        var (_, store, w) = Boot("Carol", _priv);

        var a = await w.AddJsonAsync("dup", """{"v":1}""", _priv);
        await Task.Delay(5); // ensure timestamp difference
        var b = await w.AddJsonAsync("dup", """{"v":2}""", _priv);

        Assert.That(b.SignedAt, Is.GreaterThanOrEqualTo(a.SignedAt));

        // index must now point to chunk that holds _b_
        var idx = await Helpers.LoadIndex(w.OwnerProfile().Namespaces["carol"], store);
        var chunk = await Helpers.LoadChunk(idx.Entries["dup"], store);

        Assert.That(chunk.Links.Single(l => l.Name == "dup").Cid, Is.EqualTo(b.Cid));
    }

    [Test]
    public void AttachExistingCidAsync_NullName_Throws()
        => Assert.ThrowsAsync<ArgumentNullException>(() =>
            Boot("k", _priv).w.AttachExistingCidAsync(null!, "cid", _priv));
}