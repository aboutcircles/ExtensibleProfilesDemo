using System.Text.Json;
using Circles.Profiles.Interfaces;
using Circles.Profiles.Models;
using Circles.Profiles.Sdk;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Signer;
using Nethereum.Util;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;

namespace ExtensibleProfilesDemo;

/// <summary>
/// Command-line helper for Circles “extensible profiles”.
/// All heavy lifting lives in **Circles.Profiles.Sdk** – this file is just glue + UX.
/// </summary>
public static class Program
{
    /* ────────────────────────── shared helpers ───────────────────────── */
    private static async Task<Profile> LoadOrNewAsync(
        IProfileStore store,
        string avatar,
        CancellationToken ct = default)
    {
        return await store.FindAsync(avatar, ct) ?? new Profile();
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly Sha3Keccack Keccak = new();

    /// <summary>Adds a <see cref="SigningKey"/> to the profile if it isn’t there yet.</summary>
    private static void EnsureSigningKey(Profile p, string priv)
    {
        var key = new EthECKey(priv);
        var pkHex = "0x" + key.GetPubKeyNoPrefix().ToHex();
        var fp = "0x" + Keccak.CalculateHash(pkHex.HexToByteArray())
            .AsSpan(0, 4).ToArray().ToHex(); // 4-byte fingerprint
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        p.SigningKeys.TryAdd(fp, new SigningKey
        {
            PublicKey = pkHex,
            ValidFrom = now
        });
    }

    /* ───────────────────────────── commands ──────────────────────────── */

    private static async Task CmdCreate(
        Dictionary<string, string> o, KeyManager km)
    {
        if (!CheckKey(o, km, out _, out var priv)) return;
        if (!o.TryGetValue("name", out var name) ||
            !o.TryGetValue("description", out var desc))
        {
            Console.WriteLine("create needs --name and --description");
            return;
        }

        var address = o.TryGetValue("address", out var a)
            ? a
            : new EthECKey(priv).GetPublicAddress();

        var profile = new Profile { Name = name, Description = desc };
        EnsureSigningKey(profile, priv);

        await using var ipfs = new IpfsStore();
        var registry = new NameRegistry(priv, Config.RpcUrl);
        var store = new ProfileStore(ipfs, registry);

        var (_, cid) = await store.SaveAsync(profile, priv);

        Console.WriteLine($"profile CID  {cid}");
        CliLogger.Log(new
        {
            action = "create", address, profileCid = cid,
            ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });
    }

    private static async Task CmdSend(
        Dictionary<string, string> o, KeyManager km)
    {
        if (!CheckKey(o, km, out _, out var priv)) return;

        if (!o.TryGetValue("to", out var recipient) ||
            !o.TryGetValue("type", out var typ) ||
            !o.TryGetValue("text", out var txt))
        {
            Console.WriteLine("send needs --to --type --text");
            return;
        }

        var sender = o.TryGetValue("from", out var f)
            ? f
            : new EthECKey(priv).GetPublicAddress();

        Account account = new Account(priv);
        var web3 = new Web3(account, Config.RpcUrl);
        var chainApi = new EthereumChainApi(web3, 100);

        var msg = new ChatMessage
        {
            From = sender,
            To = recipient,
            Type = typ,
            Text = txt,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        /* profile + namespace writer */
        await using var ipfs = new IpfsStore();
        var registry = new NameRegistry(priv, Config.RpcUrl);
        var store = new ProfileStore(ipfs, registry);

        var profile = await LoadOrNewAsync(store, sender);

        EnsureSigningKey(profile, priv);

        ILinkSigner signer = o.TryGetValue("safe", out var safeAddr)
            ? new SafeLinkSigner(safeAddr, chainApi)
            : new DefaultLinkSigner();

        var writer = await NamespaceWriter.CreateAsync(profile, recipient, ipfs, signer);

        /* persist */
        var link = await writer.AddJsonAsync($"msg-{msg.Timestamp}",
            JsonSerializer.Serialize(msg, JsonOpts), priv);
        var (_, newProfileCid) = await store.SaveAsync(profile, priv);

        var idxCid = profile.Namespaces[recipient.ToLowerInvariant()];
        var idxDoc = await Helpers.LoadIndex(idxCid, ipfs);
        var chunkCid = idxDoc.Entries[link.Name];

        Console.WriteLine($"sent.\n ├─ profile {newProfileCid}\n ├─ index   {idxCid}\n └─ chunk   {chunkCid}");
        CliLogger.Log(new
        {
            action = "send",
            from = sender,
            to = recipient,
            msgCid = link.Cid,
            chunkCid,
            indexCid = idxCid,
            profileCid = newProfileCid,
            ts = msg.Timestamp
        });
    }

    private static async Task CmdLink(
        Dictionary<string, string> o, KeyManager km)
    {
        if (!CheckKey(o, km, out _, out var priv)) return;

        if (!o.TryGetValue("ns", out var ns) ||
            !o.TryGetValue("name", out var name) ||
            !o.TryGetValue("cid", out var cid))
        {
            Console.WriteLine("link needs --ns --name --cid");
            return;
        }

        var target = o.TryGetValue("profile", out var p)
            ? p
            : new EthECKey(priv).GetPublicAddress();

        await using var ipfs = new IpfsStore();
        var registry = new NameRegistry(priv, Config.RpcUrl);
        var store = new ProfileStore(ipfs, registry);

        var profile = await LoadOrNewAsync(store, target);

        EnsureSigningKey(profile, priv);

        var writer = await NamespaceWriter.CreateAsync(profile, ns, ipfs, new DefaultLinkSigner());
        var link = await writer.AttachExistingCidAsync(name, cid, priv);
        var (_, newProfileCid) = await store.SaveAsync(profile, priv);

        var idxCid = profile.Namespaces[ns.ToLowerInvariant()];
        var idxDoc = await Helpers.LoadIndex(idxCid, ipfs);
        var chunkCid = idxDoc.Entries[link.Name];

        Console.WriteLine($"link saved.\n ├─ profile {newProfileCid}\n ├─ index   {idxCid}\n └─ chunk   {chunkCid}");
        CliLogger.Log(new
        {
            action = "link",
            target,
            ns,
            name,
            cid,
            chunkCid,
            indexCid = idxCid,
            profileCid = newProfileCid,
            ts = link.SignedAt
        });
    }

    private static async Task CmdInbox(
        Dictionary<string, string> o, KeyManager km, long lastSeen)
    {
        if (!CheckKey(o, km, out _, out var priv)) return;

        var me = o.TryGetValue("me", out var meArg)
            ? meArg
            : new EthECKey(priv).GetPublicAddress();

        if (!o.TryGetValue("trust", out var csv))
        {
            Console.WriteLine("inbox needs --trust addr1[,addr2]");
            return;
        }

        string[] trusted = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        await using var ipfs = new IpfsStore();
        var chainApi = new EthereumChainApi(new Web3(new Account(priv), Config.RpcUrl), 100);
        var verifier = new DefaultSignatureVerifier(chainApi);
        var registry = new NameRegistry(priv, Config.RpcUrl);

        string myNs = me.ToLowerInvariant();
        long shown = 0;

        foreach (var sender in trusted)
        {
            string? profCid = await registry.GetProfileCidAsync(sender);
            if (profCid is null) continue;

            var profJson = await ipfs.CatStringAsync(profCid);
            var senderProf = JsonSerializer.Deserialize<Profile>(profJson, JsonOpts);
            if (senderProf is null ||
                !senderProf.Namespaces.TryGetValue(myNs, out var idxCid)) continue;

            var idx = await Helpers.LoadIndex(idxCid, ipfs);
            var reader = new DefaultNamespaceReader(idx.Head, ipfs, verifier);

            await foreach (var link in reader.StreamAsync(lastSeen))
            {
                string raw = await ipfs.CatStringAsync(link.Cid);
                var msg = JsonSerializer.Deserialize<ChatMessage>(raw, JsonOpts);
                if (msg is null) continue;

                Console.WriteLine($"[{sender}] {msg.Type}@{msg.Timestamp}: {msg.Text}");
                lastSeen = msg.Timestamp;
                shown++;
            }
        }

        if (shown == 0) Console.WriteLine("no new messages");
    }

    /* ───────────────────── key-management commands ───────────────────── */

    private static Task CmdKeyGen(Dictionary<string, string> o, KeyManager km)
    {
        if (!o.TryGetValue("alias", out var alias))
        {
            Console.WriteLine("keygen needs --alias");
            return Task.CompletedTask;
        }

        if (km.GetPrivateKey(alias) is not null)
        {
            Console.WriteLine("alias already exists");
            return Task.CompletedTask;
        }

        var k = EthECKey.GenerateKey();
        km.Add(alias, k.GetPrivateKey());
        Console.WriteLine($"generated key {alias} (public {k.GetPublicAddress()})");
        return Task.CompletedTask;
    }

    private static void CmdKeyLs(KeyManager km)
    {
        Console.WriteLine("stored keys:");
        foreach (var (alias, priv) in km.List())
        {
            var pub = new EthECKey(priv).GetPublicAddress();
            string m = km.CurrentAlias == alias ? "*" : " ";
            Console.WriteLine($"{m} {alias,-8} {pub}");
        }
    }

    private static void CmdKeyUse(Dictionary<string, string> o, KeyManager km)
    {
        if (!o.TryGetValue("alias", out var alias))
        {
            Console.WriteLine("keyuse needs --alias");
            return;
        }

        km.Use(alias);
        Console.WriteLine($"current key → {alias}");
    }

    /* ───────────────────── smoke-test (demo helper) ───────────────────── */

    private static async Task SmokeSendAsync(
        string privKey, string senderAddr, string recipientAddr, string text, KeyManager km)
    {
        await CmdSend(new Dictionary<string, string>
        {
            ["key"] = km.List().First(a => km.GetPrivateKey(a.alias) == privKey).alias, // find alias
            ["from"] = senderAddr,
            ["to"] = recipientAddr,
            ["type"] = "ping",
            ["text"] = text
        }, km);
    }

    private static async Task CmdSmoke(KeyManager km)
    {
        /* we expect the aliases “1”, “2”, “alice” to exist */
        var required = new[] { "alice", "bob", "charly" };
        foreach (var a in required)
            if (km.GetPrivateKey(a) is null)
            {
                Console.WriteLine($"❌ smoke-test needs key alias “{a}” – run keygen first");
                return;
            }

        var privOf = required.ToDictionary(a => a, a => km.GetPrivateKey(a)!);
        var addrOf = privOf.ToDictionary(kv => kv.Key, kv => new EthECKey(kv.Value).GetPublicAddress());

        /* quick ping-pong */
        await SmokeSendAsync(privOf["alice"], addrOf["alice"], addrOf["bob"], "hi alice→bob", km);
        await SmokeSendAsync(privOf["bob"], addrOf["bob"], addrOf["alice"], "hi bob→alice", km);
        await SmokeSendAsync(privOf["alice"], addrOf["alice"], addrOf["alice"], "gm 👋", km);
        await SmokeSendAsync(privOf["alice"], addrOf["alice"], addrOf["alice"], "hey!", km);
        await SmokeSendAsync(privOf["alice"], addrOf["alice"], addrOf["bob"], "yo bob", km);
        await SmokeSendAsync(privOf["bob"], addrOf["bob"], addrOf["alice"], "back at ya", km);

        /* read-back for each participant */
        foreach (var me in required)
        {
            string trustCsv = string.Join(',', required.Where(a => a != me).Select(a => addrOf[a]));
            await CmdInbox(new Dictionary<string, string>
            {
                ["key"] = me,
                ["me"] = addrOf[me],
                ["trust"] = trustCsv
            }, km, 0);
        }

        Console.WriteLine("✅ smoke-test finished without errors");
    }

    /* ───────────────────────── plumbing / UX ─────────────────────────── */

    private static bool CheckKey(
        Dictionary<string, string> o,
        KeyManager km,
        out string alias,
        out string priv)
    {
        alias = o.TryGetValue("key", out var a)
            ? a
            : km.CurrentAlias ?? "";

        if (alias.Length == 0)
        {
            Console.WriteLine("no key selected – run keygen / keyuse or pass --key");
            priv = "";
            return false;
        }

        priv = km.GetPrivateKey(alias) ?? "";
        if (priv.Length == 0)
        {
            Console.WriteLine($"key not found: {alias}");
            return false;
        }

        return true;
    }

    private static Dictionary<string, string> Parse(string[] argv)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < argv.Length;)
        {
            var tok = argv[i];
            if (!tok.StartsWith("--"))
            {
                i++;
                continue;
            }

            var key = tok[2..];
            var val = (i + 1 < argv.Length && !argv[i + 1].StartsWith("--"))
                ? argv[i + 1]
                : "true";
            d[key] = val;
            i += val == "true" ? 1 : 2;
        }

        return d;
    }

    private static void Help()
    {
        Console.WriteLine("Circles CLI");
        Console.WriteLine(" keygen  --alias name             generate & store a new key");
        Console.WriteLine(" keyls                           list keys");
        Console.WriteLine(" keyuse --alias name             make key default");
        Console.WriteLine(" create --name n --description d --address 0x.. [--key k]");
        Console.WriteLine(" send   --from a --to b --type t --text txt     [--key k]");
        Console.WriteLine(" inbox  --me a --trust addr1,addr2              [--key k]");
        Console.WriteLine(" link   --ns addr --name n --cid cid  [--profile addr] [--key k]");
        Console.WriteLine(" smoke                              quick ping-pong demo");
    }

    /* ───────────────────────── entry-point ───────────────────────────── */

    public static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            Help();
            return;
        }

        var cmd = args[0].ToLowerInvariant();
        var opts = Parse(args.Skip(1).ToArray());
        var keys = new KeyManager();

        switch (cmd)
        {
            case "keygen": await CmdKeyGen(opts, keys); break;
            case "keyls": CmdKeyLs(keys); break;
            case "keyuse": CmdKeyUse(opts, keys); break;
            case "create": await CmdCreate(opts, keys); break;
            case "send": await CmdSend(opts, keys); break;
            case "inbox": await CmdInbox(opts, keys, 0); break;
            case "link": await CmdLink(opts, keys); break;
            case "smoke": await CmdSmoke(keys); break;
            default:
                Console.WriteLine($"unknown command: {cmd}");
                Help();
                break;
        }
    }
}