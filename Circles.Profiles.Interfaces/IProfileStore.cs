using Circles.Profiles.Models;
using Circles.Profiles.Models.Core;

namespace Circles.Profiles.Interfaces;

/// <summary>Profile CRUD – *no* business logic beyond pin+publish.</summary>
public interface IProfileStore
{
    Task<Profile?> FindAsync(string avatar, CancellationToken ct = default);

    Task<(Profile prof, string cid)> SaveAsync(
        Profile profile,
        ISigner signer,
        CancellationToken ct = default);
}