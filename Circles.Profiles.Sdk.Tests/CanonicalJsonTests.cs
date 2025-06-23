using System.Text;
using Circles.Profiles.Models;

namespace Circles.Profiles.Sdk.Tests;

[TestFixture]
public class CanonicalJsonTests
{
    [Test]
    public void CanonicaliseWithoutSignature_StableFieldOrder()
    {
        var link = new CustomDataLink
        {
            Name = "n", Cid = "c", Signature = "0xdead"
        };

        byte[] a = CanonicalJson.CanonicaliseWithoutSignature(link);
        byte[] b = CanonicalJson.CanonicaliseWithoutSignature(link with { Cid = "c" }); // same data

        Assert.That(a, Is.EqualTo(b));
        Assert.That(Encoding.UTF8.GetString(a), Does.Not.Contain("signature"));
    }
}