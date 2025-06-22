using System.Net.Http.Headers;
using System.Text;
using Ipfs.CoreApi;

namespace ExtensibleProfilesDemo;

internal sealed class IpfsService
{
    private static readonly Uri ApiBase = new("http://127.0.0.1:5001/api/v0/");
    private readonly Ipfs.Http.IpfsClient _client = new("http://127.0.0.1:5001");
    private readonly HttpClient _http = new() { BaseAddress = ApiBase };

    public async Task<string> AddJsonAsync(string json)
    {
        byte[] data = Encoding.UTF8.GetBytes(json);
        await using var ms = new MemoryStream(data);

        var node = await _client.FileSystem.AddAsync(
                ms,
                options: new AddFileOptions { Pin = true, Wrap = false })
            .ConfigureAwait(false);

        return node.Id.Hash.ToString();
    }

    public async Task<string> CatAsync(string cid)
    {
        string route = $"cat?arg={Uri.EscapeDataString(cid)}";

        // POST is mandatory; body can be empty.
        var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = new StringContent(string.Empty) // dummy content keeps some proxies happy
        };
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/octet-stream"));

        using HttpResponseMessage response =
            await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        await using Stream s = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var reader = new StreamReader(s, Encoding.UTF8);

        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    public async Task<string> AddJsonAsync(string json, bool pin = true)
    {
        await using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var opts = new AddFileOptions { Pin = pin, Wrap = false };
        var node = await _client.FileSystem.AddAsync(ms, options: opts).ConfigureAwait(false);
        return node.Id.Hash.ToString();
    }

    /// <summary>
    /// Calculates the CID (SHA-256) without pinning or uploading the data.
    /// </summary>
    public async Task<string> CalcCidAsync(string json)
    {
        await using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var opts = new AddFileOptions { Pin = false, Wrap = false, OnlyHash = true };
        var node = await _client.FileSystem.AddAsync(ms, options: opts).ConfigureAwait(false);
        return node.Id.Hash.ToString();
    }
}