using System.Security.Claims;
using Microsoft.Extensions.Options;
using MindAttic.Authentication;
using MindAttic.Authentication.Options;
using MindAttic.Authentication.Web;
using IdeaCms.Core.Entities;

namespace IdeaCms.Blazor.Services;

/// <summary>
/// Bakes the IdeaCms-owned <see cref="CmsClaims.AuthorRawMarkup"/> claim into the auth cookie at sign-in,
/// so MindAttic.Authentication issues a principal the CMS raw-content gate already understands — only the
/// issuer of the ticket changed, not the trust vocabulary (M.IDEA-LAW-9). The library runs
/// every registered augmentor once, just before SignInAsync, over the freshly-built identity; because
/// claims are NOT rebuilt on revalidation, this MUST be deterministic from the identity's existing claims.
/// An Admin gets the claim; with MFA on it is withheld until amr=mfa is present (the final cookie sign-in
/// happens after MFA confirmation, so the gate is satisfied at the right moment).
/// </summary>
public sealed class IdeaCmsClaimsAugmentor(IOptions<MfaOptions> mfa) : IMaClaimsAugmentor
{
    public ValueTask AugmentAsync(ClaimsIdentity identity, IServiceProvider services, CancellationToken ct)
    {
        var isAdmin = identity.HasClaim(ClaimTypes.Role, MaRoles.Admin)
                      || identity.HasClaim(identity.RoleClaimType, MaRoles.Admin);
        var mfaSatisfied = !mfa.Value.RequireForAdmin || identity.HasClaim(MaClaims.Amr, "mfa");

        if (isAdmin && mfaSatisfied && !identity.HasClaim(CmsClaims.AuthorRawMarkup, "true"))
            identity.AddClaim(new Claim(CmsClaims.AuthorRawMarkup, "true"));

        return ValueTask.CompletedTask;
    }
}
