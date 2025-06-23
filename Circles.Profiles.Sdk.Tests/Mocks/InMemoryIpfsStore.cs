using System.Security.Cryptography;
using System.Text;
using Circles.Profiles.Interfaces;

namespace Circles.Profiles.Sdk.Tests.Mocks;

/// <summary>A throw-away, thread-safe store for unit testing.</summary>
internal sealed class InMemoryIpfsStore : IIpfsStore
{
    private readonly Dictionary<string, byte[]> _blobs = new();
    private readonly object _gate = new();

    private static string NewCid()
    {
        // create a random 32-byte digest and wrap it in the multihash header 0x12 0x20
        Span<byte> digest = stackalloc byte[32];
        RandomNumberGenerator.Fill(digest);
        return CidConverter.DigestToCid(digest.ToArray()); // valid Base58 CID-v0
    }

    public Task<string> AddJsonAsync(string json, bool pin = true,
        CancellationToken ct = default) =>
        AddBytesAsync(Encoding.UTF8.GetBytes(json), pin, ct);

    public Task<string> AddBytesAsync(ReadOnlyMemory<byte> bytes, bool pin = true,
        CancellationToken ct = default)
    {
        var cid = NewCid();
        lock (_gate) _blobs[cid] = bytes.ToArray();
        return Task.FromResult(cid);
    }

    public Task<Stream> CatAsync(string cid, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_blobs.TryGetValue(cid, out var data))
                throw new KeyNotFoundException(cid);
            return Task.FromResult<Stream>(new MemoryStream(data, writable: false));
        }
    }

    public async Task<string> CatStringAsync(string cid, CancellationToken ct = default)
    {
        await using var stream = await CatAsync(cid, ct);
        using var sr = new StreamReader(stream);
        return await sr.ReadToEndAsync(ct);
    }

    public Task<string> CalcCidAsync(ReadOnlyMemory<byte> bytes,
        CancellationToken ct = default) =>
        Task.FromResult(NewCid());
}