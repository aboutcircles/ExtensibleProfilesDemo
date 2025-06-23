using Ipfs;

namespace Circles.Profiles.Sdk;

public static class CidConverter
{
    // multihash header for sha2-256 - 0x12 0x20
    private static readonly byte[] MhPrefix = { 0x12, 0x20 };

    public static byte[] CidToDigest(string cid)
    {
        byte[] full = Base58.Decode(cid);
        if (full.Length != 34 || full[0] != MhPrefix[0] || full[1] != MhPrefix[1])
        {
            throw new ArgumentException("CID is not a CIDv0 (sha2-256, 32 bytes)");
        }

        byte[] digest = new byte[32];
        Array.Copy(full, 2, digest, 0, 32);
        return digest;
    }

    public static string DigestToCid(byte[] digest32)
    {
        if (digest32.Length != 32) throw new ArgumentException("digest must be 32 bytes");
        byte[] full = new byte[34];
        MhPrefix.CopyTo(full, 0);
        Array.Copy(digest32, 0, full, 2, 32);
        return Base58.Encode(full);
    }
}