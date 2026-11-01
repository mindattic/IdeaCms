using Microsoft.EntityFrameworkCore;
using MindAttic.Authentication.Entities;
using IdeaCms.Core.Data;

namespace IdeaCms.Tests;

/// <summary>
/// CmsDbContext is the MindAttic.Authentication data seam (IAuthDataContext): the 8 identity tables must
/// be mapped into the isolated 'auth' schema, and the retired interim dbo.Users table must be gone.
/// Model inspection is offline — no database connection is opened.
/// </summary>
[TestFixture]
public class CmsDbContextAuthModelTests
{
    private static CmsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CmsDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new CmsDbContext(options);
    }

    [Test]
    public void AllEightAuthEntities_AreMappedToTheAuthSchema()
    {
        using var db = BuildContext();
        Type[] authTypes =
        [
            typeof(AuthUser), typeof(AuthUserMfa), typeof(AuthRecoveryCode), typeof(AuthSession),
            typeof(AuthLoginThrottle), typeof(AuthAuditLog), typeof(AuthPasswordHistory), typeof(AuthPasswordResetToken),
        ];

        foreach (var t in authTypes)
        {
            var et = db.Model.FindEntityType(t);
            Assert.That(et, Is.Not.Null, $"{t.Name} is not in the model.");
            Assert.That(et!.GetSchema(), Is.EqualTo("auth"), $"{t.Name} is not in the 'auth' schema.");
        }
    }

    [Test]
    public void RetiredUsersTable_IsNotInTheModel()
    {
        using var db = BuildContext();
        var hasUsersTable = db.Model.GetEntityTypes().Any(e => e.GetTableName() == "Users");
        Assert.That(hasUsersTable, Is.False, "The interim dbo.Users table should be retired.");
    }

    [Test]
    public void PagesTable_RemainsMapped()
    {
        using var db = BuildContext();
        var hasPages = db.Model.GetEntityTypes().Any(e => e.GetTableName() == "Pages");
        Assert.That(hasPages, Is.True, "The CMS Pages table must remain.");
    }
}
