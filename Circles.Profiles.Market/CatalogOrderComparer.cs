using Circles.Profiles.Models.Market;

namespace Circles.Profiles.Market;

public sealed class CatalogOrderComparer : IComparer<AggregatedCatalogItem>
{
    public int Compare(AggregatedCatalogItem? x, AggregatedCatalogItem? y)
    {
        if (ReferenceEquals(x, y)) { return 0; }
        if (x is null) { return -1; }
        if (y is null) { return 1; }

        int byTs = y.PublishedAt.CompareTo(x.PublishedAt);
        if (byTs != 0) { return byTs; }

        int byIdx = y.IndexInChunk.CompareTo(x.IndexInChunk);
        if (byIdx != 0) { return byIdx; }

        int bySeller = string.CompareOrdinal(x.Seller, y.Seller);
        if (bySeller != 0) { return bySeller; }

        return string.CompareOrdinal(x.LinkKeccak, y.LinkKeccak);
    }
}
