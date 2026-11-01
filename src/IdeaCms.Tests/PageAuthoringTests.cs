using System.Security.Claims;
using IdeaCms.Abstractions;
using IdeaCms.Core.Entities;
using IdeaCms.Core.Services;

namespace IdeaCms.Tests;

/// <summary>Write-time trust stamping: Author iff the Cms.AuthorRawMarkup claim is present, else Untrusted.</summary>
[TestFixture]
public class PageAuthoringTests
{
    [Test]
    public void StampTrust_FollowsTheClaim()
    {
        Assert.That(PageAuthoring.StampTrust(true), Is.EqualTo(ContentTrust.Author));
        Assert.That(PageAuthoring.StampTrust(false), Is.EqualTo(ContentTrust.Untrusted));
    }

    [Test]
    public void Stamp_WithClaim_IsAuthor_AndCapturesUid()
    {
        var p = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(CmsClaims.AuthorRawMarkup, "true"), new Claim("ma:uid", "user-123") }, "test"));
        var (trust, uid) = PageAuthoring.Stamp(p);
        Assert.That(trust, Is.EqualTo(ContentTrust.Author));
        Assert.That(uid, Is.EqualTo("user-123"));
    }

    [Test]
    public void Stamp_WithoutClaim_IsUntrusted()
    {
        var p = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("ma:uid", "u2") }, "test"));
        Assert.That(PageAuthoring.Stamp(p).Trust, Is.EqualTo(ContentTrust.Untrusted));
    }

    [Test]
    public void Stamp_TruncatesUidTo64()
    {
        var longUid = new string('a', 100);
        var p = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("ma:uid", longUid) }, "test"));
        Assert.That(PageAuthoring.Stamp(p).AuthoredByUserId, Has.Length.EqualTo(64));
    }
}
