using Circles.Profiles.Models;
using Circles.Profiles.Models.Core;
using Circles.Profiles.Sdk.Tests.Mocks;

namespace Circles.Profiles.Sdk.Tests;

[TestFixture]
public class NamespaceWriterRotationTests
{
    [Test]
    public async Task Multiple_Rotations_Preserve_All_Links()
    {
        var profile = new Profile();
        var store   = new InMemoryIpfsStore();
        var writer  = await NamespaceWriter.CreateAsync(profile, "dst", store, new EoaSigner(Nethereum.Signer.EthECKey.GenerateKey()));

        const int total = Helpers.ChunkMaxLinks * 2 + 5;   // ≥ 2 rotations
        var logicalNames = new List<string>();

        for (int i = 0; i < total; i++)
        {
            string name = $"n{i:D3}";
            await writer.AddJsonAsync(name, "{}");
            logicalNames.Add(name);
        }

        // walk index → chunk → links and collect back
        var idxCid  = profile.Namespaces["dst"];
        var idx     = await Helpers.LoadIndex(idxCid, store);
        string? cur = idx.Head;

        var seen = new HashSet<string>();

        while (cur is not null)
        {
            var chunk = await Helpers.LoadChunk(cur, store);
            foreach (var l in chunk.Links) seen.Add(l.Name);
            cur = chunk.Prev;
        }

        Assert.That(seen, Is.EquivalentTo(logicalNames));
    }
}