using System.Security.Claims;
using MindAttic.Ideas.Abstractions;
using IdeaCms.Core.Entities;

namespace IdeaCms.Core.Services;

/// <summary>
/// The single source of WRITE-time trust truth (mirroring <c>IRawContentGate</c> as the read-time truth):
/// a page is stamped <see cref="ContentTrust.Author"/> iff the writer holds the
/// <see cref="CmsClaims.AuthorRawMarkup"/> claim, else <see cref="ContentTrust.Untrusted"/>. Pure + DB-free
/// so the security decision is trivially unit-testable. Trust is decided at the moment of authorship and
/// never re-evaluated against the viewer (FOUNDATION trust invariant).
/// </summary>
public static class PageAuthoring
{
    public static ContentTrust StampTrust(bool holdsAuthorClaim) =>
        holdsAuthorClaim ? ContentTrust.Author : ContentTrust.Untrusted;

    /// <summary>Trust + author id from the writer's principal. AuthoredByUserId is the ma:uid (AuthUser.Id) claim, capped at 64.</summary>
    public static (ContentTrust Trust, string? AuthoredByUserId) Stamp(ClaimsPrincipal principal)
    {
        var holds = principal.HasClaim(CmsClaims.AuthorRawMarkup, "true");
        var uid = principal.FindFirst("ma:uid")?.Value
                  ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (uid is { Length: > 64 }) uid = uid[..64];
        return (StampTrust(holds), uid);
    }
}
