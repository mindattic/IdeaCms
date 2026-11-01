using Microsoft.EntityFrameworkCore;
using MindAttic.Authentication;
using IdeaCms.Core.Data;
using IdeaCms.Core.Entities;

namespace IdeaCms.Core.Services;

public sealed record CmsRoleSummary(int Id, string Name, string? Description);

public interface ICmsRoleService
{
    Task<IReadOnlyList<CmsRoleSummary>> ListRolesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetAllRoleNamesAsync(CancellationToken ct = default);
    Task<(bool Ok, string? Error)> CreateRoleAsync(string name, string? description = null, CancellationToken ct = default);
    Task<(bool Ok, string? Error)> UpdateRoleDescriptionAsync(int id, string? description, CancellationToken ct = default);
    Task<bool> DeleteRoleAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<int>> GetUserCmsRoleIdsAsync(string userId, CancellationToken ct = default);
    Task SetUserCmsRolesAsync(string userId, IEnumerable<int> roleIds, CancellationToken ct = default);
}

public sealed class CmsRoleService(IDbContextFactory<CmsDbContext> dbFactory) : ICmsRoleService
{
    public async Task<IReadOnlyList<CmsRoleSummary>> ListRolesAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.CmsRoles.AsNoTracking().OrderBy(r => r.Name)
            .Select(r => new CmsRoleSummary(r.Id, r.Name, r.Description)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetAllRoleNamesAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var cmsNames = await db.CmsRoles.AsNoTracking().OrderBy(r => r.Name).Select(r => r.Name).ToListAsync(ct);
        // Built-in auth roles first, then CMS-defined roles.
        return new[] { "User", MaRoles.Admin }.Concat(cmsNames).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static readonly string[] BuiltInRoleNames = ["User", MaRoles.Admin];

    public async Task<(bool Ok, string? Error)> CreateRoleAsync(string name, string? description = null, CancellationToken ct = default)
    {
        name = name.Trim();
        if (string.IsNullOrEmpty(name)) return (false, "Name is required.");
        if (BuiltInRoleNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            return (false, $"'{name}' is a built-in role name and can't be reused.");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.CmsRoles.AnyAsync(r => r.Name.ToLowerInvariant() == name.ToLowerInvariant(), ct))
            return (false, $"Role '{name}' already exists.");
        db.CmsRoles.Add(new CmsRole { Name = name, Description = description?.Trim(), CreatedUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> UpdateRoleDescriptionAsync(int id, string? description, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var role = await db.CmsRoles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null) return (false, "Role not found.");
        role.Description = description?.Trim();
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<bool> DeleteRoleAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var role = await db.CmsRoles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null) return false;
        // Remove all user assignments and page access rows first.
        var userRoles = await db.CmsUserRoles.Where(ur => ur.RoleId == id).ToListAsync(ct);
        db.CmsUserRoles.RemoveRange(userRoles);
        var pageRoles = await db.PageRoleAccess.Where(pr => pr.RoleName == role.Name).ToListAsync(ct);
        db.PageRoleAccess.RemoveRange(pageRoles);
        db.CmsRoles.Remove(role);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<int>> GetUserCmsRoleIdsAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.CmsUserRoles.Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).ToListAsync(ct);
    }

    public async Task SetUserCmsRolesAsync(string userId, IEnumerable<int> roleIds, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.CmsUserRoles.Where(ur => ur.UserId == userId).ToListAsync(ct);
        db.CmsUserRoles.RemoveRange(existing);
        foreach (var rid in roleIds.Distinct())
            db.CmsUserRoles.Add(new CmsUserRole { UserId = userId, RoleId = rid });
        await db.SaveChangesAsync(ct);
    }
}
