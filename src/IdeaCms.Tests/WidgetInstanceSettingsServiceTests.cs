using Microsoft.EntityFrameworkCore;
using IdeaCms.Core.Data;
using IdeaCms.Core.Entities;
using IdeaCms.Core.Services;
using CmsPage = IdeaCms.Core.Entities.Page;

namespace IdeaCms.Tests;

[TestFixture]
public class WidgetInstanceSettingsServiceTests
{
    private sealed class InMemoryFactory(string dbName) : IDbContextFactory<CmsDbContext>
    {
        private readonly DbContextOptions<CmsDbContext> _opts =
            new DbContextOptionsBuilder<CmsDbContext>().UseInMemoryDatabase(dbName).Options;
        public CmsDbContext CreateDbContext() => new(_opts);
        public Task<CmsDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(CreateDbContext());
    }

    private static IDbContextFactory<CmsDbContext> NewFactory() =>
        new InMemoryFactory("wis_" + Guid.NewGuid().ToString("N"));

    private static async Task<int> SeedPageAsync(IDbContextFactory<CmsDbContext> factory)
    {
        await using var db = factory.CreateDbContext();
        var page = new CmsPage
        {
            Slug = "hero-page", Title = "Hero",
            Kind = MindAttic.Ideas.Abstractions.PageKind.Data,
            BodyTrust = MindAttic.Ideas.Abstractions.ContentTrust.Untrusted,
            Enabled = true, CreatedUtc = DateTime.UtcNow,
        };
        db.Pages.Add(page);
        await db.SaveChangesAsync();
        return page.Id;
    }

    [Test]
    public async Task Save_Create_PersistsVersionOne()
    {
        var factory = NewFactory();
        var svc = new WidgetInstanceSettingsService(factory);
        var pageId = await SeedPageAsync(factory);

        var result = await svc.SaveAsync(pageId, "hero", "Plugin.hero@1", """{"color":"blue"}""", "admin");

        Assert.Multiple(() =>
        {
            Assert.That(result.SettingsVersion, Is.EqualTo(1));
            Assert.That(result.SettingsJson, Does.Contain("blue"));
            Assert.That(result.SlotName, Is.EqualTo("hero"));
        });
    }

    [Test]
    public async Task Save_Update_BumpsVersionAndWritesHistory()
    {
        var factory = NewFactory();
        var svc = new WidgetInstanceSettingsService(factory);
        var pageId = await SeedPageAsync(factory);

        await svc.SaveAsync(pageId, "hero", "Plugin.hero@1", """{"color":"blue"}""");
        var v2 = await svc.SaveAsync(pageId, "hero", "Plugin.hero@1", """{"color":"red"}""", "editor");

        Assert.That(v2.SettingsVersion, Is.EqualTo(2));

        var history = await svc.GetHistoryAsync(pageId, "hero");
        Assert.That(history, Has.Count.EqualTo(1), "one history snapshot (the V1 pre-save state)");
        Assert.That(history[0].SettingsVersion, Is.EqualTo(1));
        Assert.That(history[0].SettingsJson, Does.Contain("blue"));
    }

    [Test]
    public async Task Save_MultipleUpdates_AccumulatesHistory()
    {
        var factory = NewFactory();
        var svc = new WidgetInstanceSettingsService(factory);
        var pageId = await SeedPageAsync(factory);

        await svc.SaveAsync(pageId, "hero", "Plugin.hero@1", """{"v":1}""");
        await svc.SaveAsync(pageId, "hero", "Plugin.hero@1", """{"v":2}""");
        await svc.SaveAsync(pageId, "hero", "Plugin.hero@1", """{"v":3}""");

        var current = await svc.GetAsync(pageId, "hero");
        var history = await svc.GetHistoryAsync(pageId, "hero");

        Assert.Multiple(() =>
        {
            Assert.That(current!.SettingsVersion, Is.EqualTo(3));
            Assert.That(history, Has.Count.EqualTo(2), "two snapshots for V1 and V2");
        });
    }

    [Test]
    public async Task Rollback_RestoresPreviousSettingsAndBumpsVersion()
    {
        var factory = NewFactory();
        var svc = new WidgetInstanceSettingsService(factory);
        var pageId = await SeedPageAsync(factory);

        await svc.SaveAsync(pageId, "hero", "Plugin.hero@1", """{"color":"blue"}""");   // V1
        await svc.SaveAsync(pageId, "hero", "Plugin.hero@1", """{"color":"red"}""");    // V2

        var ok = await svc.RollbackAsync(pageId, "hero", version: 1);

        var current = await svc.GetAsync(pageId, "hero");
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(current!.SettingsJson, Does.Contain("blue"), "rolled back to V1 content");
            Assert.That(current.SettingsVersion, Is.EqualTo(3), "version always increases");
        });
    }

    [Test]
    public async Task Rollback_UnknownVersion_ReturnsFalse()
    {
        var factory = NewFactory();
        var svc = new WidgetInstanceSettingsService(factory);
        var pageId = await SeedPageAsync(factory);
        await svc.SaveAsync(pageId, "hero", "Plugin.hero@1", "{}");

        var ok = await svc.RollbackAsync(pageId, "hero", version: 99);

        Assert.That(ok, Is.False);
    }

    [Test]
    public async Task GetAsync_UnknownSlot_ReturnsNull()
    {
        var factory = NewFactory();
        var svc = new WidgetInstanceSettingsService(factory);
        var pageId = await SeedPageAsync(factory);

        var result = await svc.GetAsync(pageId, "nonexistent");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetHistoryAsync_UnknownSlot_ReturnsEmpty()
    {
        var factory = NewFactory();
        var svc = new WidgetInstanceSettingsService(factory);
        var pageId = await SeedPageAsync(factory);

        var history = await svc.GetHistoryAsync(pageId, "ghost");

        Assert.That(history, Is.Empty);
    }

    [Test]
    public async Task Rollback_RestoresWidgetRef()
    {
        // Regression: rollback only restored SettingsJson, leaving the old WidgetRef in place.
        var factory = NewFactory();
        var svc = new WidgetInstanceSettingsService(factory);
        var pageId = await SeedPageAsync(factory);

        await svc.SaveAsync(pageId, "hero", "Component.hero@1", """{"color":"blue"}""");   // V1 — hero@1
        await svc.SaveAsync(pageId, "hero", "Component.hero@2", """{"color":"red"}""");    // V2 — upgraded to hero@2

        var ok = await svc.RollbackAsync(pageId, "hero", version: 1);

        var current = await svc.GetAsync(pageId, "hero");
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(current!.WidgetRef, Is.EqualTo("Component.hero@1"), "WidgetRef must also roll back");
            Assert.That(current.SettingsJson, Does.Contain("blue"));
        });
    }

    [Test]
    public async Task Save_Update_HistoryCaptures_WidgetRef()
    {
        // Regression: history snapshots omitted WidgetRef, so rollback had nothing to restore.
        var factory = NewFactory();
        var svc = new WidgetInstanceSettingsService(factory);
        var pageId = await SeedPageAsync(factory);

        await svc.SaveAsync(pageId, "cta", "Plugin.cta@1", """{"text":"Sign up"}""");
        await svc.SaveAsync(pageId, "cta", "Plugin.cta@2", """{"text":"Get started"}""");

        var history = await svc.GetHistoryAsync(pageId, "cta");
        Assert.That(history[0].WidgetRef, Is.EqualTo("Plugin.cta@1"),
            "history snapshot must record the WidgetRef that was active before the save");
    }
}
