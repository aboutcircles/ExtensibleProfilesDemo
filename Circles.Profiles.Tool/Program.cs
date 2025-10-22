using System.Text.Json;
using System.Linq;
using Circles.Profiles.Models;
using Circles.Profiles.Models.Core;
using Circles.Profiles.Models.Market;
using Circles.Profiles.Sdk;
using JsonSerializerOptions = Circles.Profiles.Models.JsonSerializerOptions;

namespace Circles.Profiles.Tool;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return 1;
        }

        string cmd = args[0].ToLowerInvariant();
        string[] tail = args.Skip(1).ToArray();

        try
        {
            return cmd switch
            {
                "dump" => await CmdDumpAsync(tail),
                "init-profile" => await CmdInitProfileAsync(tail),
                "add-product" => await CmdAddProductAsync(tail),
                "publish" => await CmdPublishAsync(tail),
                _ => await TrySmartDumpOrHelpAsync(args)
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }
    }

    /* ───────────────────────────────────── commands ────────────────── */

    private static async Task<int> CmdDumpAsync(string[] args)
    {
        var (ipfsUrl, bearer, _, _) = ReadCommonEnvAndArgs(args);
        if (args.Length == 0)
        {
            Console.Error.WriteLine("dump requires <profileCid>");
            return 2;
        }

        string profileCid = args[0];

        await using var ipfs = new IpfsRpcApiStore(ipfsUrl, bearer);
        var tree = await LoadProfileTreeAsync(profileCid, ipfs, CancellationToken.None);

        var json = JsonSerializer.Serialize(tree, JsonSerializerOptions.JsonLd);
        Console.Out.WriteLine(json);
        return 0;
    }

    private static async Task<int> CmdInitProfileAsync(string[] args)
    {
        var (ipfsUrl, bearer, rpcUrl, privKey) = ReadCommonEnvAndArgs(args);
        string name = GetOpt(args, "--name") ?? throw new ArgumentException("--name is required");
        string? desc = GetOpt(args, "--description") ?? GetOpt(args, "-d");

        await using var ipfs = new IpfsRpcApiStore(ipfsUrl, bearer);
        var registry = new NameRegistry(RequirePrivKey(privKey), rpcUrl);
        var store = new ProfileStore(ipfs, registry);

        var profile = new Profile { Name = name, Description = desc ?? string.Empty };
        var (_, cid) = await store.SaveAsync(profile, RequirePrivKey(privKey));

        Console.WriteLine($"Profile created and published. CID={cid}");
        return 0;
    }

    private static async Task<int> CmdAddProductAsync(string[] args)
    {
        var (ipfsUrl, bearer, rpcUrl, privKey) = ReadCommonEnvAndArgs(args);
        string sku = GetOpt(args, "--sku") ?? throw new ArgumentException("--sku is required");
        string name = GetOpt(args, "--name") ?? throw new ArgumentException("--name is required");
        string currency = GetOpt(args, "--currency") ?? "CRC";
        string priceStr = GetOpt(args, "--price") ?? "100";
        if (!decimal.TryParse(priceStr, out decimal price))
            throw new ArgumentException("--price must be a decimal");

        string? nsKey = GetOpt(args, "--ns"); // defaults to signer address
        string? url = GetOpt(args, "--url");
        string? brand = GetOpt(args, "--brand");
        string? category = GetOpt(args, "--category");
        string? img = GetOpt(args, "--image");

        await using var ipfs = new IpfsRpcApiStore(ipfsUrl, bearer);

        // See: QmWAYK6xx6LY8tDoKNnVGP1cxSUDfqLmLnziJytYpRrFvn
        // Large: QmUnSuuvR1tXpfyTX8EQZsyWEnuRFX7oSBfsfWeM3Sthqv

        // Load or create profile in-memory (if none exists yet)
        var profile = new Profile { Name = GetOpt(args, "--profile-name") ?? "new-profile" };

        for (var i = 1; i <= 10; i++)
        {
            var offer = new SchemaOrgOffer
            {
                Price = price,
                PriceCurrency = currency,
                Url = url,
                Checkout = "https://app.metri.xyz/transfer/0xde374ece6fa50e781e81aac78e811b33d16912c7/crc/100",
                CirclesAvailabilityFeed = "https://example.com/api/availability/" + sku + i,
                CirclesInventoryFeed = "https://example.com/api/inventory/" + sku + i,
            };
            var product = new SchemaOrgProduct
            {
                Name = name,
                Description = GetOpt(args, "--description") ?? GetOpt(args, "-d"),
                Sku = sku + i,
                Brand = brand,
                Category = category,
                Url = url,
                Offers = { offer },
                DateModified = DateTimeOffset.UtcNow,
                Image =
                [
                    new ImageRef
                    {
                        Object = new SchemaOrgImageObject
                        {
                            ContentUrl = "cid://Qm124234234",
                            Url = "https://prd.place/400"
                        }
                    },
                    new ImageRef
                    {
                        Object = new SchemaOrgImageObject
                        {
                            ContentUrl = "cid://Qm353490580934",
                            Url = "https://prd.place/400"
                        }
                    }
                ]
            };
            if (!string.IsNullOrWhiteSpace(img) && Uri.TryCreate(img, UriKind.Absolute, out var imgUri))
            {
                product.Image.Add(new ImageRef { Url = imgUri });
            }

            // Determine namespace key (lower-case address of signer by default)
            string signerAddress = new Nethereum.Web3.Accounts.Account(RequirePrivKey(privKey)).Address;
            string ns = (nsKey ?? signerAddress).ToLowerInvariant();

            var signer = new EoaLinkSigner();
            var writer = await NamespaceWriter.CreateAsync(profile, ns, ipfs, signer);

            string logicalName = $"product/{sku + i}";
            string json = JsonSerializer.Serialize(product, JsonSerializerOptions.JsonLd);
            var link = await writer.AddJsonAsync(logicalName, json, RequirePrivKey(privKey));

            Console.WriteLine($"Added product as {logicalName} (CID={link.Cid})");
        }

        // Publish profile digest (pins profile+index to IPFS and updates registry)
        var registry = new NameRegistry(RequirePrivKey(privKey), rpcUrl);
        var store = new ProfileStore(ipfs, registry);
        var (_, cid) = await store.SaveAsync(profile, RequirePrivKey(privKey));

        Console.WriteLine($"Commited profile CID={cid}");
        return 0;
    }

    private static async Task<int> CmdPublishAsync(string[] args)
    {
        var (ipfsUrl, bearer, rpcUrl, privKey) = ReadCommonEnvAndArgs(args);
        // simply re-save the profile passed by CID? Better: publish current local composed profile.
        // For simplicity, require a CID to publish as the new digest.
        string profileCid = GetOpt(args, "--cid") ?? throw new ArgumentException("--cid is required");

        await using var ipfs = new IpfsRpcApiStore(ipfsUrl, bearer);
        await using var s = await ipfs.CatAsync(profileCid);
        var profile = await JsonSerializer.DeserializeAsync<Profile>(s, Models.JsonSerializerOptions.JsonLd) ??
                      new Profile();

        var registry = new NameRegistry(RequirePrivKey(privKey), rpcUrl);
        var store = new ProfileStore(ipfs, registry);
        var (_, cid) = await store.SaveAsync(profile, RequirePrivKey(privKey));
        Console.WriteLine($"Published profile. CID={cid}");
        return 0;
    }

    private static async Task<int> TrySmartDumpOrHelpAsync(string[] args)
    {
        // Backward-compatible behavior: if first arg looks like CID, treat as dump
        string first = args[0];
        bool looksCid = first.Length >= 46 && first.StartsWith("Qm", StringComparison.Ordinal);
        if (looksCid)
        {
            return await CmdDumpAsync(args);
        }

        PrintUsage();
        return 1;
    }

    /* ────────────────────────── helpers ───────────────────────────── */

    private static (string ipfsUrl, string? bearer, string rpcUrl, string? privKey) ReadCommonEnvAndArgs(string[] args)
    {
        string ipfsUrl = GetOpt(args, "--ipfs") ??
                         Environment.GetEnvironmentVariable("IPFS_RPC_URL") ?? "http://127.0.0.1:5001/api/v0/";
        string? bearer = GetOpt(args, "--bearer") ?? Environment.GetEnvironmentVariable("IPFS_RPC_BEARER");
        string rpc = GetOpt(args, "--rpc") ?? Environment.GetEnvironmentVariable("RPC") ?? "http://localhost:8545";
        string? pk = GetOpt(args, "--private-key") ?? Environment.GetEnvironmentVariable("PRIVATE_KEY");
        return (ipfsUrl, bearer, rpc, pk);
    }

    private static string? GetOpt(string[] args, string name)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return i + 1 < args.Length ? args[i + 1] : string.Empty;
            }
        }

        return null;
    }

    private static string RequirePrivKey(string? pk)
    {
        if (string.IsNullOrWhiteSpace(pk))
            throw new ArgumentException("--private-key or env PRIVATE_KEY is required for this command");
        return pk;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Circles.Profiles.Tool – create and manage profiles and products");
        Console.WriteLine("Usage:");
        Console.WriteLine("  circles-profiles-tool dump <profileCid> [--ipfs URL] [--bearer TOKEN]");
        Console.WriteLine(
            "  circles-profiles-tool init-profile --name NAME [-d DESC] [--ipfs URL] [--bearer TOKEN] [--rpc URL] [--private-key HEX]");
        Console.WriteLine(
            "  circles-profiles-tool add-product --sku SKU --name NAME --price DEC [--currency EUR] [--url URL] [--brand STR] [--category STR] [--image URL] [--ns 0xaddr] [--ipfs URL] [--bearer TOKEN] [--rpc URL] [--private-key HEX]");
        Console.WriteLine(
            "  circles-profiles-tool publish --cid CID [--ipfs URL] [--bearer TOKEN] [--rpc URL] [--private-key HEX]\n");
        Console.WriteLine("Environment defaults: IPFS_RPC_URL, IPFS_RPC_BEARER, RPC, PRIVATE_KEY");
        Console.WriteLine(
            "Notes: add-product writes a link named product/<sku> into the namespace keyed by your signer address unless --ns is provided. After writing, the profile is saved and published via updateMetadataDigest.");
    }

    private static async Task<ProfileTreeDump> LoadProfileTreeAsync(
        string profileCid,
        IpfsRpcApiStore ipfs,
        CancellationToken ct)
    {
        await using var s = await ipfs.CatAsync(profileCid, ct);
        var profile = await JsonSerializer.DeserializeAsync<Profile>(s, Models.JsonSerializerOptions.JsonLd, ct)
                      ?? new Profile();

        var namespaces = new Dictionary<string, NamespaceDump>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in profile.Namespaces)
        {
            string nsKey = kv.Key; // expected: lowercase address
            string indexCid = kv.Value; // profile stores index CID

            // Load index
            var index = await Helpers.LoadIndex(indexCid, ipfs, ct);

            // Walk chunks from head → prev
            var chunks = new List<ChunkDump>();
            string? cur = index.Head;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (!string.IsNullOrWhiteSpace(cur) && seen.Add(cur))
            {
                var chunk = await Helpers.LoadChunk(cur, ipfs, ct);
                chunks.Add(new ChunkDump(cur!, chunk));
                cur = chunk.Prev;
            }

            namespaces[nsKey] = new NamespaceDump(indexCid, index, chunks);
        }

        return new ProfileTreeDump(profileCid, profile, namespaces);
    }
}

internal sealed record ProfileTreeDump(
    string ProfileCid,
    Profile Profile,
    Dictionary<string, NamespaceDump> Namespaces);

internal sealed record NamespaceDump(
    string IndexCid,
    NameIndexDoc Index,
    List<ChunkDump> Chunks);

internal sealed record ChunkDump(
    string Cid,
    NamespaceChunk Chunk);