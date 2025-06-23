namespace Circles.Profiles.Interfaces;

/// <summary>Pure IPFS store abstraction.</summary>
public interface IIpfsStore
{
    /* Pins UTF-8 JSON (convenience) */
    Task<string> AddJsonAsync(string json,
        bool pin = true,
        CancellationToken ct = default);

    /* Raw byte variant – same semantics */
    Task<string> AddBytesAsync(ReadOnlyMemory<byte> bytes,
        bool pin = true,
        CancellationToken ct = default);

    Task<Stream> CatAsync(string cid, CancellationToken ct = default);

    Task<string> CatStringAsync(string cid, CancellationToken ct = default);

    /* hash-only: no upload */
    Task<string> CalcCidAsync(ReadOnlyMemory<byte> bytes,
        CancellationToken ct = default);
}