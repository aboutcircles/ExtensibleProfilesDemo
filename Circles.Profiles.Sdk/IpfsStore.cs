using System.Text;
using Circles.Profiles.Interfaces;
using Ipfs.CoreApi;

namespace Circles.Profiles.Sdk;

/// <summary>Thin wrapper around <c>Ipfs.Http.IpfsClient</c>.</summary>
public sealed class IpfsStore : IIpfsStore, IAsyncDisposable
{
    private readonly Ipfs.Http.IpfsClient _client;
    private readonly HttpClient _raw; // for /api/v0/cat streaming

    public IpfsStore(string apiBase = "http://127.0.0.1:5001")
    {
        _client = new Ipfs.Http.IpfsClient(apiBase);
        _raw = new HttpClient { BaseAddress = new Uri($"{apiBase}/api/v0/") };
    }

    public async Task<string> AddJsonAsync(string json, bool pin = true,
        CancellationToken ct = default) =>
        await AddBytesAsync(Encoding.UTF8.GetBytes(json), pin, ct);

    public async Task<string> AddBytesAsync(ReadOnlyMemory<byte> bytes, bool pin = true,
        CancellationToken ct = default)
    {
        await using var ms = new MemoryStream(bytes.ToArray());
        var opts = new AddFileOptions { Pin = pin, Wrap = false };
        var node = await _client.FileSystem.AddAsync(ms, options: opts, cancel: ct);
        return node.Id.Hash.ToString();
    }

    public async Task<Stream> CatAsync(string cid, CancellationToken ct = default)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"cat?arg={cid}")
            { Content = new StringContent("") };
        var res = await _raw.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsStreamAsync(ct);
    }

    public async Task<string> CatStringAsync(string cid, CancellationToken ct = default)
    {
        await using var stream = await CatAsync(cid, ct);
        using var sr = new StreamReader(stream);
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
}