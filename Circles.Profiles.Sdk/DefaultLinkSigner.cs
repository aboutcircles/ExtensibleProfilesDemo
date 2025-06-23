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

    public bool Verify(CustomDataLink link)
    {
        if (string.IsNullOrWhiteSpace(link.Signature) || !link.Signature.StartsWith("0x"))
            return false;

        try
        {
            var sig = EthECDSASignatureFactory.ExtractECDSASignature(link.Signature);
            var hash = Sha3.Keccak256Bytes(CanonicalJson.CanonicaliseWithoutSignature(link));
            var pub = EthECKey.RecoverFromSignature(sig, hash);
            return pub?.GetPublicAddress()
                .Equals(link.SignerAddress, StringComparison.OrdinalIgnoreCase) == true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex.Message);
            System.Diagnostics.Debug.WriteLine(ex.StackTrace);

            return false;
        }
    }
}