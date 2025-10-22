using Circles.Profiles.Models;
using Circles.Profiles.Models.Core;

namespace Circles.Profiles.Interfaces;

/// <summary>Profile CRUD – *no* business logic beyond pin+publish.</summary>
public interface IProfileStore
{
    /// <summary>
    /// Fetch the avatar’s profile or <see langword="null"/> when the
    /// registry has no CID yet.
    /// </summary>
    Task<Profile?> FindAsync(string avatar, CancellationToken ct = default);

    Task<(Profile prof, string cid)> SaveAsync(Profile profile, string signerPrivKey,
        CancellationToken ct = default);
}