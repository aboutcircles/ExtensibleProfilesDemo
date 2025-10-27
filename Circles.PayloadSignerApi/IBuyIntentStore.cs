using Circles.PayloadSignerApi.Model;

namespace Circles.PayloadSignerApi;

public interface IBuyIntentStore
{
    Task SaveAsync(BuyIntent intent, CancellationToken ct = default);
    Task<BuyIntent?> GetAsync(string token, CancellationToken ct = default);
}