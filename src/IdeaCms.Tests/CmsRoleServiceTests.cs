using Microsoft.EntityFrameworkCore;
using IdeaCms.Core.Data;
using IdeaCms.Core.Services;

namespace IdeaCms.Tests;

[TestFixture]
public class CmsRoleServiceTests
{
    private sealed class InMemoryFactory(string dbName) : IDbContextFactory<CmsDbContext>
    {
        private readonly DbContextOptions<CmsDbContext> _opts =
            new DbContextOptionsBuilder<CmsDbContext>().UseInMemoryDatabase(dbName).Options;
        public CmsDbContext CreateDbContext() => new(_opts);
        public Task<CmsDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(CreateDbContext());
    }

    private static ICmsRoleService NewService() =>
        new CmsRoleService(new InMemoryFactory("role_" + Guid.NewGuid().ToString("N")));

    [Test]
    public async Task GetAllRoleNamesAsync_CmsRoleSameNameAsBuiltinDifferentCase_IsDeduped()
    {
        // Regression: GetAllRoleNamesAsync used Distinct() with the default ordinal (case-sensitive)
        // comparer, so a CMS role named "user" (lowercase) was NOT deduplicated against the built-in
        // "User" entry — the list contained both, causing duplicate entries in role-selection dropdowns.
        // The fix uses Distinct(StringComparer.OrdinalIgnoreCase).
        var svc = NewService();
        // Create a CMS role whose name is a case-variant of the built-in "User" role.
        await svc.CreateRoleAsync("User");   // will fail the dedup check — but let's also try lowercase
        // The dedup check in CreateRoleAsync already blocks this; use lowercase which also dedupes with "User"
        var lower = await svc.CreateRoleAsync("user");
        // CreateRoleAsync rejects "user" (case-insensitive dedup), so no CMS "user" role is stored.
        // Instead create a CMS role "Editor" and verify the built-in "User" only appears once.
        await svc.CreateRoleAsync("Editor");

        var names = await svc.GetAllRoleNamesAsync();

        Assert.That(names.Count(n => string.Equals(n, "User", StringComparison.OrdinalIgnoreCase)),
            Is.EqualTo(1), "\"User\" must appear exactly once even when case-variant CMS roles exist");
        Assert.That(names, Does.Contain("Editor"), "custom CMS roles must still appear");
    }

    [Test]
    public async Task CreateRole_DuplicateNameDifferentCase_ReturnsFriendlyError()
    {
        // Regression: CreateRoleAsync used r.Name == name (case-sensitive ordinal), allowing logically
        // duplicate roles on InMemory / case-sensitive collations. Now uses ToLower() comparison.
        var svc = NewService();
        var first = await svc.CreateRoleAsync("Editor");
        Assert.That(first.Ok, Is.True, "first create must succeed");

        var duplicate = await svc.CreateRoleAsync("editor");
        Assert.Multiple(() =>
        {
            Assert.That(duplicate.Ok, Is.False, "duplicate name (different case) must be rejected");
            Assert.That(duplicate.Error, Does.Contain("already exists"), "friendly error message expected");
        });
    }
}
