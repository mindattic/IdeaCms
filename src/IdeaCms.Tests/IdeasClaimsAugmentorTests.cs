using System.Security.Claims;
using Microsoft.Extensions.Options;
using MindAttic.Authentication;
using MindAttic.Authentication.Options;
using IdeaCms.Core.Entities;
using IdeaCms.Blazor.Services;

namespace IdeaCms.Tests;

/// <summary>
/// The Cms.AuthorRawMarkup claim must ride the MindAttic.Authentication principal: an Admin gets it at
/// sign-in; non-admins never do; and when MFA is required it is withheld until amr=mfa is present.
/// </summary>
[TestFixture]
public class IdeaCmsClaimsAugmentorTests
{
    // The augmentor never resolves from the provider; a non-disposable stub keeps the analyzer quiet.
    private sealed class NullServiceProvider : IServiceProvider { public object? GetService(Type t) => null; }
    private static readonly IServiceProvider EmptyServices = new NullServiceProvider();

    private static ClaimsIdentity Identity(params Claim[] claims) =>
        new(claims, authenticationType: "test", nameType: ClaimTypes.Name, roleType: ClaimTypes.Role);

    private static IdeaCmsClaimsAugmentor Augmentor(bool requireMfa) =>
        new(Options.Create(new MfaOptions { RequireForAdmin = requireMfa }));

    private static bool HasRawMarkup(ClaimsIdentity id) => id.HasClaim(CmsClaims.AuthorRawMarkup, "true");

    [Test]
    public async Task Admin_WithMfaOff_GetsAuthorRawMarkup()
    {
        var id = Identity(new Claim(ClaimTypes.Role, MaRoles.Admin));
        await Augmentor(requireMfa: false).AugmentAsync(id, EmptyServices, default);
        Assert.That(HasRawMarkup(id), Is.True);
    }

    [Test]
    public async Task NonAdmin_NeverGetsAuthorRawMarkup()
    {
        var id = Identity(new Claim(ClaimTypes.Role, "Member"));
        await Augmentor(requireMfa: false).AugmentAsync(id, EmptyServices, default);
        Assert.That(HasRawMarkup(id), Is.False);
    }

    [Test]
    public async Task Admin_WithMfaRequiredButNotSatisfied_IsWithheld()
    {
        var id = Identity(new Claim(ClaimTypes.Role, MaRoles.Admin));   // no amr=mfa
        await Augmentor(requireMfa: true).AugmentAsync(id, EmptyServices, default);
        Assert.That(HasRawMarkup(id), Is.False);
    }

    [Test]
    public async Task Admin_WithMfaRequiredAndSatisfied_GetsAuthorRawMarkup()
    {
        var id = Identity(new Claim(ClaimTypes.Role, MaRoles.Admin), new Claim(MaClaims.Amr, "mfa"));
        await Augmentor(requireMfa: true).AugmentAsync(id, EmptyServices, default);
        Assert.That(HasRawMarkup(id), Is.True);
    }

    [Test]
    public async Task IsIdempotent_WhenClaimAlreadyPresent()
    {
        var id = Identity(new Claim(ClaimTypes.Role, MaRoles.Admin),
                          new Claim(CmsClaims.AuthorRawMarkup, "true"));
        await Augmentor(requireMfa: false).AugmentAsync(id, EmptyServices, default);
        Assert.That(id.FindAll(CmsClaims.AuthorRawMarkup).Count(), Is.EqualTo(1));
    }
}
