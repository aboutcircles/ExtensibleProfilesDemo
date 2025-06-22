using System.Text.Json;
using ExtensibleProfilesDemo.Model;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Model;
using Nethereum.Signer;
using Nethereum.Util;

namespace ExtensibleProfilesDemo;

public static class Program
{
    private const int ChunkMaxLinks = 100;
    private static readonly Sha3Keccack Keccak = new();


    /// <summary>
    /// Calculates and signs a link (Keccak-256, ECDSA, 65-byte r‖s‖v, hex-encoded).
    /// </summary>
    private static CustomDataLink SignLink(CustomDataLink link, string privKeyHex)
    {
        byte[] canon = link.CanonicaliseForSigning(); // RFC 8785 (no “signature”)
        byte[] hash = Keccak.CalculateHash(canon);

        var key = new EthECKey(privKeyHex);
        var sig = key.SignAndCalculateV(hash); // EthECDSASignature (r,s,v)

        byte[] rs = sig.To64ByteArray(); // 64-byte r‖s
        byte v = sig.V[0]; // recovery id (0/1 or 27/28)
        byte[] full = rs.Concat([v]).ToArray(); // 65 bytes total

        return link with { Signature = "0x" + full.ToHex() };
    }


    private static void EnsureSigningKey(Profile prof, string privKeyHex)
    {
        var key = new EthECKey(privKeyHex);
        string pkHex = "0x" + key.GetPubKeyNoPrefix().ToHex();
        byte[] fpRaw = Keccak.CalculateHash(pkHex.HexToByteArray());
        string fp = "0x" + fpRaw.AsSpan(0, 4).ToArray().ToHex(); // 4-byte fingerprint
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        prof.SigningKeys.TryAdd(fp, new SigningKey
        {
            PublicKey = pkHex,
            ValidFrom = now,
            ValidTo = null,
            RevokedAt = null
        });
    }

    /* ────────────────── CmdCreate (modified) ────────────────── */

    private static async Task CmdCreate(Dictionary<string, string> o, KeyManager km)
    {
        if (!CheckKey(o, km, out _, out var priv)) return;
        if (!o.TryGetValue("name", out var name) || !o.TryGetValue("description", out var desc))
        {
            Console.WriteLine("create needs --name --description");
            return;
        }

        string address = o.TryGetValue("address", out var a) ? a : new EthECKey(priv).GetPublicAddress();

        var prof = new Profile { Name = name, Description = desc };
        EnsureSigningKey(prof, priv); // ← NEW

        var ipfs = new IpfsService();
        string cid = await ipfs.AddJsonAsync(JsonSerializer.Serialize(prof, JsonOpts));

        var eth = new NameRegistry(priv);
        string? tx = await eth.UpdateProfileCidAsync(CidConverter.CidToDigest(cid));

        Console.WriteLine($"profile CID {cid}");
        CliLogger.Log(new
        {
            action = "create", address, profileCid = cid, txHash = tx, ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });
    }

    private static async Task CmdSend(Dictionary<string, string> o, KeyManager km)
    {
        if (!CheckKey(o, km, out _, out var priv))
        {
            return;
        }

        // declare first so the compiler sees them definitely-assigned
        string recipient = "";
        string typ = "";
        string txt = "";

        if (!o.TryGetValue("to", out recipient) ||
            !o.TryGetValue("type", out typ) ||
            !o.TryGetValue("text", out txt))
        {
            Console.WriteLine("send needs --to --type --text");
            return;
        }

        string sender = o.TryGetValue("from", out var fromArg)
            ? fromArg
            : new EthECKey(priv).GetPublicAddress();

        var msg = new ChatMessage
        {
            From = sender,
            To = recipient,
            Type = typ,
            Text = txt,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        var ipfs = new IpfsService();
        var eth = new NameRegistry(priv);
        var prof = await LoadOrNew(sender, eth, ipfs);
        EnsureSigningKey(prof, priv);

        string msgCid = await ipfs.AddJsonAsync(JsonSerializer.Serialize(msg, JsonOpts));

        /* ───── namespace index ───── */
        string ns = recipient.ToLowerInvariant();

        prof.Namespaces.TryGetValue(ns, out var indexCid);
        var indexDoc = await LoadIndexDoc(indexCid, ipfs);

        /* choose / rotate chunk */
        string? headCid = string.IsNullOrWhiteSpace(indexDoc.Head) ? null : indexDoc.Head;
        var chunk = await LoadChunk(headCid, ipfs);

        if (chunk.Links.Count >= ChunkMaxLinks)
        {
            chunk = new NamespaceChunk { Prev = headCid };
        }

        /* add link */
        var link = new CustomDataLink
        {
            Name = $"msg-{msg.Timestamp}",
            Cid = msgCid,
            Encrypted = false,
            SignedAt = msg.Timestamp,
            SignerAddress = sender,
            Nonce = CustomDataLink.NewNonce()
        };
        link = SignLink(link, priv);
        chunk.Links.Add(link);

        /* persist */
        string newChunkCid = await SaveChunk(chunk, ipfs);

        indexDoc.Head = newChunkCid; // ← no CS8852 anymore
        indexDoc.Entries[link.Name] = newChunkCid;
        string newIndexCid = await SaveIndexDoc(indexDoc, ipfs);

        prof.Namespaces[ns] = newIndexCid;
        string newProfCid = await ipfs.AddJsonAsync(JsonSerializer.Serialize(prof, JsonOpts));
        string? tx = await eth.UpdateProfileCidAsync(CidConverter.CidToDigest(newProfCid));

        Console.WriteLine($"sent.\n ├─ profile {newProfCid}\n ├─ index   {newIndexCid}\n └─ chunk   {newChunkCid}");
        CliLogger.Log(new
        {
            action = "send",
            from = sender,
            to = recipient,
            msgCid,
            chunkCid = newChunkCid,
            indexCid = newIndexCid,
            profileCid = newProfCid,
            txHash = tx,
            ts = msg.Timestamp
        });
    }

    private static async Task CmdLink(Dictionary<string, string> o, KeyManager km)
    {
        if (!CheckKey(o, km, out _, out var priv))
        {
            return;
        }

        string ns = "";
        string name = "";
        string cid = "";

        if (!o.TryGetValue("ns", out ns) ||
            !o.TryGetValue("name", out name) ||
            !o.TryGetValue("cid", out cid))
        {
            Console.WriteLine("link needs --ns addr --name n --cid cid");
            return;
        }

        string target = o.TryGetValue("profile", out var p)
            ? p
            : new EthECKey(priv).GetPublicAddress();

        var ipfs = new IpfsService();
        var eth = new NameRegistry(priv);
        var prof = await LoadOrNew(target, eth, ipfs);
        EnsureSigningKey(prof, priv);

        ns = ns.ToLowerInvariant();

        /* namespace index */
        prof.Namespaces.TryGetValue(ns, out var indexCid);
        var indexDoc = await LoadIndexDoc(indexCid, ipfs);

        /* locate or rotate chunk */
        NamespaceChunk chunk;
        if (indexDoc.Entries.TryGetValue(name, out var existingChunkCid))
        {
            chunk = await LoadChunk(existingChunkCid, ipfs);
        }
        else
        {
            string? headCid = string.IsNullOrWhiteSpace(indexDoc.Head) ? null : indexDoc.Head;
            chunk = await LoadChunk(headCid, ipfs);
        }

        if (chunk.Links.Count >= ChunkMaxLinks)
        {
            chunk = new NamespaceChunk { Prev = indexDoc.Head };
        }

        /* upsert link */
        var newLink = new CustomDataLink
        {
            Name = name,
            Cid = cid,
            Encrypted = false,
            SignedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            SignerAddress = target,
            Nonce = CustomDataLink.NewNonce()
        };
        newLink = SignLink(newLink, priv);

        int i = chunk.Links.FindIndex(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (i >= 0)
        {
            chunk.Links[i] = newLink;
        }
        else
        {
            chunk.Links.Add(newLink);
        }

        /* persist */
        string newChunkCid = await SaveChunk(chunk, ipfs);

        indexDoc.Head = newChunkCid; // ← mutable now
        indexDoc.Entries[name] = newChunkCid;
        string newIndexCid = await SaveIndexDoc(indexDoc, ipfs);

        prof.Namespaces[ns] = newIndexCid;
        string newProfCid = await ipfs.AddJsonAsync(JsonSerializer.Serialize(prof, JsonOpts));
        string? tx = await eth.UpdateProfileCidAsync(CidConverter.CidToDigest(newProfCid));

        Console.WriteLine(
            $"link saved.\n ├─ profile {newProfCid}\n ├─ index   {newIndexCid}\n └─ chunk   {newChunkCid}");
        CliLogger.Log(new
        {
            action = "link",
            target,
            ns,
            name,
            cid,
            chunkCid = newChunkCid,
            indexCid = newIndexCid,
            profileCid = newProfCid,
            txHash = tx,
            ts = newLink.SignedAt
        });
    }

    /* ────────────────── chunk helpers (modified) ────────────────── */

    private static async Task<NamespaceChunk> LoadChunk(string? cid, IpfsService ipfs)
    {
        if (cid is null) return new NamespaceChunk();
        string json = await ipfs.CatAsync(cid);
        if (string.IsNullOrWhiteSpace(json)) return new NamespaceChunk();

        NamespaceChunk chunk;
        try
        {
            chunk = JsonSerializer.Deserialize<NamespaceChunk>(json, JsonOpts)!;
        }
        catch
        {
            var legacy = JsonSerializer.Deserialize<List<CustomDataLink>>(json, JsonOpts) ?? new();
            chunk = new NamespaceChunk { Links = legacy };
        }

        return chunk with
        {
            Links = chunk.Links.Where(IsSigValid).ToList() // ← NEW
        };
    }

    private static bool IsSigValid(CustomDataLink l)
    {
        if (string.IsNullOrEmpty(l.Signature) || !l.Signature.StartsWith("0x"))
            return false;

        try
        {
            byte[] canon = l.CanonicaliseForSigning();
            byte[] hash = Keccak.CalculateHash(canon);

            // Parse the r|s|v hex string we stored
            var sig = EthECDSASignatureFactory.ExtractECDSASignature(l.Signature);

            var pub = EthECKey.RecoverFromSignature(sig, hash);
            return pub?.GetPublicAddress()
                .Equals(l.SignerAddress, StringComparison.OrdinalIgnoreCase) == true;
        }
        catch
        {
            return false; // malformed sig ⇒ invalid
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            Help();
            return;
        }

        string cmd = args[0].ToLowerInvariant();
        var opts = Parse(args.Skip(1).ToArray());
        var keys = new KeyManager();

        switch (cmd)
        {
            case "keygen":
                await CmdKeyGen(opts, keys); break;
            case "keyls":
                CmdKeyLs(keys); break;
            case "keyuse":
                CmdKeyUse(opts, keys); break;
            case "create":
                await CmdCreate(opts, keys); break;
            case "send":
                await CmdSend(opts, keys); break;
            case "inbox":
                await CmdInbox(opts, keys); break;
            case "link":
                await CmdLink(opts, keys); break;
            case "smoke": await CmdSmoke(keys); break;
            default:
                Console.WriteLine($"Unknown command: {cmd}");
                Help();
                break;
        }
    }

    /* ------------ key commands ------------ */

    private static Task CmdKeyGen(Dictionary<string, string> o, KeyManager km)
    {
        if (!o.TryGetValue("alias", out string? alias))
        {
            Console.WriteLine("keygen needs --alias <name>");
            return Task.CompletedTask;
        }

        if (km.GetPrivateKey(alias) is not null)
        {
            Console.WriteLine("alias already exists");
            return Task.CompletedTask;
        }

        var key = EthECKey.GenerateKey();
        km.Add(alias, key.GetPrivateKey());
        Console.WriteLine($"Generated key {alias} (public {key.GetPublicAddress()})");
        return Task.CompletedTask;
    }

    private static void CmdKeyLs(KeyManager km)
    {
        Console.WriteLine("stored keys:");
        foreach (var (alias, priv) in km.List())
        {
            var pub = new EthECKey(priv).GetPublicAddress();
            string mark = km.CurrentAlias == alias ? "*" : " ";
            Console.WriteLine($"{mark} {alias}  {pub}");
        }
    }

    private static void CmdKeyUse(Dictionary<string, string> o, KeyManager km)
    {
        if (!o.TryGetValue("alias", out string? alias))
        {
            Console.WriteLine("keyuse needs --alias <name>");
            return;
        }

        km.Use(alias);
        Console.WriteLine($"current key → {alias}");
    }

    private static async Task CmdInbox(Dictionary<string, string> o, KeyManager km)
    {
        if (!CheckKey(o, km, out _, out var priv)) return;

        string me = o.TryGetValue("me", out var meArg)
            ? meArg
            : new EthECKey(priv).GetPublicAddress();

        if (!o.TryGetValue("trust", out var csv))
        {
            Console.WriteLine("inbox needs --trust addr[,addr]");
            return;
        }

        string[] trusted = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var eth = new NameRegistry(priv);
        var ipfs = new IpfsService();
        var myProfile = await LoadOrNew(me, eth, ipfs);

        long shown = 0;
        string myNs = me.ToLowerInvariant();

        foreach (string sender in trusted)
        {
            /* ----------- look-up sender’s profile & namespace ----------- */
            string? profCid = await eth.GetProfileCidAsync(sender);
            if (profCid is null) continue;

            var senderProfile =
                JsonSerializer.Deserialize<Profile>(await ipfs.CatAsync(profCid), JsonOpts) ?? new Profile();

            if (!senderProfile.Namespaces.TryGetValue(myNs, out var indexCid)) continue;

            var indexDoc = await LoadIndexDoc(indexCid, ipfs);
            if (string.IsNullOrWhiteSpace(indexDoc.Head)) continue; // empty namespace

            long lastSeen = myProfile.LastRead.GetValueOrDefault(sender, 0);

            /* ----------- walk chunks newest → old until lastSeen -------- */
            string? cur = indexDoc.Head;
            while (cur is not null)
            {
                var chunk = await LoadChunk(cur, ipfs);

                foreach (var link in chunk.Links.OrderBy(l => l.SignedAt))
                {
                    if (link.SignedAt <= lastSeen) continue;

                    var msg = JsonSerializer.Deserialize<ChatMessage>(
                        await ipfs.CatAsync(link.Cid), JsonOpts);
                    if (msg is null) continue;

                    Console.WriteLine($"[{sender}] {msg.Type}@{msg.Timestamp}: {msg.Text}");
                    lastSeen = msg.Timestamp;
                    shown++;
                }

                /* optimisation: stop when the oldest link in this chunk is
                   already older than lastSeen */
                if (chunk.Links.Any() && chunk.Links.Min(l => l.SignedAt) <= lastSeen)
                    break;

                cur = chunk.Prev;
            }

            myProfile.LastRead[sender] = lastSeen;
        }

        if (shown == 0)
        {
            Console.WriteLine("no new messages");
            return;
        }

        string newProfileCid = await ipfs.AddJsonAsync(JsonSerializer.Serialize(myProfile, JsonOpts));
        string? tx = await eth.UpdateProfileCidAsync(CidConverter.CidToDigest(newProfileCid));

        Console.WriteLine($"inbox saved, profile CID {newProfileCid}");
        CliLogger.Log(new
        {
            action = "inbox_save",
            me,
            profileCid = newProfileCid,
            newReads = shown,
            txHash = tx,
            ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });
    }

    /* ------------ helpers ------------ */

    private static async Task<Profile> LoadOrNew(string addr, NameRegistry nameRegistry, IpfsService ipfs)
    {
        string? cid = await nameRegistry.GetProfileCidAsync(addr);
        if (cid is null)
        {
            return new();
        }

        var catResult = await ipfs.CatAsync(cid);
        var deserializedResult = JsonSerializer.Deserialize<Profile>(catResult, JsonOpts);

        if (deserializedResult is null)
        {
            throw new Exception("invalid profile data or cid");
        }

        return deserializedResult;
    }

    // returns true if we found a key
    private static bool CheckKey(
        Dictionary<string, string> o,
        KeyManager km,
        out string alias,
        out string priv)
    {
        alias = o.TryGetValue("key", out string? a) ? a : km.CurrentAlias ?? "";
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
            string tok = argv[i];
            if (!tok.StartsWith("--"))
            {
                i++;
                continue;
            }

            string key = tok[2..];
            string val = (i + 1 < argv.Length && !argv[i + 1].StartsWith("--")) ? argv[i + 1] : "true";
            d[key] = val;
            i += val == "true" ? 1 : 2;
        }

        return d;
    }

    private static async Task<NameIndexDoc> LoadIndexDoc(string? cid, IpfsService ipfs)
    {
        if (string.IsNullOrWhiteSpace(cid))
        {
            return new NameIndexDoc();
        }

        string json = await ipfs.CatAsync(cid);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new NameIndexDoc();
        }

        return JsonSerializer.Deserialize<NameIndexDoc>(json, JsonOpts) ?? new NameIndexDoc();
    }

    private static Task<string> SaveIndexDoc(NameIndexDoc doc, IpfsService ipfs) =>
        ipfs.AddJsonAsync(JsonSerializer.Serialize(doc, JsonOpts));

/* one-shot chunk pin, no ahead-of-time CID juggling */
    private static Task<string> SaveChunk(NamespaceChunk chunk, IpfsService ipfs) =>
        ipfs.AddJsonAsync(JsonSerializer.Serialize(chunk, JsonOpts));

    private static async Task SmokeSendAsync(
        string privKey,
        string senderAddr,
        string recipientAddr,
        string text)
    {
        var ipfs = new IpfsService();
        var eth = new NameRegistry(privKey);

        /* ---- build + pin chat-message ---- */
        var msg = new ChatMessage
        {
            From = senderAddr,
            To = recipientAddr,
            Type = "smoke",
            Text = text,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        string msgCid = await ipfs.AddJsonAsync(JsonSerializer.Serialize(msg, JsonOpts));

        /* ---- load or create sender profile ---- */
        var profile = await LoadOrNew(senderAddr, eth, ipfs);
        EnsureSigningKey(profile, privKey);

        /* ---- namespace index handling ---- */
        string ns = recipientAddr.ToLowerInvariant();
        profile.Namespaces.TryGetValue(ns, out var idxCid);
        var idxDoc = await LoadIndexDoc(idxCid, ipfs);
        var headChunk = await LoadChunk(string.IsNullOrWhiteSpace(idxDoc.Head) ? null : idxDoc.Head, ipfs);

        if (headChunk.Links.Count >= ChunkMaxLinks)
            headChunk = new NamespaceChunk { Prev = idxDoc.Head };

        var link = new CustomDataLink
        {
            Name = $"smoke-{msg.Timestamp}",
            Cid = msgCid,
            Encrypted = false,
            SignedAt = msg.Timestamp,
            SignerAddress = senderAddr,
            Nonce = CustomDataLink.NewNonce()
        };
        link = SignLink(link, privKey);
        headChunk.Links.Add(link);

        /* ---- persist chunk, index, profile ---- */
        string newChunkCid = await SaveChunk(headChunk, ipfs);

        idxDoc.Head = newChunkCid;
        idxDoc.Entries[link.Name] = newChunkCid;
        string newIdxCid = await SaveIndexDoc(idxDoc, ipfs);

        profile.Namespaces[ns] = newIdxCid;
        string newProfCid = await ipfs.AddJsonAsync(JsonSerializer.Serialize(profile, JsonOpts));

        await eth.UpdateProfileCidAsync(CidConverter.CidToDigest(newProfCid));

        Console.WriteLine($"smoke: {senderAddr[..10]}… → {recipientAddr[..10]}…  ok");
    }

    private static async Task CmdSmoke(KeyManager km)
    {
        // aliases that must already exist – we just reuse them
        var aliases = new[] { "1", "2", "alice" };

        // make sure we have every key
        foreach (var a in aliases.Where(a => km.GetPrivateKey(a) is null))
        {
            Console.WriteLine($"❌ smoke-test needs key alias “{a}” – run keygen / keyuse first");
            return;
        }

        // helper: alias → priv / addr
        var privOf = aliases.ToDictionary(a => a, a => km.GetPrivateKey(a)!);
        var addrOf = privOf.ToDictionary(kv => kv.Key, kv => new EthECKey(kv.Value).GetPublicAddress());

        async Task Send(string from, string to, string text) =>
            await CmdSend(new Dictionary<string, string>
            {
                ["key"] = from,
                ["from"] = addrOf[from],
                ["to"] = addrOf[to],
                ["type"] = "ping",
                ["text"] = text
            }, km);

        await Send("1", "2", "hi 1→2");
        await Send("2", "1", "hi 2→1");
        await Send("alice", "1", "gm 👋");
        await Send("1", "alice", "hey!");
        await Send("alice", "2", "yo 2");
        await Send("2", "alice", "back at ya");

        // read-back verification phase
        foreach (var me in aliases)
        {
            string trustCsv = string.Join(',', aliases.Where(a => a != me).Select(a => addrOf[a]));
            await CmdInbox(new Dictionary<string, string>
            {
                ["key"] = me,
                ["me"] = addrOf[me],
                ["trust"] = trustCsv
            }, km);
        }

        Console.WriteLine("✅ smoke-test finished without errors");
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
        Console.WriteLine(" smoke                              quick ping-pong test");
    }
}