using System.Collections.Concurrent;

namespace Circles.Market.Api.Cart;

public interface IBasketStore
{
    Basket Create(string? operatorAddr, long? chainId);
    (Basket basket, bool expired)? Get(string basketId);
    Basket Patch(string basketId, Action<Basket> patch);
    bool TryFreeze(string basketId);
}

internal class BasketRecord
{
    public Basket Basket { get; set; } = new();
    public DateTimeOffset ExpiresAt { get; set; }
}

public class InMemoryBasketStore : IBasketStore
{
    private readonly ConcurrentDictionary<string, BasketRecord> _baskets = new();

    private static string NewId(string prefix)
        => prefix + Guid.NewGuid().ToString("N").Substring(0, 22);

    public Basket Create(string? operatorAddr, long? chainId)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string id = NewId("bkt_");
        var b = new Basket
        {
            BasketId = id,
            Operator = operatorAddr,
            ChainId = chainId ?? 100,
            Status = nameof(BasketStatus.Draft),
            CreatedAt = now,
            ModifiedAt = now,
            TtlSeconds = 86400
        };

        var rec = new BasketRecord
        {
            Basket = b,
            ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(now).AddSeconds(b.TtlSeconds)
        };
        _baskets[id] = rec;
        return b;
    }

    public (Basket basket, bool expired)? Get(string basketId)
    {
        if (!_baskets.TryGetValue(basketId, out var rec)) return null;
        bool expired = DateTimeOffset.UtcNow >= rec.ExpiresAt || string.Equals(rec.Basket.Status, nameof(BasketStatus.Expired), StringComparison.Ordinal);
        return (rec.Basket, expired);
    }

    public Basket Patch(string basketId, Action<Basket> patch)
    {
        if (!_baskets.TryGetValue(basketId, out var rec)) throw new KeyNotFoundException();
        if (rec.Basket.Status is nameof(BasketStatus.CheckedOut)) throw new InvalidOperationException("Basket already checked out");
        patch(rec.Basket);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        rec.Basket.ModifiedAt = now;
        rec.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(rec.Basket.TtlSeconds);
        return rec.Basket;
    }

    public bool TryFreeze(string basketId)
    {
        if (!_baskets.TryGetValue(basketId, out var rec)) return false;
        if (rec.Basket.Status is nameof(BasketStatus.CheckedOut)) return false;
        rec.Basket.Status = nameof(BasketStatus.CheckedOut);
        return true;
    }
}
