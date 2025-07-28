using Circles.Profiles.Interfaces;
using Circles.Profiles.Models;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Model;
using Nethereum.Signer;

namespace Circles.Profiles.Sdk;

public sealed class DefaultLinkSigner : ILinkSigner
{
    public CustomDataLink Sign(CustomDataLink link, string privKeyHex)
    {
        var key = new EthECKey(privKeyHex);
        link = link with { SignerAddress = key.GetPublicAddress() };

        var hash = Sha3.Keccak256Bytes(CanonicalJson.CanonicaliseWithoutSignature(link));
        var sig = key.SignAndCalculateV(hash);
        var bytes = sig.To64ByteArray().Concat([sig.V[0]]).ToArray();

        return link with { Signature = "0x" + bytes.ToHex() };
    }
}