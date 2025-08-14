using System.Text.Json;
using Circles.Profile.UI.Services;
using Circles.Profiles.Sdk;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;

async Task AddNamespace(ProfileStore profileStore1, string addr2, LocalProfileStore localProfileStore2, string key1)
{
    var prof = await profileStore1.FindAsync(addr2) ?? localProfileStore2.GetOrCreate(addr2);
    if (!prof.Namespaces.TryAdd(key1, ""))
    {
        Console.WriteLine("Namespace already present.");
        return;
    }

    localProfileStore2.Save(addr2, prof);
    Console.WriteLine("✔ namespace added (local).");
}

async Task Show(ProfileStore profileStore, string addr1, LocalProfileStore localProfileStore1)
{
    var prof = await profileStore.FindAsync(addr1) ?? localProfileStore1.GetOrCreate(addr1);
    Console.WriteLine(JsonSerializer.Serialize(prof, new JsonSerializerOptions { WriteIndented = true }));
}

Console.WriteLine("=== Circles CLI playground ===");
Console.WriteLine("env  PRIVATE_KEY   … Tx signer (deployer & fallback funding)");
Console.WriteLine("env  RPC_URL       … defaults to https://rpc.aboutcircles.com");
Console.WriteLine("env  IPFS_API      … defaults to http://localhost:5001       ");
Console.WriteLine("----------------------------------------------");

string rpc = Environment.GetEnvironmentVariable("RPC_URL")
             ?? "https://rpc.aboutcircles.com";
string priv = Environment.GetEnvironmentVariable("PRIVATE_KEY")
              ?? throw new("export PRIVATE_KEY with a funded Gnosis‑Chain EOA");

var deployer = new Account(priv, 100);
var web3 = new Web3(deployer, rpc);
var ipfs = new IpfsStore(); // same SDK impl
var registry = new NameRegistry(deployer.PrivateKey, rpc);
var storeSdk = new ProfileStore(ipfs, registry);

var keys = new KeyStore(); // ./keys
var safes = new SafeStore(web3, deployer); // ./safes.json
var cache = new LocalProfileStore(); // ./profiles

void Log(string msg) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");

/* ------------------------------------------------------------------ */

while (true)
{
    try
    {
        Console.WriteLine();
        Console.WriteLine("1) list   2) new‑eoa   3) new‑safe   4) show <addr>");
        Console.WriteLine("5) edit‑name <addr>   6) add‑ns <addr> <key>");
        Console.WriteLine("7) add‑link <addr> <ns> <name> <json>   8) quit");
        Console.Write("> ");
        var parts = Console.ReadLine()!.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) continue;

        switch (parts[0])
        {
            case "1":
            case "list":
            {
                await List(keys, safes, registry);
                break;
            }

            case "2":
            case "new-eoa":
            {
                await NewEoa(keys, cache);
                break;
            }

            case "3":
            case "new-safe":
            {
                await NewSafe(keys, safes, cache);
                break;
            }

            case "4":
            case "show" when parts.Length >= 2:
            {
                string addr = parts[1];
                await Show(storeSdk, addr, cache);
                break;
            }

            case "5":
            case "edit-name" when parts.Length >= 2:
            {
                string addr = parts[1];
                await EditName(storeSdk, addr, cache, safes, keys);
                break;
            }

            case "6":
            case "add-ns" when parts.Length >= 3:
            {
                string addr = parts[1];
                string key = parts[2].ToLowerInvariant();

                await AddNamespace(storeSdk, addr, cache, key);
                break;
            }

            case "7":
            case "add-link" when parts.Length >= 5:
            {
                string addr = parts[1];
                string ns = parts[2];
                string name = parts[3];
                string json = parts[4];

                await AddLink(storeSdk, addr, cache, ns, ipfs, keys, name, json);
                break;
            }

            case "8":
            case "quit":
                return;

            default:
                Console.WriteLine("Unknown command.");
                break;
        }
    }
    catch (Exception e)
    {
        Console.WriteLine(e.Message);
    }
}

async Task List(KeyStore keyStore, SafeStore safeStore, NameRegistry nameRegistry)
{
    var addressProfileMapPromises = new Dictionary<string, Task<string?>>();
    keyStore.Wallets.Select(o => o.Address)
        .Union(safeStore.Safes.Select(o => o.Address)).ToList()
        .ForEach(o => { addressProfileMapPromises.Add(o, nameRegistry.GetProfileCidAsync(o)); });

    await Task.WhenAll(addressProfileMapPromises.Values);

    var addressProfileMap = addressProfileMapPromises.Where(o => o.Value.Result != null)
        .ToDictionary(o => o.Key, o => o.Value.Result!);

    Console.WriteLine("EOAs:");
    foreach (var w in keyStore.Wallets)
    {
        var profileCidStr = addressProfileMap.TryGetValue(w.Address, out string? cid)
            ? $" (CID {cid})"
            : "";
        var str = $"  {w.Address}{profileCidStr}";
        Console.WriteLine(str);
    }

    Console.WriteLine("Safes:");
    foreach (var s in safeStore.Safes)
    {
        var profileCidStr = addressProfileMap.TryGetValue(s.Address, out string? cid)
            ? $", CID {cid}"
            : "";
        var str = $"  {s.Address} (owner {s.Owner}{profileCidStr})";
        Console.WriteLine(str);
    }
}

Task NewEoa(KeyStore keys1, LocalProfileStore localProfileStore)
{
    var w = keys1.CreateNew();
    localProfileStore.GetOrCreate(w.Address);
    Console.WriteLine($"✔ created EOA {w.Address}");

    return Task.CompletedTask;
}

async Task NewSafe(KeyStore keyStore1, SafeStore safes1, LocalProfileStore cache1)
{
    if (!keyStore1.Wallets.Any())
    {
        Console.WriteLine("No wallets yet. Create an EOA first.");
        return;
    }

    Console.Write("Owner address: ");
    var owner = Console.ReadLine()!.Trim();
    var wk = keyStore1.Wallets.FirstOrDefault(w => w.Address == owner)
             ?? throw new("Unknown owner");
    var info = await safes1.CreateAsync(wk);
    cache1.GetOrCreate(info.Address);
    Console.WriteLine($"✔ Safe {info.Address}");
}

async Task EditName(ProfileStore storeSdk1, string s1, LocalProfileStore cache2, SafeStore safeStore1, KeyStore keys2)
{
    var prof = await storeSdk1.FindAsync(s1) ?? cache2.GetOrCreate(s1);
    Console.Write($"New name (old “{prof.Name}”): ");
    prof = prof with
    {
        Name = Console.ReadLine()!
    };
    cache2.Save(s1, prof);

    if (safeStore1.Safes.Any(s => s.Address == s1))
    {
        Console.WriteLine("Safe detected – profile only cached. Use 7) add‑link etc. & publish later.");
    }
    else
    {
        var ownerKey = keys2.Wallets.First(w => w.Address == s1);
        await storeSdk1.SaveAsync(prof, ownerKey.PrivateKey);
        Console.WriteLine("✔ on-chain updated");
    }
}

async Task AddLink(ProfileStore storeSdk2, string s2, LocalProfileStore cache3, string ns1, IpfsStore ipfsStore,
    KeyStore keyStore2, string name1, string json1)
{
    var prof = await storeSdk2.FindAsync(s2) ?? cache3.GetOrCreate(s2);
    if (!prof.Namespaces.TryGetValue(ns1, out var idxCid) || string.IsNullOrEmpty(idxCid))
    {
        Console.WriteLine("Namespace missing or empty. Use 6) first.");
        return;
    }

    var writer = await NamespaceWriter.CreateAsync(prof, ns1, ipfsStore, new DefaultLinkSigner());
    var owner = keyStore2.Wallets.First(w => w.Address == s2);
    await writer.AddJsonAsync(name1, json1, owner.PrivateKey);

    // writer internally updates prof.Namespaces[…]
    await storeSdk2.SaveAsync(prof, owner.PrivateKey);
    Console.WriteLine("✔ link pinned & profile republished");
}