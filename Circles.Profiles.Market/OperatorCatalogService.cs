using System.Text.Json;
using Circles.Profiles.Aggregation;
using Circles.Profiles.Models.Market;

namespace Circles.Profiles.Market;

public sealed class OperatorCatalogService
{
    private readonly BasicAggregator _basic;
    private readonly CatalogReducer _reducer;

    public OperatorCatalogService(BasicAggregator basic, CatalogReducer reducer)
    {
        _basic = basic ?? throw new ArgumentNullException(nameof(basic));
        _reducer = reducer ?? throw new ArgumentNullException(nameof(reducer));
    }

    public async Task<(List<string> avatarsScanned, List<AggregatedCatalogItem> products, List<JsonElement> errors)>
        AggregateAsync(
            string operatorAddress,
            IReadOnlyList<string> avatars,
            long chainId,
            long start,
            long end,
            CancellationToken ct = default)
    {
        if (avatars is not { Count: > 0 })
        {
            throw new ArgumentException("avatars must contain at least one address", nameof(avatars));
        }

        string op = operatorAddress?.Trim().ToLowerInvariant()
                     ?? throw new ArgumentException("operatorAddress is required", nameof(operatorAddress));

        var normalizedAvatars = avatars
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var links = await _basic.AggregateLinksAsync(op, normalizedAvatars, chainId, start, end, ct);
        var (products, errors) = await _reducer.ReduceAsync(links.Links, links.Errors, ct);
        return (links.AvatarsScanned, products, errors);
    }
}
