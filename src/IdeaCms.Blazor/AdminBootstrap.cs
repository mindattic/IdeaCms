using Microsoft.Extensions.Configuration;
using MindAttic.Authentication;
using MindAttic.Authentication.Secrets;
using MindAttic.Authentication.Services;

namespace IdeaCms.Blazor;

/// <summary>
/// First-run admin. By default this is MindAttic.Authentication's own bootstrap: <c>admin</c> from the
/// Vault <c>Security:bootstrap-token</c>, forced to change it at first sign-in.
/// <para>
/// <c>MindAttic:Auth:Bootstrap:RequirePasswordChange=false</c> creates that same admin WITHOUT the forced
/// change. That is for a deployment whose admin login is shared and rotated from outside (a public demo
/// whose operator replaces the token and the database together): the forced change would let the first
/// visitor set a password nobody else knows. Leave it unset everywhere else.
/// </para>
/// </summary>
public static class AdminBootstrap
{
    public const string AdminUserName = "admin";

    public static async Task ApplyAsync(
        IConfiguration configuration, IUserStore users, IUserAdminService admins, IAuthSecrets secrets,
        Func<CancellationToken, Task> defaultBootstrap, CancellationToken ct = default)
    {
        var requireChange = !bool.TryParse(configuration["MindAttic:Auth:Bootstrap:RequirePasswordChange"], out var v) || v;
        if (requireChange)
        {
            await defaultBootstrap(ct);
            return;
        }

        if (await users.AnyUsersAsync(ct)) return;
        var result = await admins.CreateAsync(AdminUserName, email: null, MaRoles.Admin,
            secrets.GetRequired("bootstrap-token"), mustChangePassword: false, ct: ct);
        if (!result.Ok)
            throw new InvalidOperationException($"Could not create the shared admin: {result.Error}");
    }
}
