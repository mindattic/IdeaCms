using Microsoft.EntityFrameworkCore;
using IdeaCms.Core.Data;
using IdeaCms.Core.Entities;

namespace IdeaCms.Tests;

/// <summary>
/// Offline model inspection (no connection opened): the AdminInboxMessage dedup index is the runtime
/// backstop behind AdminInboxService's lookup-before-insert, so it must stay in the model.
/// </summary>
[TestFixture]
public class CmsModelGuardTests
{
    private static CmsDbContext BuildModelOnly() =>
        new(new DbContextOptionsBuilder<CmsDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);

    [Test]
    public void AdminInbox_HasUniqueDedupKeyIndex()
    {
        using var db = BuildModelOnly();
        var et = db.Model.FindEntityType(typeof(AdminInboxMessage));
        Assert.That(et, Is.Not.Null);
        var hasUnique = et!.GetIndexes().Any(i => i.IsUnique && i.Properties.Count == 1 && i.Properties[0].Name == nameof(AdminInboxMessage.DedupKey));
        Assert.That(hasUnique, Is.True, "AdminInboxMessage must keep a unique index on DedupKey (the dedup backstop).");
    }
}
