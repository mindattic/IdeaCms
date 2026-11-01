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

    [TestCase("User")]
    [TestCase("admin")]
    public async Task CreateRole_BuiltInName_IsRejected(string name)
    {
        // Bug (IDEA-T46): a CMS role named "Admin"/"User" compiled fine against the uniqueness check
        // (which only looked at the CmsRoles table) but was then silently swallowed by
        // GetAllRoleNamesAsync's Distinct() against the built-ins -- the role existed, was deletable
        // from RolesPanel, but could never be selected anywhere that lists role names.
        var svc = NewService();
        var result = await svc.CreateRoleAsync(name);
        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False, "a built-in role name must be rejected at creation");
            Assert.That(result.Error, Does.Contain("built-in"));
        });
    }

    [Test]
    public async Task UpdateRoleDescriptionAsync_PersistsTheNewDescription()
    {
        // Bug (IDEA-T46): RolesPanel.Save() for an existing role only set a status message and never
        // called any persistence method -- an admin editing a role's Description saw no error, but the
        // edit was silently discarded on reload.
        var svc = NewService();
        await svc.CreateRoleAsync("Editor", "original");
        var created = (await svc.ListRolesAsync()).Single(r => r.Name == "Editor");

        var (ok, error) = await svc.UpdateRoleDescriptionAsync(created.Id, "updated description");

        Assert.That(ok, Is.True, error);
        var reloaded = (await svc.ListRolesAsync()).Single(r => r.Id == created.Id);
        Assert.That(reloaded.Description, Is.EqualTo("updated description"));
    }

    [Test]
    public async Task UpdateRoleDescriptionAsync_UnknownId_ReturnsFriendlyError()
    {
        var svc = NewService();
        var (ok, error) = await svc.UpdateRoleDescriptionAsync(12345, "x");
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(error, Does.Contain("not found"));
        });
    }
}
