namespace Circles.PayloadSignerApi.Model;

public sealed record BuyIntentResponse
{
    public required string Token { get; init; }
    public required string Seller { get; init; }
    public required string Namespace { get; init; }
    public required string Sku { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    public static BuyIntentResponse From(BuyIntent i) => new()
    {
        Token = i.Token,
        Seller = i.Seller,
        Namespace = i.Namespace,
        Sku = i.Sku,
        CreatedAt = i.CreatedAt
    };
}