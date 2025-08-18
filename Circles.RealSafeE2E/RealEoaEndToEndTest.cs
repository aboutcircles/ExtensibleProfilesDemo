using System.Text.Json;
using Circles.Profiles.Models;
using Circles.Profiles.Safe;
using Circles.Profiles.Sdk;
using Nethereum.Signer;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;

namespace Circles.RealSafeE2E;

[TestFixture]
public class RealEoaEndToEndTests
{
    private const string Rpc = "https://rpc.aboutcircles.com";
    private const int ChainId = 100; // Gnosis Chain (0x64)

    private Account _deployer = null!;
    private Web3 _web3 = null!;

    private record Actor(string Alias, Account Key, string Address, Profile Profile);

    private readonly List<Actor> _actors = new();

    /* ────────────────────────────────────────────────────────── boot ── */
    [OneTimeSetUp]
    public async Task BootAsync()
    {
        Console.WriteLine("[EOA E2E] Boot – deployer + EOAs");

        var privateKey = Environment.GetEnvironmentVariable("PRIVATE_KEY") ??
                         throw new ArgumentException("The PRIVATE_KEY environment variable is not set");
        
        _deployer = new Account(privateKey, ChainId);
        _web3 = new Web3(_deployer, Rpc);

        foreach (string alias in new[] { "Alice", "Bob", "Charly" })
        {
            // Fresh EOA for each actor
            var eoaPriv = EthECKey.GenerateKey().GetPrivateKey();
            var acct = new Account(eoaPriv, ChainId);

            // fund so they can call updateMetadataDigest
            await SafeHelper.FundAsync(_web3, _deployer, acct.Address, 0.001);

            _actors.Add(new Actor(
                alias,
                acct,
                acct.Address,
                new Profile { Name = alias, Description = "real-eoa-e2e" }));
        }
    }

    /* ─────────────────────────────────────────── full round‑trip ────── */
    [Test]
    public async Task PingPong_MultiRound_EndToEnd_EOA()
    {
        await using var ipfs = new IpfsStore();

        const int rounds = 3;
        Console.WriteLine($"[EOA E2E] Writing {rounds} rounds, all sender→recipient pairs");

        /* ---------- 1) write messages (Rounds × 3 × 2 = 18 links) ---- */
        for (int r = 0; r < rounds; r++)
        {
            foreach (var sender in _actors)
            {
                foreach (var recipient in _actors.Where(a => !ReferenceEquals(a, sender)))
                {
                    var signer = new EoaLinkSigner();
                    var writer = await NamespaceWriter.CreateAsync(
                        sender.Profile, recipient.Address, ipfs, signer);

                    string logicalName = $"msg-r{r}-{sender.Alias[0]}to{recipient.Alias[0]}";
                    string json = JsonSerializer.Serialize(
                        new { txt = $"round {r} – hi from {sender.Alias} to {recipient.Alias}" });

                    var link = await writer.AddJsonAsync(
                        logicalName, json, sender.Key.PrivateKey);

                    Console.WriteLine($"[round {r}] {sender.Alias} → {recipient.Alias}  {logicalName}  CID={link.Cid}");
                }
            }
        }

        /* ---------- 2) publish via EOA ---------- */
        Console.WriteLine("[EOA E2E] Publishing profile digests via EOAs");

        foreach (var a in _actors)
        {
            var registry = new NameRegistry(a.Key.PrivateKey, Rpc);
            var store = new ProfileStore(ipfs, registry);

            var (_, cid) = await store.SaveAsync(a.Profile, a.Key.PrivateKey);

            Console.WriteLine($"   {a.Alias} profile CID {cid}");
        }

        /* ---------- 3) verified reads for every pair ---- */
        Console.WriteLine("[EOA E2E] Verifying inboxes (with signature checks)");

        foreach (var recipient in _actors)
        {
            var registry = new NameRegistry(recipient.Key.PrivateKey, Rpc);
            var store = new ProfileStore(ipfs, registry);

            foreach (var sender in _actors.Where(a => !ReferenceEquals(a, recipient)))
            {
                var prof = await store.FindAsync(sender.Address);
                Assert.That(prof, Is.Not.Null, $"profile of {sender.Alias} missing");

                string nsKey = recipient.Address.ToLowerInvariant();
                Assert.That(prof!.Namespaces, Contains.Key(nsKey),
                    $"namespace {nsKey} missing in {sender.Alias} profile");

                var idx = await Helpers.LoadIndex(prof.Namespaces[nsKey], ipfs);

                var verifier = new DefaultSignatureVerifier(new EthereumChainApi(_web3, 100));
                var reader = new DefaultNamespaceReader(idx.Head, ipfs, verifier);

                string expectedName = $"msg-r{rounds - 1}-{sender.Alias[0]}to{recipient.Alias[0]}";
                var latest = await reader.GetLatestAsync(expectedName);

                Assert.That(latest, Is.Not.Null,
                    $"verified link {expectedName} not found {sender.Alias}→{recipient.Alias}");

                var raw = await ipfs.CatStringAsync(latest!.Cid);
                Assert.That(raw, Does.Contain($"round {rounds - 1}"));
            }
        }

        Console.WriteLine("[EOA E2E] ✅ all inbox checks (verified) passed");
    }
}