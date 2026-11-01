using Microsoft.Extensions.Configuration;
using MindAttic.Authentication;
using MindAttic.Authentication.Entities;
using MindAttic.Authentication.Secrets;
using MindAttic.Authentication.Services;
using IdeaCms.Blazor;

namespace IdeaCms.Tests;

[TestFixture]
public class AdminBootstrapTests
{
    private sealed class FakeUsers(bool any) : IUserStore
    {
        public Task<bool> AnyUsersAsync(CancellationToken ct = default) => Task.FromResult(any);
        public Task<AuthUser?> FindByUserNameAsync(string userName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthUser?> FindByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthUserMfa?> FindMfaAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public void ApplyRehash(AuthUser user, string phc, string pepperKeyId) => throw new NotSupportedException();
        public void RecordLogin(AuthUser user, DateTime utcNow) => throw new NotSupportedException();
    }

    private sealed class FakeAdmins : IUserAdminService
    {
        public List<(string User, string Role, string Password, bool MustChange)> Created { get; } = [];
        public Task<CreateUserResult> CreateAsync(string userName, string? email, string role, string password,
            bool mustChangePassword = true, string? displayName = null, CancellationToken ct = default)
        {
            Created.Add((userName, role, password, mustChangePassword));
            return Task.FromResult(new CreateUserResult(true, Guid.NewGuid(), null));
        }
        public Task<IReadOnlyList<AuthUserSummary>> ListAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthUserSummary?> GetAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CountAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AdminActionResult> SetRoleAsync(Guid id, string role, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AdminActionResult> SetActiveAsync(Guid id, bool active, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AdminActionResult> UpdateProfileAsync(Guid id, string? email, string? displayName = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AdminActionResult> ResetPasswordAsync(Guid id, string newPassword, bool requireChange = true, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeSecrets : IAuthSecrets
    {
        public string GetRequired(string name) => name == "bootstrap-token" ? "the-hourly-password" : throw new InvalidOperationException(name);
        public string? GetOptional(string name) => null;
        public byte[] GetRequiredBytes(string name) => throw new NotSupportedException();
    }

    private static IConfiguration Config(string? requireChange) =>
        new ConfigurationBuilder().AddInMemoryCollection(requireChange is null
            ? []
            : new Dictionary<string, string?> { ["MindAttic:Auth:Bootstrap:RequirePasswordChange"] = requireChange }).Build();

    [TestCase(null)]
    [TestCase("true")]
    public async Task ByDefault_UsesTheLibraryBootstrap(string? setting)
    {
        var admins = new FakeAdmins();
        var defaultRan = false;
        await AdminBootstrap.ApplyAsync(Config(setting), new FakeUsers(false), admins, new FakeSecrets(),
            _ => { defaultRan = true; return Task.CompletedTask; });
        Assert.Multiple(() =>
        {
            Assert.That(defaultRan, Is.True);
            Assert.That(admins.Created, Is.Empty);
        });
    }

    [Test]
    public async Task SharedLogin_CreatesTheAdminWithoutAForcedChange()
    {
        var admins = new FakeAdmins();
        var defaultRan = false;
        await AdminBootstrap.ApplyAsync(Config("false"), new FakeUsers(false), admins, new FakeSecrets(),
            _ => { defaultRan = true; return Task.CompletedTask; });
        Assert.Multiple(() =>
        {
            Assert.That(defaultRan, Is.False, "the forced-change bootstrap must not run for a shared login");
            Assert.That(admins.Created, Is.EqualTo(new[] { ("admin", MaRoles.Admin, "the-hourly-password", false) }));
        });
    }

    [Test]
    public async Task SharedLogin_IsANoOpOnceAnyUserExists()
    {
        var admins = new FakeAdmins();
        await AdminBootstrap.ApplyAsync(Config("false"), new FakeUsers(true), admins, new FakeSecrets(), _ => Task.CompletedTask);
        Assert.That(admins.Created, Is.Empty);
    }
}
