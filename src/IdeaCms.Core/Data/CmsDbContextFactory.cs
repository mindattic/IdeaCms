using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IdeaCms.Core.Data;

/// <summary>
/// Design-time factory for <c>dotnet ef</c> (migrations). Uses a placeholder connection string;
/// the runtime connection is supplied via DI/Vault. Mirrors the MindAttic.Frontpage pattern.
/// </summary>
public sealed class CmsDbContextFactory : IDesignTimeDbContextFactory<CmsDbContext>
{
    public CmsDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("ConnectionStrings__IdeaCms")
                   ?? "Server=(localdb)\\MSSQLLocalDB;Database=IdeaCms;Trusted_Connection=True;TrustServerCertificate=True;";
        var options = new DbContextOptionsBuilder<CmsDbContext>()
            .UseSqlServer(conn)
            .Options;
        return new CmsDbContext(options);
    }
}
