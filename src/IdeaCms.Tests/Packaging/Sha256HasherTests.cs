using System.Text;
using IdeaCms.Packaging;

namespace IdeaCms.Tests.Packaging;

[TestFixture]
public class Sha256HasherTests
{
    [Test]
    public void Deterministic_AcrossTwoReadsOfIdenticalBytes()
    {
        var first = Sha256Hasher.OfBytes(Encoding.UTF8.GetBytes("the quick brown fox"));
        var second = Sha256Hasher.OfBytes(Encoding.UTF8.GetBytes("the quick brown fox"));
        Assert.That(first, Is.EqualTo(second));
    }

    [Test]
    public void Differs_OnAOneByteChange()
    {
        var a = Encoding.UTF8.GetBytes("payload-a");
        var b = Encoding.UTF8.GetBytes("payload-b");
        Assert.That(Sha256Hasher.OfBytes(a), Is.Not.EqualTo(Sha256Hasher.OfBytes(b)));
    }

    [Test]
    public void OfStream_MatchesOfBytes_ForSameContent()
    {
        var bytes = Encoding.UTF8.GetBytes("same content, two paths");
        using var ms = new MemoryStream(bytes);
        Assert.That(Sha256Hasher.OfStream(ms), Is.EqualTo(Sha256Hasher.OfBytes(bytes)));
    }

    [Test]
    public void Output_IsLowercaseHex()
    {
        var hash = Sha256Hasher.OfBytes(Encoding.UTF8.GetBytes("x"));
        Assert.That(hash, Has.Length.EqualTo(64));
        Assert.That(hash, Is.EqualTo(hash.ToLowerInvariant()));
    }
}
