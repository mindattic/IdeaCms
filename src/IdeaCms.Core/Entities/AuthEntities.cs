namespace IdeaCms.Core.Entities;

// Identity is MindAttic.Authentication's (M.IDEA-LAW-9): it lives in the library's `auth`-schema AuthUser
// table (CmsDbContext implements IAuthDataContext). UserRoles + CmsClaims stay — they're
// the IdeaCms-owned trust vocabulary the raw-content gate and claims augmentor key off of.

/// <summary>Role constants. Admin holds the <c>Cms.AuthorRawMarkup</c> claim (trusted inline JS/HTML).</summary>
public static class UserRoles
{
    public const string Admin = "Admin";
    public static readonly IReadOnlyList<string> All = new[] { Admin };
}

/// <summary>Claim types the CMS issues/checks.</summary>
public static class CmsClaims
{
    /// <summary>Holder may author raw, unsanitized HTML/JS in a page (Author trust). Granted to Admin.</summary>
    public const string AuthorRawMarkup = "Cms.AuthorRawMarkup";
}
