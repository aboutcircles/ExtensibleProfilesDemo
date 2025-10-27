using Circles.PayloadSignerApi.Model;

namespace Circles.PayloadSignerApi;

public sealed class InMemoryBuyIntentStore : IBuyIntentStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, BuyIntent> _db = new();
    public Task SaveAsync(BuyIntent intent, CancellationToken ct = default)
    {
        _db[intent.Token] = intent;
        return Task.CompletedTask;
    }
    public Task<BuyIntent?> GetAsync(string token, CancellationToken ct = default)
    {
        _db.TryGetValue(token, out var v);
        return Task.FromResult(v);
    }
}