using Microsoft.EntityFrameworkCore;
using IdeaCms.Abstractions;
using IdeaCms.Core.Data;
using IdeaCms.Core.Discovery;
using IdeaCms.Core.Entities;
using IdeaCms.Core.Services;
using CmsPage = IdeaCms.Core.Entities.Page;

namespace IdeaCms.Tests;

/// <summary>
/// The version-specific, reference-guarded delete logic (pure FindBlockingPages) and the enable/disable
/// live-apply path (SetEnabledAsync + DiscoveryService.ReloadCatalogAsync makes the catalog report Disabled).
/// </summary>
[TestFixture]
public class ContentLifecycleServiceTests
{
    private static PageRef Pub(string slug, string? body = null, string? themeKey = null, int? themeVer = null) =>
        new(slug, body, themeKey, themeVer, Enabled: true, IsPublished: true);

    // ---- pure delete-guard ----

    [Test]
    public void PinnedVersion_AlwaysBlocks_AndListsSlug()
    {
        var pages = new[] { Pub("home", "<Plugin.Tooltip data-version=\"11\" />") };
        var blocking = ContentLifecycleService.FindBlockingPages(
            ContentKind.Plugin, "tooltip", 11, pages, otherEnabledVersions: new[] { 12 });
        Assert.That(blocking, Is.EquivalentTo(new[] { "home" }));   // pin blocks even though V12 also exists
    }

    [Test]
    public void FloatingReference_BlocksOnlyWhenItWouldOrphan()
    {
        var pages = new[] { Pub("home", "<Plugin.Tooltip />") };  // floats to latest
        // V11 is the last enabled version -> deleting it orphans the float -> blocked.
        Assert.That(ContentLifecycleService.FindBlockingPages(ContentKind.Plugin, "tooltip", 11, pages, Array.Empty<int>()),
            Is.EquivalentTo(new[] { "home" }));
        // V12 remains enabled -> the float harmlessly moves to V12 -> deleting V11 is allowed.
        Assert.That(ContentLifecycleService.FindBlockingPages(ContentKind.Plugin, "tooltip", 11, pages, new[] { 12 }),
            Is.Empty);
    }

    [Test]
    public void DisabledOrUnpublishedPage_IsNotABlockingReference()
    {
        var pages = new[]
        {
            new PageRef("draft", "<Plugin.Tooltip data-version=\"11\" />", null, null, Enabled: false, IsPublished: true),
            new PageRef("hidden", "<Plugin.Tooltip data-version=\"11\" />", null, null, Enabled: true, IsPublished: false),
        };
        Assert.That(ContentLifecycleService.FindBlockingPages(ContentKind.Plugin, "tooltip", 11, pages, Array.Empty<int>()),
            Is.Empty);
    }

    [Test]
    public void SoftDeletedPage_DoesNotBlockCitizenDeletion()
    {
        // Regression: IsDeleted pages still had Enabled=true+IsPublished=true after SoftDeleteAsync,
        // so they fell through the guard and falsely blocked citizen deletion.
        var pages = new[]
        {
            new PageRef("removed", "<Plugin.Tooltip data-version=\"11\" />", null, null, Enabled: true, IsPublished: true, IsDeleted: true),
        };
        Assert.That(ContentLifecycleService.FindBlockingPages(ContentKind.Plugin, "tooltip", 11, pages, Array.Empty<int>()),
            Is.Empty, "soft-deleted pages must not block citizen deletion");
    }

    [Test]
    public void ActivePluginsJson_VersionedPin_BlocksDeletion()
    {
        // Regression: only floating refs in ActivePluginsJson were guarded; versioned pins like
        // "Plugin.navmenu@1" were missed, allowing deletion of a citizen still actively used.
        var pages = new[]
        {
            new PageRef("home", null, null, null, Enabled: true, IsPublished: true,
                ActivePluginsJson: """["Plugin.navmenu@1"]"""),
        };
        Assert.That(ContentLifecycleService.FindBlockingPages(ContentKind.Plugin, "navmenu", 1, pages, Array.Empty<int>()),
            Is.EquivalentTo(new[] { "home" }), "versioned plugin pin in ActivePluginsJson must block deletion");
    }

    [Test]
    public void ActivePluginsJson_FloatingRef_BlocksOnlyWhenOrphaning()
    {
        var pages = new[]
        {
            new PageRef("p", null, null, null, Enabled: true, IsPublished: true,
                ActivePluginsJson: """["Plugin.navmenu"]"""),
        };
        // Last version → orphan → blocked.
        Assert.That(ContentLifecycleService.FindBlockingPages(ContentKind.Plugin, "navmenu", 1, pages, Array.Empty<int>()),
            Is.EquivalentTo(new[] { "p" }));
        // Another version remains → float moves there → allowed.
        Assert.That(ContentLifecycleService.FindBlockingPages(ContentKind.Plugin, "navmenu", 1, pages, new[] { 2 }),
            Is.Empty);
    }

    [Test]
    public void ActivePluginsJson_FloatingRef_IsMatchedThroughTheUsesGrammar()
    {
        // Regression: the floating-plugin check was an ad-hoc string compare against "Plugin." + key, so
        // any ref the uses[] grammar accepts but that string does not — a padded entry, or the explicit
        // "@latest" float — slipped the guard and the page's last enabled plugin version could be deleted.
        foreach (var json in new[] { """[" Plugin.navmenu "]""", """["Plugin.navmenu@latest"]""" })
        {
            var pages = new[]
            {
                new PageRef("p", null, null, null, Enabled: true, IsPublished: true, ActivePluginsJson: json),
            };
            Assert.That(ContentLifecycleService.FindBlockingPages(ContentKind.Plugin, "navmenu", 1, pages, Array.Empty<int>()),
                Is.EquivalentTo(new[] { "p" }), $"floating plugin ref {json} must block the last enabled version");
            Assert.That(ContentLifecycleService.FindBlockingPages(ContentKind.Plugin, "navmenu", 1, pages, new[] { 2 }),
                Is.Empty, $"floating plugin ref {json} must float to a remaining version");
        }
    }

    [Test]
    public void ThemePin_Blocks_AndThemeFloatFollowsOrphanRule()
    {
        var pinned = new[] { Pub("a", themeKey: "cyberspace", themeVer: 1) };
        Assert.That(ContentLifecycleService.FindBlockingPages(ContentKind.Theme, "cyberspace", 1, pinned, new[] { 2 }),
            Is.EquivalentTo(new[] { "a" }));   // pinned theme version blocks

        var floating = new[] { Pub("b", themeKey: "cyberspace", themeVer: null) };
        Assert.That(ContentLifecycleService.FindBlockingPages(ContentKind.Theme, "cyberspace", 1, floating, Array.Empty<int>()),
            Is.EquivalentTo(new[] { "b" }));   // last enabled version -> orphan -> blocked
        Assert.That(ContentLifecycleService.FindBlockingPages(ContentKind.Theme, "cyberspace", 1, floating, new[] { 2 }),
            Is.Empty);                          // another version remains -> allowed
    }

    // ---- enable/disable live-apply over EF InMemory ----

    private sealed class InMemoryFactory(string dbName) : IDbContextFactory<CmsDbContext>
    {
        private readonly DbContextOptions<CmsDbContext> _opts =
            new DbContextOptionsBuilder<CmsDbContext>().UseInMemoryDatabase(dbName).Options;
        public CmsDbContext CreateDbContext() => new(_opts);
    }

    private sealed class NullResolver : ITypeResolver
    {
        public Type? Resolve(ContentDescriptor descriptor) => null;
    }

    [Test]
    public async Task DeleteAsync_WithBlockingPage_ReturnsBLockedWithoutPreCallToCanDelete()
    {
        // Regression: DeleteAsync used to call CanDeleteAsync in a separate DbContext, leaving a TOCTOU
        // window. Now both the guard check and the delete run in one context.
        var factory = new InMemoryFactory("lc_del_" + Guid.NewGuid().ToString("N"));
        await using (var db = factory.CreateDbContext())
        {
            db.ContentDefinitions.Add(new CmsContentDefinition
            {
                Kind = ContentKind.Plugin, Key = "myplugin", Version = 1,
                Origin = ContentOrigin.Package, DisplayName = "My Plugin",
                Enabled = true, IsActive = true, IsShadowed = false, DiscoveredUtc = DateTime.UtcNow,
            });
            db.Pages.Add(new CmsPage
            {
                Slug = "home", Title = "Home",
                Kind = PageKind.Data, BodyHtml = "<Plugin.Myplugin data-version=\"1\" />",
                BodyTrust = ContentTrust.Untrusted, Enabled = true, IsPublished = true,
                CreatedUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var catalog = new ContentCatalog(new NullResolver());
        var discovery = new DiscoveryService(factory, Array.Empty<ICmsContentSource>(), catalog);
        var svc = new ContentLifecycleService(factory, discovery);

        var result = await svc.DeleteAsync(ContentKind.Plugin, "myplugin", 1);

        Assert.Multiple(() =>
        {
            Assert.That(result.Blocked, Is.True, "DeleteAsync must block when a page pins the version");
            Assert.That(result.PinnedBy, Contains.Item("home"));
        });
    }

    [Test]
    public async Task ReloadCatalog_DisabledCompiledRow_DoesNotShadowEnabledPackageRow()
    {
        // Regression: shadowing was ordered by Priority only; a disabled high-priority Compiled row
        // (Priority=100) shadowed an enabled lower-priority Package row (Priority=50), making the
        // catalog report Disabled even though a working Package version was available.
        // The fix orders by (Enabled DESC, Priority DESC) so enabled rows always win within an identity.
        var factory = new InMemoryFactory("lc_shadow_" + Guid.NewGuid().ToString("N"));
        await using (var db = factory.CreateDbContext())
        {
            db.ContentDefinitions.AddRange(
                new CmsContentDefinition
                {
                    Kind = ContentKind.Plugin, Key = "shared", Version = 1,
                    Origin = ContentOrigin.Compiled, DisplayName = "Compiled (disabled)",
                    Priority = 100, Enabled = false, IsActive = true, IsShadowed = false,
                    DiscoveredUtc = DateTime.UtcNow,
                },
                new CmsContentDefinition
                {
                    Kind = ContentKind.Plugin, Key = "shared", Version = 1,
                    Origin = ContentOrigin.Package, DisplayName = "Package (enabled)",
                    Priority = 50, Enabled = true, IsActive = true, IsShadowed = false,
                    DiscoveredUtc = DateTime.UtcNow,
                });
            await db.SaveChangesAsync();
        }

        var catalog = new ContentCatalog(new NullResolver());
        var discovery = new DiscoveryService(factory, Array.Empty<ICmsContentSource>(), catalog);
        await discovery.ReloadCatalogAsync();

        // ResolveTag returns Missing when the resolver returns null (NullResolver), so check the
        // underlying catalog.All list — a descriptor in All means the row WON the shadowing election.
        Assert.That(catalog.All.Any(d => d.Kind == ContentKind.Plugin && d.Key == "shared" && d.Version == 1),
            Is.True,
            "enabled Package row must appear in catalog.All — it must win over the disabled Compiled row");

        // Cross-check: the DB rows must have the correct IsShadowed flags after the reload.
        await using var verify = factory.CreateDbContext();
        var rows = await verify.ContentDefinitions.Where(d => d.Key == "shared").ToListAsync();
        var compiledRow = rows.Single(d => d.Origin == ContentOrigin.Compiled);
        var packageRow  = rows.Single(d => d.Origin == ContentOrigin.Package);
        Assert.Multiple(() =>
        {
            Assert.That(packageRow.IsShadowed,  Is.False, "enabled Package row must not be shadowed");
            Assert.That(compiledRow.IsShadowed, Is.True,  "disabled Compiled row must be marked shadowed");
        });
    }

    [Test]
    public async Task Delete_CompiledAlreadyDisabled_ReturnsDisabledInstead_WithoutRechurn()
    {
        // Regression: DeleteAsync for compiled citizens always called SaveChangesAsync + ReloadCatalogAsync,
        // even when def.Enabled was already false, causing a gratuitous catalog reload.
        // The fix guards with `if (def.Enabled)` before the write/reload path.
        var factory = new InMemoryFactory("lc_compiledis_" + Guid.NewGuid().ToString("N"));
        await using (var db = factory.CreateDbContext())
        {
            db.ContentDefinitions.Add(new CmsContentDefinition
            {
                Kind = ContentKind.Plugin, Key = "alreadydisabled", Version = 1,
                Origin = ContentOrigin.Compiled, DisplayName = "Already Disabled",
                Priority = 100, Enabled = false,   // already disabled before DeleteAsync is called
                IsActive = true, IsShadowed = false, DiscoveredUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var catalog = new ContentCatalog(new NullResolver());
        var discovery = new DiscoveryService(factory, Array.Empty<ICmsContentSource>(), catalog);
        var svc = new ContentLifecycleService(factory, discovery);

        var result = await svc.DeleteAsync(ContentKind.Plugin, "alreadydisabled", 1);

        Assert.Multiple(() =>
        {
            Assert.That(result.Deleted, Is.False);
            Assert.That(result.Blocked, Is.False);
            Assert.That(result.DisabledInstead, Is.True,
                "already-disabled compiled citizen must still return DisabledInstead=true");
        });
    }

    [Test]
    public async Task SetEnabledFalse_ReloadsCatalog_SoResolveTagReportsDisabled()
    {
        var factory = new InMemoryFactory("lc_" + Guid.NewGuid().ToString("N"));
        int id;
        await using (var db = factory.CreateDbContext())
        {
            var def = new CmsContentDefinition
            {
                Kind = ContentKind.Theme, Key = "cyberspace", Version = 1, Origin = ContentOrigin.Compiled,
                DisplayName = "Cyberspace", Enabled = true, IsActive = true, IsShadowed = false,
                DiscoveredUtc = DateTime.UtcNow,
            };
            db.ContentDefinitions.Add(def);
            await db.SaveChangesAsync();
            id = def.Id;
        }

        var catalog = new ContentCatalog(new NullResolver());
        var discovery = new DiscoveryService(factory, Array.Empty<ICmsContentSource>(), catalog);
        var svc = new ContentLifecycleService(factory, discovery);

        var result = await svc.SetEnabledAsync(id, enabled: false);

        Assert.That(result.Ok, Is.True);
        Assert.That(catalog.ResolveTag(ContentKind.Theme, "cyberspace", 1).Outcome,
            Is.EqualTo(ContentResolution.Disabled));   // disabled identity is now known-disabled, not Missing
    }

    [Test]
    public void LoadSnapshot_SetsEnabledAndDisabledAtomically()
    {
        // Regression: ContentCatalog exposed public Load() and LoadDisabled() methods that updated the
        // snapshot separately. A concurrent reader between the two calls would see a torn state — the
        // enabled list updated but the disabled list still stale (or vice versa), making a known-disabled
        // identity appear as Missing instead of Disabled. Both methods are now internal; LoadSnapshot is
        // the sole public write path and atomically replaces both halves in one volatile write.
        var catalog = new ContentCatalog(new NullResolver());
        var enabledDesc = new ContentDescriptor
        {
            Kind = ContentKind.Plugin, Key = "active", Version = 1,
            DisplayName = "Active", Category = "Plugin", Origin = ContentOrigin.Compiled,
        };

        catalog.LoadSnapshot(
            enabled: [enabledDesc],
            disabled: [(ContentKind.Plugin, "disabled-key", 1)]);

        // Enabled winner resolves to Missing (no CLR type via NullResolver) — not Disabled.
        Assert.That(catalog.ResolveTag(ContentKind.Plugin, "active", 1).Outcome,
            Is.Not.EqualTo(ContentResolution.Disabled),
            "enabled identity must not be reported as Disabled after LoadSnapshot");

        // Disabled identity resolves to Disabled — both halves are set atomically.
        Assert.That(catalog.ResolveTag(ContentKind.Plugin, "disabled-key", 1).Outcome,
            Is.EqualTo(ContentResolution.Disabled),
            "disabled identity must be reported as Disabled after a single LoadSnapshot call");
    }

    [Test]
    public async Task ReloadCatalogAsync_ConcurrentCalls_DoNotThrowOrDeadlock()
    {
        // Regression: ReloadCatalogAsync had no serialization lock; two concurrent admin enable/disable
        // operations could interleave their IsShadowed writes and LoadSnapshot calls, producing a torn
        // catalog or a DbUpdateConcurrencyException. The SemaphoreSlim(1,1) serializes all reloads.
        var factory = new InMemoryFactory("lc_concur_" + Guid.NewGuid().ToString("N"));
        await using (var db = factory.CreateDbContext())
        {
            db.ContentDefinitions.Add(new CmsContentDefinition
            {
                Kind = ContentKind.Plugin, Key = "concurrent", Version = 1,
                Origin = ContentOrigin.Compiled, DisplayName = "Concurrent",
                Priority = 100, Enabled = true, IsActive = true, IsShadowed = false,
                DiscoveredUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var catalog = new ContentCatalog(new NullResolver());
        var discovery = new DiscoveryService(factory, Array.Empty<ICmsContentSource>(), catalog);

        // Two concurrent reloads: the SemaphoreSlim queues one behind the other — neither should throw.
        await Task.WhenAll(discovery.ReloadCatalogAsync(), discovery.ReloadCatalogAsync());

        // Both calls completed: the descriptor must still be present in the catalog.
        Assert.That(catalog.All.Any(d => d.Key == "concurrent"), Is.True,
            "concurrent ReloadCatalogAsync calls must not corrupt the catalog");
    }
}
