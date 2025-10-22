using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Circles.Profiles.Interfaces;
using Circles.Profiles.Sdk.Utils;

namespace Circles.Profiles.Sdk;

/// <summary>
/// Thin wrapper around the Kubo-compatible HTTP API (RPC):
/// • CIDv0 whitelist (46 char base58btc, must start with “Qm”)
/// • 8 MiB response cap – header pre-check + run-time guard
/// • URL-encoded query strings
///
/// Defaults to local RPC: http://127.0.0.1:5001/api/v0/
/// To use a remote RPC (e.g. Filebase), pass the baseUrl and a Bearer token.
/// Endpoints used: /api/v0/add, /api/v0/pin/add, /api/v0/cat
/// </summary>
public sealed class IpfsRpcApiStore : IIpfsStore, IAsyncDisposable
{
    private static readonly Regex CidV0 =
        new("^Qm[1-9A-HJ-NP-Za-km-z]{44}$", RegexOptions.Compiled); // 46 chars, CID-v0 only

    private const int MaxJsonBytes = 8 * 1024 * 1024; // 8 MiB

    private readonly HttpClient _rpc;

    /// <param name="baseUrl">
    /// Local Kubo (default): "http://127.0.0.1:5001/api/v0/"
    /// Remote RPC (e.g., Filebase): "https://rpc.filebase.io/api/v0/"
    /// </param>
    /// <param name="bearerToken">Optional. Set only when your RPC requires Bearer auth.</param>
    public IpfsRpcApiStore(
        string baseUrl = "http://127.0.0.1:5001/api/v0/",
        string? bearerToken = null)
    {
        _rpc = new HttpClient { BaseAddress = new Uri(EnsureTrailingSlash(baseUrl)) };

        bool hasToken = !string.IsNullOrWhiteSpace(bearerToken);
        if (hasToken)
        {
            _rpc.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }
    }

    /* ───────────────────────────── write helpers ───────────────────────── */

    public async Task<string> AddStringAsync(string json, bool pin = true, CancellationToken ct = default)
    {
        return await AddBytesAsync(Encoding.UTF8.GetBytes(json), pin, ct);
    }

    public async Task<string> AddBytesAsync(ReadOnlyMemory<byte> bytes, bool pin = true, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        using var payload = new ByteArrayContent(bytes.ToArray());
        payload.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(payload, "file", "payload.bin");

        // /add (RPC). We'll pin explicitly via /pin/add for portability.
        var addUrl = "add?cid-version=0&wrap-with-directory=false";
        var req = new HttpRequestMessage(HttpMethod.Post, addUrl) { Content = form };

        using var res = await _rpc.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();

        // /add can emit multiple JSON lines; take the last object.
        string text = await res.Content.ReadAsStringAsync(ct);
        string last = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault() ?? string.Empty;
        bool noLast = string.IsNullOrWhiteSpace(last);
        if (noLast)
        {
            throw new InvalidOperationException("Empty response from /add");
        }

        using var doc = JsonDocument.Parse(last);
        bool hasHash = doc.RootElement.TryGetProperty("Hash", out var hashProp);
        if (!hasHash)
        {
            throw new InvalidOperationException("/add response missing 'Hash'");
        }

        string cid = hashProp.GetString() ?? throw new InvalidOperationException("Invalid 'Hash' value");
        ValidateCid(cid);

        if (pin)
        {
            // POST /pin/add?arg=<cid>
            string url = $"pin/add?arg={Uri.EscapeDataString(cid)}";
            using var pinReq = new HttpRequestMessage(HttpMethod.Post, url);
            using var pinRes = await _rpc.SendAsync(pinReq, ct);
            bool ok = pinRes.IsSuccessStatusCode;
            if (!ok)
            {
                string body = await pinRes.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException(
                    $"pin/add failed: {(int)pinRes.StatusCode} {pinRes.ReasonPhrase}; body: {body}");
            }
        }

        return cid;
    }

    /* ───────────────────────────── read helpers ────────────────────────── */

    public async Task<Stream> CatAsync(string cid, CancellationToken ct = default)
    {
        ValidateCid(cid);

        // POST /cat?arg=<cid> (RPC)
        string url = $"cat?arg={Uri.EscapeDataString(cid)}";
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        var res = await _rpc.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();

        bool hasLen = res.Content.Headers.ContentLength != null;
        long len = res.Content.Headers.ContentLength ?? -1;
        bool tooBig = hasLen && len > MaxJsonBytes;
        if (tooBig)
        {
            res.Dispose();
            throw new IOException(
                $"IPFS response advertises {len:N0} bytes – exceeds hard cap of {MaxJsonBytes:N0} bytes");
        }

        var stream = await res.Content.ReadAsStreamAsync(ct);
        // Lease the HttpResponseMessage lifetime to the returned stream.
        return new LimitedReadStream(stream, MaxJsonBytes, res);
    }

    public async Task<string> CatStringAsync(string cid, CancellationToken ct = default)
    {
        await using var s = await CatAsync(cid, ct);
        using var sr = new StreamReader(s, Encoding.UTF8, false, 4096, leaveOpen: false);
        return await sr.ReadToEndAsync(ct);
    }

    public async Task<string> CalcCidAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        using var payload = new ByteArrayContent(bytes.ToArray());
        payload.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(payload, "file", "payload.bin");

        // Kubo-compatible "only-hash" path (RPC).
        var url = "add?cid-version=0&wrap-with-directory=false&only-hash=true";
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };

        using var res = await _rpc.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();

        string text = await res.Content.ReadAsStringAsync(ct);
        string last = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault() ?? string.Empty;
        bool empty = string.IsNullOrWhiteSpace(last);
        if (empty)
        {
            throw new InvalidOperationException("Empty response from /add (only-hash)");
        }

        using var doc = JsonDocument.Parse(last);
        bool hasHash = doc.RootElement.TryGetProperty("Hash", out var hashProp);
        if (!hasHash)
        {
            throw new InvalidOperationException("/add (only-hash) response missing 'Hash'");
        }

        string cid = hashProp.GetString() ?? throw new InvalidOperationException("Invalid 'Hash' value");
        ValidateCid(cid);
        return cid;
    }

    public ValueTask DisposeAsync()
    {
        _rpc.Dispose();
        return ValueTask.CompletedTask;
    }

    /* ───────────────────────────── internals ───────────────────────────── */

    private static void ValidateCid(string cid)
    {
        bool isCidV0 = CidV0.IsMatch(cid);
        if (!isCidV0)
        {
            throw new ArgumentException($"CID must be CID-v0 (46 chars base58btc, starts with “Qm”): {cid}",
                nameof(cid));
        }
    }

    private static string EnsureTrailingSlash(string s) => s.EndsWith('/') ? s : (s + "/");
}