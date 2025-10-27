using System.Text;
using Circles.Profiles.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace Circles.Market.Api;

internal sealed class CachingIpfsStore : IIpfsStore
{
    private const int MaxObjectBytes = 8 * 1024 * 1024; // 8 MiB cap

    private sealed class RefGate
    {
        public readonly SemaphoreSlim Sem = new(1, 1);
        public int RefCount;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, RefGate> Gates = new();

    private readonly IIpfsStore _inner;
    private readonly IMemoryCache _cache;

    public CachingIpfsStore(IIpfsStore inner, IMemoryCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    private sealed record CacheEntry(byte[] Bytes);

    public async Task<string> AddStringAsync(string json, bool pin = true, CancellationToken ct = default) =>
        await _inner.AddStringAsync(json, pin, ct);

    public async Task<string> AddBytesAsync(ReadOnlyMemory<byte> bytes, bool pin = true, CancellationToken ct = default) =>
        await _inner.AddBytesAsync(bytes, pin, ct);

    public async Task<Stream> CatAsync(string cid, CancellationToken ct = default)
    {
        bool cachedHit = _cache.TryGetValue<CacheEntry>(cid, out var entry);
        if (cachedHit)
        {
            return new MemoryStream(entry!.Bytes, writable: false);
        }

        var gate = Gates.GetOrAdd(cid, _ => new RefGate());
        Interlocked.Increment(ref gate.RefCount);
        await gate.Sem.WaitAsync(ct);
        try
        {
            bool cachedHit2 = _cache.TryGetValue<CacheEntry>(cid, out entry);
            if (cachedHit2)
            {
                return new MemoryStream(entry!.Bytes, writable: false);
            }

            await using var s = await _inner.CatAsync(cid, ct);
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();

            int sizeWithOverhead = bytes.Length + 128;
            bool tooLarge = sizeWithOverhead > MaxObjectBytes;
            if (tooLarge)
            {
                throw new PayloadTooLargeException();
            }

            var opts = new MemoryCacheEntryOptions()
                .SetSize(sizeWithOverhead)
                .SetPriority(CacheItemPriority.Low)
                .SetSlidingExpiration(TimeSpan.FromMinutes(30));

            _cache.Set(cid, new CacheEntry(bytes), opts);
            return new MemoryStream(bytes, writable: false);
        }
        finally
        {
            gate.Sem.Release();
            if (Interlocked.Decrement(ref gate.RefCount) == 0)
            {
                bool removed = Gates.TryRemove(cid, out var removedGate);
                if (removed)
                {
                    removedGate.Sem.Dispose();
                }
            }
        }
    }

    public async Task<string> CatStringAsync(string cid, CancellationToken ct = default)
    {
        await using var s = await CatAsync(cid, ct);
        using var sr = new StreamReader(s, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: false);
        return await sr.ReadToEndAsync(ct);
    }

    public Task<string> CalcCidAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct = default) =>
        _inner.CalcCidAsync(bytes, ct);
}