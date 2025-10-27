using Circles.Profiles.Models.Core;

namespace Circles.Profiles.Aggregation;

public sealed record LinkWithProvenance(
    string Avatar,
    string ChunkCid,
    int IndexInChunk,
    CustomDataLink Link,
    string LinkKeccak);
