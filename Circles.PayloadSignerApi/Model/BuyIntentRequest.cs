namespace Circles.PayloadSignerApi.Model;

public sealed record BuyIntentRequest
{
    public required string Seller { get; init; }
    public required string Namespace { get; init; }
    public required string Sku { get; init; }
}