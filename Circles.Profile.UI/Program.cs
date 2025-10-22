using System.Text;
using System.Text.Json;
using Circles.Profile.UI.Services;
using Circles.Profiles.Models;
using Circles.Profiles.Models.Chat;
using Circles.Profiles.Models.Core;
using Circles.Profiles.Safe;
using Circles.Profiles.Sdk;
using Circles.Profiles.Sdk.Utils;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using JsonSerializerOptions = System.Text.Json.JsonSerializerOptions;

async Task AddNamespace(
    ProfileStore profileStore1,
    string addr2,
    LocalProfileStore localProfileStore2,
    string key1,
    IpfsRpcApiStore ipfsRpcApiStore)
{
    if (profileStore1 == null) throw new ArgumentNullException(nameof(profileStore1));
    if (localProfileStore2 == null) throw new ArgumentNullException(nameof(localProfileStore2));
    if (string.IsNullOrWhiteSpace(addr2)) throw new ArgumentNullException(nameof(addr2));
    if (string.IsNullOrWhiteSpace(key1)) throw new ArgumentNullException(nameof(key1));

    var prof = await profileStore1.FindAsync(addr2) ?? localProfileStore2.GetOrCreate(addr2);

    bool alreadyPresent = prof.Namespaces.ContainsKey(key1);
    if (alreadyPresent)
    {
        Console.WriteLine("Namespace already present.");
        return;
    }

    // Create a proper empty index document and pin it so we have a valid CID.
    var emptyIndex = new NameIndexDoc(); // head="", entries={}
    string indexJson = JsonSerializer.Serialize(emptyIndex, Circles.Profiles.Models.JsonSerializerOptions.JsonLd);
    string idxCid = await ipfsRpcApiStore.AddStringAsync(indexJson, pin: true);

    prof.Namespaces[key1] = idxCid;
    localProfileStore2.Save(addr2, prof);

    Console.WriteLine($"✔ namespace added (local), index CID {idxCid}");
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
var ipfs = new IpfsRpcApiStore(); // same SDK impl
var registry = new NameRegistry(deployer.PrivateKey, rpc);
var storeSdk = new ProfileStore(ipfs, registry);

var keys = new KeyStore(); // ./keys
var safes = new SafeStore(web3, deployer); // ./safes.json
var cache = new LocalProfileStore(); // ./profiles

var history = new ReplHistory(Path.Combine(AppContext.BaseDirectory, "repl-history.txt"));
var editor = new LineEditor(history);

/* ------------------------------------------------------------------ */

while (true)
{
    try
    {
        Console.WriteLine();
        Console.WriteLine("1) list   2) new-eoa   3) new-safe   4) show <addr>");
        Console.WriteLine("5) edit-name <addr>   6) add-ns <addr> <key>");
        Console.WriteLine("7) add-link <addr> <ns> <name> <json>");
        Console.WriteLine("8) send-msg <from> <to> <text>");
        Console.WriteLine("9) inbox <recipient> <sender> [take]");
        Console.WriteLine("10) quit");

        // ← use the new editor (supports ↑ history, Esc, etc.)
        var line = editor.ReadLine("> ");
        if (string.IsNullOrWhiteSpace(line)) continue;

        var parts = line.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
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

                await AddNamespace(storeSdk, addr, cache, key, ipfs);
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
            case "send-msg" when parts.Length >= 4:
            {
                string from = parts[1];
                string to = parts[2];
                string text = parts[3];
                await SendMsg(storeSdk, from, cache, to, ipfs, keys, safes, web3, text);
                break;
            }

            case "9":
            case "inbox" when parts.Length >= 3:
            {
                string recipient = parts[1];
                string sender = parts[2];
                int take = 20;
                if (parts.Length == 4 && int.TryParse(parts[3], out var n) && n > 0)
                {
                    take = n;
                }

                await Inbox(storeSdk, recipient, sender, ipfs, web3, take);
                break;
            }

            case "10":
            case "quit":
                // record the command and exit
                history.Add(line);
                return;

            default:
                Console.WriteLine("Unknown command.");
                break;
        }

        // persist the command if we executed a known switch case
        history.Add(line);
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

    bool isSafeAddress = safeStore1.Safes.Any(s => s.Address.Equals(s1, StringComparison.OrdinalIgnoreCase));
    if (isSafeAddress)
    {
        Console.WriteLine("Safe detected – profile only cached. Use 7) add‑link etc. & publish later.");
    }
    else
    {
        var ownerKey = keys2.Wallets.First(w => w.Address.Equals(s1, StringComparison.OrdinalIgnoreCase));
        var eoaRegistry = new NameRegistry(ownerKey.PrivateKey, rpc);
        var eoaStore = new ProfileStore(ipfs, eoaRegistry);

        await eoaStore.SaveAsync(prof, ownerKey.PrivateKey);
        Console.WriteLine("✔ on-chain updated");
    }
}

async Task AddLink(
    ProfileStore storeSdk2,
    string s2,
    LocalProfileStore cache3,
    string ns1,
    IpfsRpcApiStore ipfsRpcApiStore,
    KeyStore keyStore2,
    string name1,
    string json1)
{
    var prof = await storeSdk2.FindAsync(s2) ?? cache3.GetOrCreate(s2);

    var isSafe = safes.Safes.Any(s => s.Address.Equals(s2, StringComparison.OrdinalIgnoreCase));
    var signer = isSafe
        ? (Circles.Profiles.Interfaces.ILinkSigner)new SafeLinkSigner(s2,
            new EthereumChainApi(web3, Helpers.DefaultChainId))
        : new EoaLinkSigner();

    var writer = await NamespaceWriter.CreateAsync(prof, ns1.ToLowerInvariant(), ipfsRpcApiStore, signer);

    string signingPriv;
    if (isSafe)
    {
        var info = safes.Safes.FirstOrDefault(s => s.Address.Equals(s2, StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException($"Safe {s2} not found.");
        var owner = keyStore2.Wallets.FirstOrDefault(w =>
                        w.Address.Equals(info.Owner, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"Owner EOA {info.Owner} not in local keystore.");
        signingPriv = owner.PrivateKey;
    }
    else
    {
        var owner = keyStore2.Wallets.FirstOrDefault(w => w.Address.Equals(s2, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"EOA {s2} not in local keystore.");
        signingPriv = owner.PrivateKey;
    }

    await writer.AddJsonAsync(name1, json1, signingPriv);

    if (isSafe)
    {
        var acct = new Account(signingPriv, Helpers.DefaultChainId);
        string profJson = JsonSerializer.Serialize(prof, Circles.Profiles.Models.JsonSerializerOptions.JsonLd);
        string cid = await ipfsRpcApiStore.AddStringAsync(profJson, pin: true);
        byte[] digest32 = CidConverter.CidToDigest(cid);

        await SafeHelper.ExecTransactionAsync(
            web3,
            s2,
            acct,
            NameRegistryConsts.ContractAddress,
            SafeHelper.EncodeUpdateDigest(digest32));
    }
    else
    {
        var eoaRegistry = new NameRegistry(signingPriv, rpc);
        var eoaStore = new ProfileStore(ipfsRpcApiStore, eoaRegistry);

        await eoaStore.SaveAsync(prof, signingPriv);
    }

    Console.WriteLine("✔ link pinned & profile republished");
}

async Task SendMsg(
    ProfileStore storeSdk,
    string fromAddr,
    LocalProfileStore localProfiles,
    string toAddr,
    IpfsRpcApiStore ipfsRpcApiStore,
    KeyStore keyStore,
    SafeStore safeStore,
    Web3 web3,
    string text)
{
    if (string.IsNullOrWhiteSpace(fromAddr)) throw new ArgumentNullException(nameof(fromAddr));
    if (string.IsNullOrWhiteSpace(toAddr)) throw new ArgumentNullException(nameof(toAddr));
    if (string.IsNullOrWhiteSpace(text)) throw new ArgumentNullException(nameof(text));

    var senderProfile = await storeSdk.FindAsync(fromAddr) ?? localProfiles.GetOrCreate(fromAddr);
    var recipientKey = toAddr.ToLowerInvariant();

    bool isSafeSender = safeStore.Safes.Any(s => s.Address.Equals(fromAddr, StringComparison.OrdinalIgnoreCase));
    var signer = isSafeSender
        ? (Circles.Profiles.Interfaces.ILinkSigner)new SafeLinkSigner(fromAddr,
            new EthereumChainApi(web3, Helpers.DefaultChainId))
        : new EoaLinkSigner();

    var writer = await NamespaceWriter.CreateAsync(senderProfile, recipientKey, ipfsRpcApiStore, signer);

    var msgName = $"msg-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
    var msg = new BasicMessage
    {
        From = fromAddr,
        To = toAddr,
        Type = "chat",
        Text = text,
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    };
    var msgJson = JsonSerializer.Serialize(msg, Circles.Profiles.Models.JsonSerializerOptions.JsonLd);

    bool fromIsEoaInKeyStore =
        keyStore.Wallets.Any(w => w.Address.Equals(fromAddr, StringComparison.OrdinalIgnoreCase));
    string signingPrivKey;
    if (isSafeSender)
    {
        var safeInfo =
            safeStore.Safes.FirstOrDefault(s => s.Address.Equals(fromAddr, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Safe {fromAddr} not found in local store.");
        var ownerKey =
            keyStore.Wallets.FirstOrDefault(w => w.Address.Equals(safeInfo.Owner, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Owner EOA {safeInfo.Owner} not found in local keystore.");
        signingPrivKey = ownerKey.PrivateKey;
    }
    else
    {
        if (!fromIsEoaInKeyStore) throw new InvalidOperationException($"EOA {fromAddr} not found in local keystore.");
        signingPrivKey = keyStore.Wallets.First(w => w.Address.Equals(fromAddr, StringComparison.OrdinalIgnoreCase))
            .PrivateKey;
    }

    var link = await writer.AddJsonAsync(msgName, msgJson, signingPrivKey);

    if (isSafeSender)
    {
        var ownerAccount = new Account(signingPrivKey, Helpers.DefaultChainId);
        string profJson = JsonSerializer.Serialize(senderProfile, Circles.Profiles.Models.JsonSerializerOptions.JsonLd);
        string cid = await ipfsRpcApiStore.AddStringAsync(profJson, pin: true);
        byte[] digest32 = CidConverter.CidToDigest(cid);

        await SafeHelper.ExecTransactionAsync(
            web3,
            fromAddr,
            ownerAccount,
            NameRegistryConsts.ContractAddress,
            SafeHelper.EncodeUpdateDigest(digest32));
    }
    else
    {
        var eoaRegistry = new NameRegistry(signingPrivKey, rpc);
        var eoaStore = new ProfileStore(ipfsRpcApiStore, eoaRegistry);

        await eoaStore.SaveAsync(senderProfile, signingPrivKey);
    }

    Console.WriteLine($"✔ sent {msgName} → {toAddr}  (CID {link.Cid})");
}

async Task Inbox(
    ProfileStore storeSdk,
    string recipientAddr,
    string senderAddr,
    IpfsRpcApiStore ipfsRpcApiStore,
    Web3 web3,
    int take = 20)
{
    if (string.IsNullOrWhiteSpace(recipientAddr)) throw new ArgumentNullException(nameof(recipientAddr));
    if (string.IsNullOrWhiteSpace(senderAddr)) throw new ArgumentNullException(nameof(senderAddr));
    if (take <= 0) take = 20;

    var prof = await storeSdk.FindAsync(senderAddr);
    if (prof is null)
    {
        Console.WriteLine($"No profile found for sender {senderAddr}.");
        return;
    }

    var nsKey = recipientAddr.ToLowerInvariant();
    bool hasNs = prof.Namespaces.TryGetValue(nsKey, out var idxCid) && !string.IsNullOrWhiteSpace(idxCid);
    if (!hasNs)
    {
        Console.WriteLine($"Sender {senderAddr} has no namespace for {nsKey}.");
        return;
    }

    var idx = await Helpers.LoadIndex(idxCid, ipfsRpcApiStore);

    var chainProbe = new EthereumChainApi(web3, Helpers.DefaultChainId);
    var code = await chainProbe.GetCodeAsync(senderAddr);
    Console.WriteLine($"[diag] signer={senderAddr} codeLen={code.Length} head={idx.Head}");

    var verifier = new DefaultSignatureVerifier(new EthereumChainApi(web3, Helpers.DefaultChainId));
    var reader = new DefaultNamespaceReader(idx.Head, ipfsRpcApiStore, verifier);

    var items = new List<(CustomDataLink Link, BasicMessage? Msg, string Raw)>();
    await foreach (var l in reader.StreamAsync())
    {
        string raw = await ipfsRpcApiStore.CatStringAsync(l.Cid);
        BasicMessage? parsed = null;
        try
        {
            parsed = JsonSerializer.Deserialize<BasicMessage>(raw, Circles.Profiles.Models.JsonSerializerOptions.JsonLd);
        }
        catch
        {
            /* tolerate non-ChatMessage payloads in the same inbox */
        }

        items.Add((l, parsed, raw));
        if (items.Count >= take * 2) break; // small buffer; we'll sort and trim
    }

    var newest = items
        .OrderByDescending(t => t.Link.SignedAt)
        .Take(take)
        .ToArray();

    if (newest.Length == 0)
    {
        Console.WriteLine("(no messages)");
        return;
    }

    Console.WriteLine($"Inbox for {recipientAddr} from {senderAddr} (latest {newest.Length}):");
    foreach (var (link, msg, raw) in newest)
    {
        var ts = DateTimeOffset.FromUnixTimeSeconds(link.SignedAt).UtcDateTime.ToString("u");
        var lineText = msg?.Text ?? raw;
        Console.WriteLine($"  [{ts}] {link.Name}  {lineText}");
    }
}

file sealed class ReplHistory
{
    private readonly string _path;
    private readonly List<string> _items;
    private readonly int _max;

    public ReplHistory(string path, int maxItems = 1000)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(nameof(path));
        _path = path;
        _max = maxItems > 0 ? maxItems : 1000;

        try
        {
            if (File.Exists(_path))
            {
                _items = File.ReadAllLines(_path)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
            }
            else
            {
                _items = new List<string>();
            }
        }
        catch (Exception ex)
        {
            throw new IOException($"Failed to load REPL history from {_path}", ex);
        }
    }

    public IReadOnlyList<string> Snapshot()
    {
        // return a copy to keep editor navigation stable while the loop runs
        return _items.ToArray();
    }

    public void Add(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        if (_items.Count > 0 && string.Equals(_items[^1], line, StringComparison.Ordinal))
            return; // avoid consecutive duplicates

        _items.Add(line);
        if (_items.Count > _max)
            _items.RemoveRange(0, _items.Count - _max);

        Save();
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllLines(_path, _items);
        }
        catch (Exception ex)
        {
            throw new IOException($"Failed to save REPL history to {_path}", ex);
        }
    }
}

file sealed class LineEditor
{
    private readonly ReplHistory _history;

    public LineEditor(ReplHistory history)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
    }

    public string ReadLine(string prompt)
    {
        if (prompt is null) prompt = string.Empty;

        var prevTreatCtrlC = Console.TreatControlCAsInput;
        Console.TreatControlCAsInput = true;
        try
        {
            var buffer = new StringBuilder();
            int cursor = 0;

            var snapshot = _history.Snapshot().ToList(); // oldest..newest
            int histPos = snapshot.Count; // N == “current”
            bool browsing = false;
            string savedCurrent = string.Empty;

            int baseTop = Console.CursorTop;
            int lastRenderLen = 0;

            void Render()
            {
                Console.SetCursorPosition(0, baseTop);
                var text = prompt + buffer.ToString();
                // clear old residue
                int clearCount = Math.Max(0, lastRenderLen - buffer.Length);
                Console.Write(text + new string(' ', clearCount));
                lastRenderLen = buffer.Length;
                Console.SetCursorPosition(prompt.Length + cursor, baseTop);
            }

            void LoadFromHistory(int pos)
            {
                if (pos < 0 || pos >= snapshot.Count) return;
                if (!browsing)
                {
                    savedCurrent = buffer.ToString();
                    browsing = true;
                }

                buffer.Clear();
                buffer.Append(snapshot[pos]);
                cursor = buffer.Length;
                Render();
            }

            // initial draw
            Console.Write(prompt);
            Render();

            while (true)
            {
                var key = Console.ReadKey(intercept: true);

                switch (key.Key)
                {
                    case ConsoleKey.Enter:
                        Console.WriteLine();
                        return buffer.ToString();

                    case ConsoleKey.LeftArrow:
                        cursor = Math.Max(0, cursor - 1);
                        Render();
                        break;

                    case ConsoleKey.RightArrow:
                        cursor = Math.Min(buffer.Length, cursor + 1);
                        Render();
                        break;

                    case ConsoleKey.Home:
                        cursor = 0;
                        Render();
                        break;

                    case ConsoleKey.End:
                        cursor = buffer.Length;
                        Render();
                        break;

                    case ConsoleKey.Backspace:
                        if (cursor > 0)
                        {
                            buffer.Remove(cursor - 1, 1);
                            cursor--;
                            Render();
                        }

                        break;

                    case ConsoleKey.Delete:
                        if (cursor < buffer.Length)
                        {
                            buffer.Remove(cursor, 1);
                            Render();
                        }

                        break;

                    case ConsoleKey.UpArrow:
                        if (snapshot.Count == 0) break;
                        if (histPos > 0) histPos--;
                        LoadFromHistory(histPos);
                        break;

                    case ConsoleKey.DownArrow:
                        if (!browsing) break;
                        if (histPos < snapshot.Count - 1)
                        {
                            histPos++;
                            LoadFromHistory(histPos);
                        }
                        else
                        {
                            // move back to the “current” line
                            histPos = snapshot.Count;
                            browsing = false;
                            buffer.Clear();
                            buffer.Append(savedCurrent);
                            cursor = buffer.Length;
                            Render();
                        }

                        break;

                    case ConsoleKey.Escape:
                        if (browsing)
                        {
                            // leave history → restore saved current
                            browsing = false;
                            histPos = snapshot.Count;
                            buffer.Clear();
                            buffer.Append(savedCurrent);
                            cursor = buffer.Length;
                            Render();
                        }
                        else
                        {
                            // already on current → clear it
                            if (buffer.Length > 0)
                            {
                                buffer.Clear();
                                cursor = 0;
                                Render();
                            }
                        }

                        break;

                    default:
                        // printable char (basic filter; allow space)
                        char ch = key.KeyChar;
                        if (!char.IsControl(ch))
                        {
                            buffer.Insert(cursor, ch);
                            cursor++;
                            Render();
                        }

                        break;
                }
            }
        }
        finally
        {
            Console.TreatControlCAsInput = prevTreatCtrlC;
        }
    }
}