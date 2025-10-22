using System.Text.Json;
using Circles.Profiles.Models;
using Circles.Profiles.Models.Chat;
using Circles.Profiles.Models.Core;
using Circles.Profiles.Sdk.Tests.Mocks;
using Nethereum.Signer;
using JsonSerializerOptions = System.Text.Json.JsonSerializerOptions;

namespace Circles.Profiles.Sdk.Tests;

[TestFixture]
public class InboxSimulationTests
{
    private readonly string _bPriv = EthECKey.GenerateKey().GetPrivateKey();

    [Test]
    public async Task Alice_Sees_Messages_From_Bob_In_Correct_Order()
    {
        /* -------- Test-bed set-up -------- */
        var store = new InMemoryIpfsStore();

        var aliceProfile = new Profile();
        var bobProfile = new Profile();

        _ = await NamespaceWriter.CreateAsync(aliceProfile, /*nsKey*/"Bob", store, new EoaLinkSigner());
        var b2A = await NamespaceWriter.CreateAsync(bobProfile, /*nsKey*/"Alice", store, new EoaLinkSigner());

        /* ------- Bob sends two messages ------- */
        await b2A.AddJsonAsync("msg-1", """{"txt":"hi"}""", _bPriv);
        await Task.Delay(10);
        await b2A.AddJsonAsync("msg-2", """{"txt":"sup"}""", _bPriv);

        /* -------- Alice’s “inbox” walk -------- */
        var idxCid = bobProfile.Namespaces["alice"];
        var idx = await Helpers.LoadIndex(idxCid, store);

        var collected = new List<CustomDataLink>();
        var curChunkCid = idx.Head;

        var chunkOrder = new Dictionary<CustomDataLink, int>();
        while (curChunkCid is not null)
        {
            var chunk = await Helpers.LoadChunk(curChunkCid, store);

            for (int i = 0; i < chunk.Links.Count; i++)
                chunkOrder[chunk.Links[i]] = i; // remember index in its chunk

            collected.AddRange(chunk.Links);
            curChunkCid = chunk.Prev;
        }

        /* newest → oldest (seconds granularity)
         * – if SignedAt ties, use the position inside the chunk (newer entries are appended
         *   to the list, so a higher index means a newer link). */
        var names = collected
            .OrderByDescending(l => l.SignedAt) // primary: timestamp
            .ThenByDescending(l => // secondary: “arrival order”
                chunkOrder[l]) // pre-computed index in its chunk
            .Select(l => l.Name)
            .ToArray();

        collected.Sort((x, y) => y.SignedAt.CompareTo(x.SignedAt));

        Assert.That(names, Is.EqualTo(new[] { "msg-2", "msg-1" }).AsCollection);
    }

    [Test]
    public async Task SendMessage_Then_ReadViaInbox_FindsMessage()
    {
        // Arrange
        var ipfs = new InMemoryIpfsStore();
        var aPriv = EthECKey.GenerateKey().GetPrivateKey();
        var bPriv = EthECKey.GenerateKey().GetPrivateKey();
        var aAddr = new EthECKey(aPriv).GetPublicAddress();
        var bAddr = new EthECKey(bPriv).GetPublicAddress();

        // Each user has their own Profile object
        var bob = new Profile { Name = "Bob", Description = "desc" };

        // Bob sends a message to Alice (the normal use-case)
        var bobToAliceWriter = await NamespaceWriter.CreateAsync(bob, aAddr, ipfs, new EoaLinkSigner());
        var msgObj = new BasicMessage
        {
            From = bAddr,
            To = aAddr,
            Type = "chat",
            Text = "hello alice, from bob",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        var msgJson = JsonSerializer.Serialize(msgObj,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await bobToAliceWriter.AddJsonAsync("msg-1", msgJson, bPriv);

        // Simulate Bob saving his profile (as would happen in a real app)
        var regMock = NameRegistryMock.WithProfileCid(bAddr, await ipfs.AddStringAsync(JsonSerializer.Serialize(bob)));
        var store = new ProfileStore(ipfs, regMock.Object);

        // Re-save Bob's profile, so the IPFS content includes the new message namespace
        await store.SaveAsync(bob, bPriv);

        // ----- "Inbox" logic -----
        // Alice reads her inbox: for each trusted sender, she checks their profile, 
        // loads the namespace pointing to herself, and finds the message

        // Simulate the "trusted" list: Alice trusts Bob
        var trusted = new[] { bAddr };

        // For this e2e, we're just going to check Bob's profile, as Alice would
        bool foundMessage = false;

        foreach (var sender in trusted)
        {
            var bobProfile = await store.FindAsync(sender);
            Assert.That(bobProfile, Is.Not.Null, "Expected to find Bob's profile");

            // Alice's own address is lower-cased as the namespace key
            var aliceNs = aAddr.ToLowerInvariant();

            Assert.That(bobProfile.Namespaces, Contains.Key(aliceNs),
                "Bob's Namespaces should include alice's address");

            var idxCid = bobProfile.Namespaces[aliceNs];
            var idxDoc = await Helpers.LoadIndex(idxCid, ipfs);

            var cur = idxDoc.Head;
            while (cur is not null)
            {
                var chunk = await Helpers.LoadChunk(cur, ipfs);
                foreach (var link in chunk.Links)
                {
                    var rawMsg = await ipfs.CatStringAsync(link.Cid);
                    var chatMsg = JsonSerializer.Deserialize<BasicMessage>(rawMsg, Helpers.JsonOpts);
                    if (chatMsg is not null && chatMsg.Text == "hello alice, from bob")
                    {
                        foundMessage = true;
                        // Optionally, check more fields here.
                    }
                }

                cur = chunk.Prev;
            }
        }

        Assert.That(foundMessage, Is.True, "Inbox should include Bob's message to Alice");
    }
}