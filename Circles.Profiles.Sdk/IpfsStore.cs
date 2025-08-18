using System.Text;
using System.Text.RegularExpressions;
using Circles.Profiles.Interfaces;
using Ipfs.CoreApi;

namespace Circles.Profiles.Sdk;

/// <summary>
/// Thin wrapper around the go‑IPFS HTTP API:
/// * CIDv0 whitelist (46 char base58btc, *must* start with “Qm”)  
/// * 8 MiB response cap – **header pre‑check + run‑time guard**  
/// * URL‑encoded query strings
/// </summary>
public sealed class IpfsStore : IIpfsStore, IAsyncDisposable
{
    private readonly Ipfs.Http.IpfsClient _client;
    private readonly HttpClient _raw;

    private static readonly Regex CidV0 =
        new("^Qm[1-9A-HJ-NP-Za-km-z]{44}$", RegexOptions.Compiled); // 46 chars, CID‑v0 only

    private const int MaxJsonBytes = 8 * 1024 * 1024; // 8 MiB

    public IpfsStore(string apiBase = "http://127.0.0.1:5001")
    {
        _client = new Ipfs.Http.IpfsClient(apiBase);
        _raw = new HttpClient { BaseAddress = new Uri($"{apiBase}/api/v0/") };
    }

    /* ───────────────────────────── write helpers ───────────────────────── */

    public async Task<string> AddJsonAsync(string json, bool pin = true,
        CancellationToken ct = default)
    {
        string cid = await AddBytesAsync(Encoding.UTF8.GetBytes(json), pin, ct);
        return cid;
    }

    public async Task<string> AddBytesAsync(ReadOnlyMemory<byte> bytes, bool pin = true,
        CancellationToken ct = default)
    {
        await using var ms = new MemoryStream(bytes.ToArray());
        var opts = new AddFileOptions { Pin = pin, Wrap = false };
        var node = await _client.FileSystem.AddAsync(ms, options: opts, cancel: ct);

        return node.Id.Hash.ToString();
    }

    /* ───────────────────────────── read helpers ────────────────────────── */

    public async Task<Stream> CatAsync(string cid, CancellationToken ct = default)
    {
        ValidateCid(cid);

        var req = new HttpRequestMessage(HttpMethod.Post,
            $"cat?arg={Uri.EscapeDataString(cid)}");

        var res = await _raw.SendAsync(req,
            HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();

        if (res.Content.Headers.ContentLength is { } len and > MaxJsonBytes)
        {
            throw new IOException(
                $"IPFS response advertises {len:N0} bytes – exceeds hard cap of {MaxJsonBytes:N0} bytes");
        }

        var stream = await res.Content.ReadAsStreamAsync(ct);
        return new LimitedReadStream(stream, MaxJsonBytes);
    }

    public async Task<string> CatStringAsync(string cid, CancellationToken ct = default)
    {
        await using var s = await CatAsync(cid, ct);
        using var sr = new StreamReader(s, Encoding.UTF8, false, 4096, leaveOpen: false);
        return await sr.ReadToEndAsync(ct);
    }

    public async Task<string> CalcCidAsync(ReadOnlyMemory<byte> bytes,
        CancellationToken ct = default)
    {
        await using var ms = new MemoryStream(bytes.ToArray());
        var opts = new AddFileOptions { Pin = false, Wrap = false, OnlyHash = true };
        var node = await _client.FileSystem.AddAsync(ms, options: opts, cancel: ct);
        return node.Id.Hash.ToString();
    }

    public ValueTask DisposeAsync()
    {
        _raw.Dispose();
        return ValueTask.CompletedTask;
    }

    /* ───────────────────────────── internals ───────────────────────────── */

    private static void ValidateCid(string cid)
    {
        if (!CidV0.IsMatch(cid))
            throw new ArgumentException(
                $"CID must be CID‑v0 (46 chars base58btc, starts with “Qm”): {cid}", nameof(cid));
    }

    private sealed class LimitedReadStream(Stream inner, long limit) : Stream
    {
        private readonly long _limit = limit;
        private long _remaining = limit;

        private void ThrowIfExceeded()
        {
            if (_remaining <= 0)
                throw new IOException($"IPFS response exceeds {_limit:N0} bytes");
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ThrowIfExceeded();
            int n = inner.Read(buffer, offset, (int)Math.Min(count, _remaining));
            _remaining -= n;
            return n;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ThrowIfExceeded();
            int n = await inner.ReadAsync(
                buffer[..(int)Math.Min(buffer.Length, _remaining)], cancellationToken);
            _remaining -= n;
            return n;
        }

        /* ---- boiler‑plate passthroughs ---- */
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();
        public override long Seek(long o, SeekOrigin k) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
        }
    }
}