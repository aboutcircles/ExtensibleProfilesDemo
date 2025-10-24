using Circles.Profiles.Models.Market;

namespace Circles.Profiles.Market;

public interface IMarketPublisher
{
    Task<string> UpsertProductAsync(
        Models.Core.Profile sellerProfile,
        string sellerSignerPrivateKey,
        string operatorAddress,
        SchemaOrgProduct product,
        CancellationToken ct = default);

    Task<string> TombstoneProductAsync(
        Models.Core.Profile sellerProfile,
        string sellerSignerPrivateKey,
        string operatorAddress,
        string sku,
        CancellationToken ct = default);

    // NEW: Safe-aware overloads (preferred)
    Task<string> UpsertProductAsync(
        Models.Core.Profile sellerProfile,
        string sellerSignerPrivateKey,
        string operatorAddress,
        string sellerSafeAddress,
        SchemaOrgProduct product,
        CancellationToken ct = default);

    Task<string> TombstoneProductAsync(
        Models.Core.Profile sellerProfile,
        string sellerSignerPrivateKey,
        string operatorAddress,
        string sellerSafeAddress,
        string sku,
        CancellationToken ct = default);
}
