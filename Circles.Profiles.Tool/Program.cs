using System.Text.Json;
using Circles.Profiles.Models;
using Circles.Profiles.Models.Core;
using Circles.Profiles.Sdk;
using JsonSerializerOptions = System.Text.Json.JsonSerializerOptions;

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

        string profileCid = args[0];
        string ipfsUrl = args.Length > 1 ? args[1] : "http://127.0.0.1:5001/api/v0/";
        string? bearer = args.Length > 2 ? args[2] : null;

        await using var ipfs = new IpfsRpcApiStore(ipfsUrl, bearer);

        try
        {
            var tree = await LoadProfileTreeAsync(profileCid, ipfs, CancellationToken.None);

            var json = JsonSerializer.Serialize(tree, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
            Console.Out.WriteLine(json);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Circles.Profiles.Tool – dump whole profile tree as JSON");
        Console.WriteLine("Usage:");
        Console.WriteLine("  circles-profiles-tool <profileCid> [ipfsRpcUrl] [bearerToken]\n");
        Console.WriteLine("Defaults:");
        Console.WriteLine("  ipfsRpcUrl  = http://127.0.0.1:5001/api/v0/");
        Console.WriteLine("  bearerToken = (none)");
    }

    private static async Task<ProfileTreeDump> LoadProfileTreeAsync(
        string profileCid,
        IpfsRpcApiStore ipfs,
        CancellationToken ct)
    {
        await using var s = await ipfs.CatAsync(profileCid, ct);
        var profile = await JsonSerializer.DeserializeAsync<Profile>(s, Helpers.JsonOpts, ct)
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