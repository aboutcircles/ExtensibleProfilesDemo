using Circles.Profiles.Interfaces;
using Circles.Profiles.Models.Core;
using Circles.Profiles.Sdk.Utils;
using Nethereum.Hex.HexConvertors.Extensions;

namespace Circles.Profiles.Sdk;

/// <summary>Utility to sign a pre-built CustomDataLink without persisting.</summary>
public static class LinkSigning
{
    public static async Task<CustomDataLink> SignAsync(
        CustomDataLink link,
        ISigner signer,
        CancellationToken ct = default)
    {
        if (signer is null) { throw new ArgumentNullException(nameof(signer)); }

        var withAddr = link with { SignerAddress = signer.Address };
        byte[] canonical = CanonicalJson.CanonicaliseWithoutSignature(withAddr);
        byte[] sig = await signer.SignAsync(canonical, withAddr.ChainId, ct).ConfigureAwait(false);

        return withAddr with { Signature = "0x" + sig.ToHex() };
    }
}
