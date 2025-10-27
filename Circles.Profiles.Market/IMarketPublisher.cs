using Circles.Profiles.Interfaces;
using Circles.Profiles.Models.Market;

namespace Circles.Profiles.Market;

public interface IMarketPublisher
{
    Task<string> UpsertProductAsync(
        Models.Core.Profile sellerProfile,
        ISigner signer,
        string operatorAddress,
        SchemaOrgProduct product,
        CancellationToken ct = default);

    Task<string> TombstoneProductAsync(
        Models.Core.Profile sellerProfile,
        ISigner signer,
        string operatorAddress,
        string sku,
        CancellationToken ct = default);
}
