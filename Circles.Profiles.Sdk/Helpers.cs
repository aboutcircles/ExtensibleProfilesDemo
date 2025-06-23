using System.Text.Json;
using Circles.Profiles.Interfaces;
using Circles.Profiles.Models;

namespace Circles.Profiles.Sdk;

/// <summary>
/// Shared, low-level helpers for namespace chunks / indices.
/// *Not* public API – the SDK layers call this internally.
/// </summary>
public static class Helpers
{
    public const int ChunkMaxLinks = 100; // keep in one place

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static async Task<NameIndexDoc> LoadIndex(
        string? cid,
        IIpfsStore ipfs,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cid))
            return new NameIndexDoc();

        using var s = await ipfs.CatAsync(cid, ct);
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

        using var stream = await ipfs.CatAsync(cid, ct);
        var chunk = await JsonSerializer.DeserializeAsync<NamespaceChunk>(stream, JsonOpts, ct);

        // never throw: fall back to empty chunk on bad JSON
        return chunk ?? new NamespaceChunk();
    }

    internal static Task<string> SaveChunk(
        NamespaceChunk chunk,
        IIpfsStore ipfs,
        CancellationToken ct = default) =>
        ipfs.AddJsonAsync(JsonSerializer.Serialize(chunk, JsonOpts), pin: true, ct);

    internal static Task<string> SaveIndex(
        NameIndexDoc idx,
        IIpfsStore ipfs,
        CancellationToken ct = default) =>
        ipfs.AddJsonAsync(JsonSerializer.Serialize(idx, JsonOpts), pin: true, ct);
}