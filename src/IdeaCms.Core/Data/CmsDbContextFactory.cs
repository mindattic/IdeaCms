using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IdeaCms.Core.Data;

/// <summary>
/// Design-time factory for <c>dotnet ef</c> (migrations). Uses <c>ConnectionStrings__IdeaCms</c> when set,
/// else the shared development database; the runtime connection is supplied via DI/Vault.
/// </summary>
public sealed class CmsDbContextFactory : IDesignTimeDbContextFactory<CmsDbContext>
{
    /// <summary>
    /// The development database: <c>IdeaCmsDev</c> on the MindAttic Azure SQL server, opened with the
    /// developer's own <c>az login</c> (no secret in the string, no local SQL Server). Used only when no
    /// <c>ConnectionStrings:IdeaCms</c> is configured; the deployed sites always set their own.
    /// </summary>
    public const string DevelopmentConnectionString =
        "Server=tcp:sql-mindattic-f65744.database.windows.net,1433;Database=IdeaCmsDev;" +
        "Authentication=Active Directory Default;Encrypt=True;Connect Timeout=60;MultipleActiveResultSets=True;";

    public CmsDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("ConnectionStrings__IdeaCms")
                   ?? DevelopmentConnectionString;
        var options = new DbContextOptionsBuilder<CmsDbContext>()
            .UseSqlServer(conn)
            .Options;
        return new CmsDbContext(options);
    }
}
