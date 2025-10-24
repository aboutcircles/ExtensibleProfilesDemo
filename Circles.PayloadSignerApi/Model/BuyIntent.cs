using System.Text.Json.Serialization;

namespace Circles.PayloadSignerApi.Model;

public sealed record BuyIntent
{
    [JsonPropertyName("token")] public required string Token { get; init; }
    [JsonPropertyName("seller")] public required string Seller { get; init; }
    [JsonPropertyName("namespace")] public required string Namespace { get; init; }
    [JsonPropertyName("sku")] public required string Sku { get; init; }
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; init; }

    public static BuyIntent Create(string seller, string ns, string sku)
    {
        return new BuyIntent
        {
            Token = NewToken(),
            Seller = seller,
            Namespace = ns,
            Sku = sku,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public static string NewToken()
    {
        Span<byte> bytes = stackalloc byte[24]; // 192-bit
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}