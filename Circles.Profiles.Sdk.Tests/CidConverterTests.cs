namespace Circles.Profiles.Sdk.Tests;

[TestFixture]
public class CidConverterTests
{
    [Test]
    public void Roundtrip_32BytesDigest_ReturnsOriginal()
    {
        var digest = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var cid    = CidConverter.DigestToCid(digest);
        var back   = CidConverter.CidToDigest(cid);

        Assert.That(back, Is.EqualTo(digest));
    }

    [Test]
    public void CidToDigest_InvalidLength_Throws()
        => Assert.Throws<ArgumentException>(() => CidConverter.CidToDigest("foo"));
}