using System.Text.Json;
using Circles.Profiles.Interfaces;
using Circles.Profiles.Models;
using Circles.Profiles.Models.Core;
using JsonSerializerOptions = System.Text.Json.JsonSerializerOptions;

namespace Circles.Profiles.Sdk;

/// <summary>
/// Shared, low‑level helpers for namespace chunks / indices.<br/>
/// *Not* public API – the SDK layers call this internally.
/// </summary>
public static class Helpers
{
    public const int ChunkMaxLinks = 100;
    public const long DefaultChainId = 100; // Gnosis Chain (0x64)

    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Loads an index document from IPFS.<br/>
    /// </summary>
    public static async Task<NameIndexDoc> LoadIndex(
        string? cid,
        IIpfsStore ipfs,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cid))
        {
            throw new ArgumentException("CID is missing", nameof(cid));
        }

        await using var s = await ipfs.CatAsync(cid, ct);
        return await JsonSerializer.DeserializeAsync<NameIndexDoc>(s, JsonOpts, ct)
               ?? new NameIndexDoc();
    }

    public static async Task<NamespaceChunk> LoadChunk(
        string? cid,
        IIpfsStore ipfs,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cid))
            return new NamespaceChunk();

        await using var stream = await ipfs.CatAsync(cid, ct);

        try
        {
            var chunk = await JsonSerializer.DeserializeAsync<NamespaceChunk>(stream, JsonOpts, ct);
            if (chunk is null)
                throw new JsonException("Deserialised chunk is null");

            return chunk;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Preserve the CID in the exception so ops can locate & fix the pin
            throw new InvalidDataException($"Invalid NamespaceChunk JSON in CID {cid}", ex);
        }
    }

    internal static Task<string> SaveChunk(
        NamespaceChunk chunk,
        IIpfsStore ipfs,
        CancellationToken ct = default) =>
        ipfs.AddStringAsync(JsonSerializer.Serialize(chunk, JsonOpts), pin: true, ct);

    internal static Task<string> SaveIndex(
        NameIndexDoc idx,
        IIpfsStore ipfs,
        CancellationToken ct = default) =>
        ipfs.AddStringAsync(JsonSerializer.Serialize(idx, JsonOpts), pin: true, ct);
}