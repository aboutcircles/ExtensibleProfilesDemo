using System.Text.Json;

namespace Circles.Profiles.Aggregation;

public sealed record AggregationLinksOutcome(
    List<string> AvatarsScanned,
    Dictionary<string, string> IndexHeads,
    List<LinkWithProvenance> Links,
    List<JsonElement> Errors
);
