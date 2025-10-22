using Circles.Profiles.Models;
using Circles.Profiles.Models.Core;

namespace Circles.Profiles.Interfaces;

/// <summary>
/// A “namespace” is the <em>pair</em> (ownerAvatar, namespaceKey).
/// It behaves like an append-only log with random access via link-name.
/// </summary>
public interface INamespaceWriter
{
    /* single-link helpers --------------------------------------------------- */

    Task<CustomDataLink> AddJsonAsync(string logicalName,
        string json,
        string signerPrivKey,
        CancellationToken ct = default);

    Task<CustomDataLink> AttachExistingCidAsync(string logicalName,
        string cid,
        string signerPrivKey,
        CancellationToken ct = default);

    /* bulk helpers – flushed atomically (one chunk rotation at most) -------- */

    Task<IReadOnlyList<CustomDataLink>> AddJsonBatchAsync(
        IEnumerable<(string name, string json)> items,
        string signerPrivKey,
        CancellationToken ct = default);

    Task<IReadOnlyList<CustomDataLink>> AttachCidBatchAsync(
        IEnumerable<(string name, string cid)> items,
        string signerPrivKey,
        CancellationToken ct = default);
}