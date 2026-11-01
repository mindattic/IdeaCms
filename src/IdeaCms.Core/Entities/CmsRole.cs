namespace IdeaCms.Core.Entities;

/// <summary>CMS-defined role used for page access control. Separate from the auth library's single-role model.</summary>
public sealed class CmsRole
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Assigns a CMS role to a user. Many-to-many: one user may hold many CMS roles.</summary>
public sealed class CmsUserRole
{
    public string UserId { get; set; } = "";   // nvarchar(64) = Guid string from ma:uid claim
    public int RoleId { get; set; }
}

/// <summary>Grants a named role access to a restricted page. RoleName may be an auth role ("Admin") or a CMS role.</summary>
public sealed class PageRoleAccess
{
    public int PageId { get; set; }
    public string RoleName { get; set; } = "";
}

/// <summary>Grants a specific user access to a restricted page, without requiring a shared role.</summary>
public sealed class PageUserAccess
{
    public int PageId { get; set; }
    public string UserId { get; set; } = "";   // nvarchar(64) = Guid string from ma:uid claim
}
