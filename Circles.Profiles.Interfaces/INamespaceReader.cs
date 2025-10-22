using Circles.Profiles.Models;
using Circles.Profiles.Models.Core;

namespace Circles.Profiles.Interfaces;

public interface INamespaceReader
{
    /// Latest link bearing that *logical* name, or `null`.
    Task<CustomDataLink?> GetLatestAsync(string logicalName,
        CancellationToken ct = default);

    /// Streams **newest → oldest**, *already* filtered on the SDK side so that
    /// only links with `signedAt > newerThanUnixTs` are yielded.
    IAsyncEnumerable<CustomDataLink> StreamAsync(
        long newerThanUnixTs = 0,
        CancellationToken ct = default);
}