using Microsoft.EntityFrameworkCore;
using MindAttic.Ideas.Abstractions;
using IdeaCms.Core.Data;
using IdeaCms.Core.Discovery;
using IdeaCms.Core.Entities;
using IdeaCms.Core.Services;
using IdeaCms.Packaging;
using PageEntity = IdeaCms.Core.Entities.Page;   // "Page" alone binds to the IdeaCms.Page namespace

namespace IdeaCms.Tests.Packaging;

/// <summary>
/// Seed-on-install: a Page (code) package carrying data/page.json becomes routable on upload — an
/// idempotent Page upsert by (SiteId, Slug) that stamps SourcePackageId, trusts as Untrusted, re-points
/// the type on a V1→V2 upgrade, and never clobbers a slug another package owns.
/// </summary>
[TestFixture]
public class SeedOnInstallTests
{
    private sealed class InMemoryFactory(string dbName) : IDbContextFactory<CmsDbContext>
    {
        private readonly DbContextOptions<CmsDbContext> _opts =
            new DbContextOptionsBuilder<CmsDbContext>().UseInMemoryDatabase(dbName).Options;
        public CmsDbContext CreateDbContext() => new(_opts);
    }

    private sealed class NullResolver : ITypeResolver { public Type? Resolve(ContentDescriptor d) => null; }

    private static (PackageInstallService Svc, InMemoryFactory Factory) NewService(string db)
    {
        var factory = new InMemoryFactory(db);
        var discovery = new DiscoveryService(factory, Array.Empty<ICmsContentSource>(), new ContentCatalog(new NullResolver()));
        var svc = new PackageInstallService(factory, discovery, new InMemoryPackageBlobStore(), new NullPackageExtractor(), new NullRenderAlertSink(),
            new TestPackageSigningTrust(), new AdminInboxService(factory));
        return (svc, factory);
    }

    private static async Task<int> SeedDefaultSiteAsync(InMemoryFactory factory)
    {
        await using var db = factory.CreateDbContext();
        var site = new Site { Key = "default", Name = "MindAttic", DefaultThemeKey = "cyberspace", DefaultThemeVersion = 1, IsDefault = true };
        db.Sites.Add(site);
        await db.SaveChangesAsync();
        return site.Id;
    }

    private static MemoryStream PagePackage(int version, string slug = "hello-world") =>
        IdeaTestArchive.Build(new Dictionary<string, string>
        {
            ["idea.json"] = ManifestReader.Write(new IdeaManifest
            {
                ManifestVersion = 1, Category = "Page", Kind = "code", Key = "helloworld", Version = version,
                DisplayName = "Hello World", Sdk = 1,
                EntryType = $"IdeaCms.Page.HelloWorld.V{version}", AssemblyName = "IdeaCms.Page.HelloWorld",
            }),
            ["bin/IdeaCms.Page.HelloWorld.dll"] = "MZ-fake",
            ["data/page.json"] = $$"""{"slug":"{{slug}}","title":"Hello World","themeKey":"cyberspace","themeVersion":1,"published":true}""",
        });

    [Test]
    public async Task Install_CreatesRoutablePageRow_FromSeed()
    {
        var (svc, factory) = NewService("seed_" + Guid.NewGuid().ToString("N"));
        var siteId = await SeedDefaultSiteAsync(factory);

        await svc.InstallAsync(PagePackage(1), allowOverride: false);

        await using var db = factory.CreateDbContext();
        var page = await db.Pages.SingleAsync();
        var pkgId = (await db.InstalledPackages.SingleAsync()).Id;
        Assert.Multiple(() =>
        {
            Assert.That(page.SiteId, Is.EqualTo(siteId));
            Assert.That(page.Slug, Is.EqualTo("hello-world"));
            Assert.That(page.Kind, Is.EqualTo(PageKind.Code));
            Assert.That(page.ComponentTypeName, Is.EqualTo("IdeaCms.Page.HelloWorld.V1"));
            Assert.That(page.ThemeKey, Is.EqualTo("cyberspace"));
            Assert.That(page.ThemeVersion, Is.EqualTo(1));
            Assert.That(page.IsPublished, Is.True);
            Assert.That(page.BodyTrust, Is.EqualTo(ContentTrust.Untrusted)); // a compiled page has no raw markup
            Assert.That(page.SourcePackageId, Is.EqualTo(pkgId));
        });
    }

    [Test]
    public async Task Reinstall_SameVersion_IsIdempotent_NoDuplicatePage()
    {
        var (svc, factory) = NewService("seed_" + Guid.NewGuid().ToString("N"));
        await SeedDefaultSiteAsync(factory);

        await svc.InstallAsync(PagePackage(1), allowOverride: false);
        await svc.InstallAsync(PagePackage(1), allowOverride: false); // NoOpAlreadyInstalled returns before the seed

        await using var db = factory.CreateDbContext();
        Assert.That(await db.Pages.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task Upgrade_V1ToV2_RepointsSamePageRow()
    {
        var (svc, factory) = NewService("seed_" + Guid.NewGuid().ToString("N"));
        await SeedDefaultSiteAsync(factory);

        await svc.InstallAsync(PagePackage(1), allowOverride: false);
        await svc.InstallAsync(PagePackage(2), allowOverride: false);

        await using var db = factory.CreateDbContext();
        var page = await db.Pages.SingleAsync();      // still one routable page
        Assert.That(page.ComponentTypeName, Is.EqualTo("IdeaCms.Page.HelloWorld.V2")); // re-pointed to V2
    }

    [Test]
    public async Task Install_DoesNotClobber_ASlugOwnedByAnotherPackage()
    {
        var (svc, factory) = NewService("seed_" + Guid.NewGuid().ToString("N"));
        var siteId = await SeedDefaultSiteAsync(factory);

        // An unrelated package already owns the "hello-world" slug.
        await using (var db = factory.CreateDbContext())
        {
            var otherPkg = new InstalledPackage
            {
                Category = "Page", Kind = "code", Key = "someoneelse", Version = 1, DisplayName = "Other",
                ManifestJson = "{}", ManifestVersion = 1, Sha256 = new string('0', 64), BlobPath = "x", Enabled = true,
            };
            db.InstalledPackages.Add(otherPkg);
            await db.SaveChangesAsync();
            db.Pages.Add(new PageEntity
            {
                SiteId = siteId, Slug = "hello-world", Title = "Owned Elsewhere", Kind = PageKind.Code,
                ComponentTypeName = "Other.V1", AssemblyName = "Other", IsPublished = true, Enabled = true,
                SourcePackageId = otherPkg.Id,
            });
            await db.SaveChangesAsync();
        }

        await svc.InstallAsync(PagePackage(1), allowOverride: false);

        await using var verify = factory.CreateDbContext();
        var page = await verify.Pages.SingleAsync(p => p.Slug == "hello-world");
        Assert.That(page.ComponentTypeName, Is.EqualTo("Other.V1"), "a slug owned by another package must not be clobbered");
    }

    [Test]
    public async Task Install_MixedCaseSlugInSeed_IsStoredLowercase()
    {
        // Regression: ApplyPageSeedAsync did Trim('/') but not ToLowerInvariant(), so a manifest with
        // slug "Hello-World" stored "Hello-World" in the DB, which would then fail the slug-uniqueness
        // pre-check (or worse, return a 404 on lowercase-canonical URLs).
        var (svc, factory) = NewService("seed_" + Guid.NewGuid().ToString("N"));
        await SeedDefaultSiteAsync(factory);

        await svc.InstallAsync(PagePackage(1, slug: "Hello-World"), allowOverride: false);

        await using var db = factory.CreateDbContext();
        var page = await db.Pages.SingleAsync();
        Assert.That(page.Slug, Is.EqualTo("hello-world"),
            "seed slug must be normalized to lowercase regardless of manifest casing");
    }
}
