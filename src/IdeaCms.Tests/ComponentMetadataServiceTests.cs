using Microsoft.EntityFrameworkCore;
using IdeaCms.Abstractions;
using IdeaCms.Core.Data;
using IdeaCms.Core.Entities;
using IdeaCms.Core.Services;

namespace IdeaCms.Tests;

/// <summary>
/// ComponentMetadataService — the IComponentMetadataStore host impl. Focus here is GetManyAsync, the batch
/// read an index component (ProjectGrid) uses so listing N child pages costs one query instead of N.
/// </summary>
[TestFixture]
public class ComponentMetadataServiceTests
{
    private sealed class InMemoryFactory(string dbName) : IDbContextFactory<CmsDbContext>
    {
        private readonly DbContextOptions<CmsDbContext> _opts =
            new DbContextOptionsBuilder<CmsDbContext>().UseInMemoryDatabase(dbName).Options;
        public CmsDbContext CreateDbContext() => new(_opts);
    }

    private static (ComponentMetadataService Service, Guid A, Guid B, Guid C) Seed()
    {
        var factory = new InMemoryFactory("meta_" + Guid.NewGuid().ToString("N"));
        using var db = factory.CreateDbContext();

        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.ComponentMetadata.AddRange(
            new ComponentMetadata { PageUid = a, ComponentKey = "repo", SlotName = "main", MetadataJson = """{"name":"A"}""", CreatedUtc = now, ModifiedUtc = now },
            new ComponentMetadata { PageUid = b, ComponentKey = "repo", SlotName = "main", MetadataJson = """{"name":"B"}""", CreatedUtc = now, ModifiedUtc = now },
            // Same page, different component — must not leak into a "repo" query.
            new ComponentMetadata { PageUid = a, ComponentKey = "frommd", SlotName = "main", MetadataJson = """{"markdown":"x"}""", CreatedUtc = now, ModifiedUtc = now },
            // Same page + component, different slot — must not leak into a "main" query.
            new ComponentMetadata { PageUid = b, ComponentKey = "repo", SlotName = "aside", MetadataJson = """{"name":"B-aside"}""", CreatedUtc = now, ModifiedUtc = now });
        db.SaveChanges();

        return (new ComponentMetadataService(factory), a, b, c);   // c has no rows at all
    }

    [Test]
    public async Task GetManyAsync_ReturnsOnlyTheRequestedComponentAndSlot()
    {
        var (svc, a, b, _) = Seed();

        var result = await svc.GetManyAsync([a, b], "repo");

        Assert.Multiple(() =>
        {
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[a], Does.Contain("\"A\""));
            Assert.That(result[b], Does.Contain("\"B\"").And.Not.Contains("aside"));
        });
    }

    [Test]
    public async Task GetManyAsync_OmitsPagesWithNoRow()
    {
        var (svc, a, _, c) = Seed();

        var result = await svc.GetManyAsync([a, c], "repo");

        // An index must still render a card for a page with no metadata, so absence is reported as
        // absence rather than an empty string the caller would have to disambiguate.
        Assert.Multiple(() =>
        {
            Assert.That(result.ContainsKey(a), Is.True);
            Assert.That(result.ContainsKey(c), Is.False);
        });
    }

    [Test]
    public async Task GetManyAsync_EmptyInput_ReturnsEmptyWithoutQuerying()
    {
        var (svc, _, _, _) = Seed();

        Assert.That(await svc.GetManyAsync([], "repo"), Is.Empty);
    }

    [Test]
    public async Task GetManyAsync_DeduplicatesRepeatedIds()
    {
        var (svc, a, _, _) = Seed();

        var result = await svc.GetManyAsync([a, a, a], "repo");

        Assert.That(result, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task GetManyAsync_AgreesWithGetAsync()
    {
        var (svc, a, b, c) = Seed();

        var batch = await svc.GetManyAsync([a, b, c], "repo");

        // The batch override must stay interchangeable with the interface's default per-id implementation.
        foreach (var uid in new[] { a, b, c })
            Assert.That(batch.GetValueOrDefault(uid), Is.EqualTo(await svc.GetAsync(uid, "repo")), $"mismatch for {uid}");
    }
}
