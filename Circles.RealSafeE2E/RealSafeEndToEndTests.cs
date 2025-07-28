using System.Text.Json;
using Circles.Profiles.Models;
using Circles.Profiles.Sdk;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using static Circles.RealSafeE2E.SafeHelpers;

namespace Circles.RealSafeE2E;

[TestFixture]
public class RealSafeEndToEndTests
{
    private const string Rpc = "https://rpc.aboutcircles.com";

    private Account _deployer = null!;
    private Web3 _web3 = null!;

    private record Actor(string Alias, Account OwnerKey, string SafeAddr, Profile Profile);

    private readonly List<Actor> _actors = new();

    /* ────────────────────────────────────────────────────────── boot ── */
    [OneTimeSetUp]
    public async Task BootAsync()
    {
        Console.WriteLine("[E2E] Boot – deployer + Safes");

        var privateKey = Environment.GetEnvironmentVariable("PRIVATE_KEY") ??
                         throw new ArgumentException("The PRIVATE_KEY environment variable is not set");
        _deployer = new Account(privateKey, 100);
        _web3 = new Web3(_deployer, Rpc);

        foreach (string alias in new[] { "Alice", "Bob", "Charly" })
        {
            var ownerKey = Nethereum.Signer.EthECKey.GenerateKey();
            var acct = new Account(ownerKey.GetPrivateKey(), 100);

            await FundAsync(_web3, _deployer, acct.Address, 0.001);
            string safeAddr = await DeploySafeAsync(
                _web3, new[] { _deployer.Address, acct.Address }, threshold: 1);

            _actors.Add(new Actor(alias, acct, safeAddr,
                new Profile { Name = alias, Description = "real‑safe‑e2e" }));
        }
    }

    /* ─────────────────────────────────────────── full round‑trip ────── */
    [Test]
    public async Task PingPong_MultiRound_EndToEnd()
    {
        await using var ipfs = new IpfsStore();
        var chainApi = new EthereumChainApi(_web3, 100);

        const int rounds = 3;
        Console.WriteLine($"[E2E] Writing {rounds} rounds, all sender→recipient pairs");

        /* ---------- 1) write messages (Rounds × 3 × 2 = 18 links) ---- */
        for (int r = 0; r < rounds; r++)
        {
            foreach (var sender in _actors)
            {
                foreach (var recipient in _actors.Where(a => a != sender))
                {
                    var signer = new SafeLinkSigner(sender.SafeAddr, chainApi);
                    var writer = await NamespaceWriter.CreateAsync(
                        sender.Profile, recipient.SafeAddr, ipfs, signer);

                    string logicalName = $"msg-r{r}-{sender.Alias[0]}to{recipient.Alias[0]}";
                    string json = JsonSerializer.Serialize(
                        new { txt = $"round {r} – hi from {sender.Alias} to {recipient.Alias}" });

                    var link = await writer.AddJsonAsync(
                        logicalName, json, sender.OwnerKey.PrivateKey);

                    Console.WriteLine($"[round {r}] {sender.Alias} → {recipient.Alias}  {logicalName}  CID={link.Cid}");
                }
            }
        }

        /* ---------- 2) pin + publish profile CID via Safe ---------- */
        Console.WriteLine("[E2E] Publishing profile digests via Safe");

        foreach (var a in _actors)
        {
            string profJson = JsonSerializer.Serialize(a.Profile, Helpers.JsonOpts);
            string cid = await ipfs.AddJsonAsync(profJson, pin: true);

            Console.WriteLine($"   {a.Alias} profile CID {cid}");

            byte[] digest32 = CidConverter.CidToDigest(cid);
            await ExecTransactionAsync(
                _web3, a.SafeAddr, a.OwnerKey,
                NameRegistryAddress, EncodeUpdateDigest(digest32));
        }

        /* ---------- 3) verify last‑round messages for every pair ---- */
        Console.WriteLine("[E2E] Verifying last‑round inboxes");

        foreach (var recipient in _actors)
        {
            var registry = new NameRegistry(recipient.OwnerKey.PrivateKey, Rpc);
            var store = new ProfileStore(ipfs, registry);

            foreach (var sender in _actors.Where(a => a != recipient))
            {
                var prof = await store.FindAsync(sender.SafeAddr);
                Assert.That(prof, Is.Not.Null, $"profile of {sender.Alias} missing");

                string nsKey = recipient.SafeAddr.ToLowerInvariant();
                Assert.That(prof!.Namespaces, Contains.Key(nsKey),
                    $"namespace {nsKey} missing in {sender.Alias} profile");

                var idx = await Helpers.LoadIndex(prof.Namespaces[nsKey], ipfs);
                var chunk = await Helpers.LoadChunk(idx.Head, ipfs);

                string expectedName = $"msg-r{rounds - 1}-{sender.Alias[0]}to{recipient.Alias[0]}";
                Assert.That(chunk.Links.Select(l => l.Name), Contains.Item(expectedName),
                    $"link {expectedName} not found in {sender.Alias}→{recipient.Alias}");
            }
        }

        Console.WriteLine("[E2E] ✅ all inbox checks passed");
    }
}