namespace Circles.Profiles.Interfaces;

/// <summary>
/// Strategy that decides whether a given <paramref name="signature"/> is valid for
/// <paramref name="hash"/> according to the rules of the account at <paramref name="signerAddress"/>.
/// </summary>
public interface ISignatureVerifier
{
    Task<bool> VerifyAsync(
        byte[] hash,
        string signerAddress,
        byte[] signature,
        CancellationToken ct = default);
}