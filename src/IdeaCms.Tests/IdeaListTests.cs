using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using IdeaCms.Abstractions;
using IdeaCms.Blazor.Cli;
using IdeaCms.Core.Data;
using IdeaCms.Core.Discovery;
using IdeaCms.Core.Entities;
using IdeaCms.Core.Portability;
using IdeaCms.Core.Services;
using IdeaCms.Packaging;
using IdeaCms.Tests.Packaging;
using IdeaCms.Tests.Portability;
using MindAttic.Media;
using CmsPage = IdeaCms.Core.Entities.Page;

namespace IdeaCms.Tests;

/// <summary>
/// `.idealist` (M.IDEA-§4.9) moves AUTHORED content between environments
/// (the cases ported from the old `ContentBundleTests`, renamed) and now also names which `.idea`
/// packages a fresh instance should install, in order, before any page is written — with a stricter,
/// non-degrading check on each page's declared `Uses[]`.
/// </summary>
[TestFixture]
public class IdeaListTests
{
    private sealed class FakeMediaStore : IMediaStore
    {
        public readonly List<MediaItem> Items = [];
        public int Uploads;

        public Task<MediaItem> UploadAsync(Stream content, string fileName, string contentType,
            int? tenantId = null, string folder = "", string mediaType = "", int? width = null,
            int? height = null, string? notes = null, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            content.CopyTo(ms);
            var bytes = ms.ToArray();
            Uploads++;
            var item = new MediaItem
            {
                // A NEW uid on every upload — this is the real store's behaviour and the whole reason
                // import has to remap references rather than trusting the exported uid.
                Uid = Guid.NewGuid(), FileName = fileName, ContentType = contentType, Folder = folder,
                MediaType = mediaType, SizeBytes = bytes.Length, Bytes = bytes, Width = width,
                Height = height, Notes = notes,
                Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
            };
            Items.Add(item);
            return Task.FromResult(item);
        }

        public Task<IReadOnlyList<MediaItem>> ListAsync(int? tenantId = null, string? folder = null,
            string? mediaType = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MediaItem>>(Items.ToList());

        public Task<MediaItem?> GetMetaAsync(Guid uid, CancellationToken ct = default)
            => Task.FromResult(Items.FirstOrDefault(i => i.Uid == uid));

        public Task<(MediaItem Meta, Stream Content)?> GetAsync(Guid uid, CancellationToken ct = default)
        {
            var m = Items.FirstOrDefault(i => i.Uid == uid);
            return Task.FromResult(m is null ? null : ((MediaItem, Stream)?)(m, new MemoryStream(m.Bytes!)));
        }

        public Task<bool> DeleteAsync(Guid uid, CancellationToken ct = default)
            => Task.FromResult(Items.RemoveAll(i => i.Uid == uid) > 0);
    }

    private sealed class InMemoryFactory(string dbName) : IDbContextFactory<CmsDbContext>
    {
        private readonly DbContextOptions<CmsDbContext> _opts =
            new DbContextOptionsBuilder<CmsDbContext>().UseInMemoryDatabase(dbName).Options;
        public CmsDbContext CreateDbContext() => new(_opts);
        public Task<CmsDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(CreateDbContext());
    }

    private sealed class NullResolver : ITypeResolver
    {
        public Type? Resolve(ContentDescriptor descriptor) => null;
    }

    /// <summary>Just enough of <see cref="IHostEnvironment"/> for the CLI's package-resolver default
    /// (ContentRootPath/library) to resolve without touching a real host.</summary>
    private sealed class FakeHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "IdeaCms.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class Env
    {
        public required InMemoryFactory Factory { get; init; }
        public required FakeMediaStore Store { get; init; }
        public required IServiceProvider Services { get; init; }
        public CmsDbContext Db() => Factory.CreateDbContext();
    }

    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "idealist_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string ListPath => Path.Combine(_dir, "site.idealist");

    private Env NewEnv()
    {
        var factory = new InMemoryFactory("idealist_" + Guid.NewGuid().ToString("N"));
        var store = new FakeMediaStore();
        var catalog = new ContentCatalog(new NullResolver());
        var discovery = new DiscoveryService(factory, Array.Empty<ICmsContentSource>(), catalog);
        var installer = new PackageInstallService(
            factory, discovery, new InMemoryPackageBlobStore(), new NullPackageExtractor(), new NullRenderAlertSink(),
            new TestPackageSigningTrust(), new AdminInboxService(factory));

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<CmsDbContext>>(factory);
        services.AddScoped(_ => factory.CreateDbContext());
        services.AddSingleton<IMediaStore>(store);
        services.AddSingleton<IPackageInstallService>(installer);
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment(_dir));
        // The real host always has IConfiguration; the CLI's resolver factory (filesystem + optional
        // NuGet fallback) reads IdeaCms:NuGetFeedUrl from it — an empty config here means "no feed
        // configured", so it composes down to just the filesystem resolver, exactly like today's
        // no-IdeaCms:NuGetFeedUrl production default.
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        return new Env { Factory = factory, Store = store, Services = services.BuildServiceProvider() };
    }

    private static async Task SeedSiteAsync(Env env, string key = "default")
    {
        await using var db = env.Db();
        db.Sites.Add(new Site
        {
            Key = key, Name = "MindAttic", IsDefault = true, DefaultThemeKey = "cyberspace",
            DefaultThemeVersion = 1, CreatedUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<CmsPage> AddPageAsync(Env env, string slug, string body,
        ContentTrust trust = ContentTrust.Author, Guid? uid = null)
    {
        await using var db = env.Db();
        var site = await db.Sites.FirstAsync();
        var page = new CmsPage
        {
            Uid = uid ?? Guid.NewGuid(), SiteId = site.Id, Slug = slug, Title = slug,
            Kind = PageKind.Data, BodyHtml = body, BodyTrust = trust, AuthorTrustVersion = 1,
            IsPublished = true, Enabled = true, ThemeKey = "cyberspace", ThemeVersion = 1,
            ActivePluginsJson = """["Plugin.tooltip"]""", CreatedUtc = DateTime.UtcNow,
        };
        db.Pages.Add(page);
        await db.SaveChangesAsync();
        return page;
    }

    private async Task<int> ExportAsync(Env env, params string[] extra) =>
        await ExportIdeaListCli.RunAsync(["--export-idealist", ListPath, .. extra], env.Services);

    private async Task<int> ImportAsync(Env env, params string[] extra) =>
        await ImportIdeaListCli.RunAsync(["--import-idealist", ListPath, .. extra], env.Services);

    private static ZipArchive EmptyZip() => new(new MemoryStream(), ZipArchiveMode.Update);

    private static byte[] BuildCodePackageBytes(string key, int version, string category, string displayName) =>
        IdeaTestArchive.Build(new Dictionary<string, string>
        {
            ["idea.json"] = ManifestReader.Write(new IdeaManifest
            {
                ManifestVersion = 1, Category = category, Kind = "code", Key = key, Version = version,
                DisplayName = displayName, Sdk = 1, EntryType = $"IdeaCms.{category}.Demo.V{version}",
                AssemblyName = "Demo",
            }),
            ["bin/Demo.dll"] = "MZ-fake",
        }).ToArray();

    private static byte[] BuildPackageRequiring(string key, string depRef) =>
        IdeaTestArchive.Build(new Dictionary<string, string>
        {
            ["idea.json"] = ManifestReader.Write(new IdeaManifest
            {
                ManifestVersion = 1, Category = "Plugin", Kind = "code", Key = key, Version = 1,
                DisplayName = key, Sdk = 1, EntryType = $"IdeaCms.Plugin.{key}.V1",
                AssemblyName = key, Requires = [depRef],
            }),
            [$"bin/{key}.dll"] = "MZ-fake",
        }).ToArray();

    // =========================================================================================
    // Authored-content round trips.
    // =========================================================================================

    [Test]
    public async Task RoundTrip_PreservesTheAuthoredPage()
    {
        var source = NewEnv();
        await SeedSiteAsync(source);
        await AddPageAsync(source, "frontpage", "<h1>Hello</h1>");
        await using (var db = source.Db())
        {
            var p = await db.Pages.FirstAsync();
            p.PageCss = ".x{color:red}";
            p.PageJs = "window.x=1;";
            p.SeoTitle = "Front";
            db.PageMetaTags.Add(new PageMetaTag { PageId = p.Id, Name = "seo.description", Content = "desc" });
            db.Settings.Add(new SettingEntry { Scope = "Host", Key = "css.global", Value = "body{margin:0}" });
            db.ComponentMetadata.Add(new ComponentMetadata
            {
                PageUid = p.Uid, ComponentKey = "frommd", SlotName = "main",
                MetadataJson = """{"localSourceFile":"README.md"}""",
                CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        Assert.That(await ExportAsync(source), Is.Zero);

        var target = NewEnv();
        Assert.That(await ImportAsync(target), Is.Zero);

        await using var read = target.Db();
        var imported = await read.Pages.Include(p => p.MetaTags).SingleAsync();
        Assert.Multiple(async () =>
        {
            Assert.That(imported.Slug, Is.EqualTo("frontpage"));
            Assert.That(imported.BodyHtml, Is.EqualTo("<h1>Hello</h1>"));
            Assert.That(imported.PageCss, Is.EqualTo(".x{color:red}"));
            Assert.That(imported.PageJs, Is.EqualTo("window.x=1;"));
            Assert.That(imported.SeoTitle, Is.EqualTo("Front"));
            Assert.That(imported.BodyTrust, Is.EqualTo(ContentTrust.Author));
            Assert.That(imported.ThemeKey, Is.EqualTo("cyberspace"));
            Assert.That(imported.ActivePluginsJson, Is.EqualTo("""["Plugin.tooltip"]"""));
            Assert.That(imported.MetaTags.Single().Content, Is.EqualTo("desc"));
            Assert.That((await read.Settings.SingleAsync(s => s.Key == "css.global")).Value,
                Is.EqualTo("body{margin:0}"));
            Assert.That(await read.ComponentMetadata.CountAsync(), Is.EqualTo(1));
            Assert.That((await read.Sites.SingleAsync()).DefaultThemeKey, Is.EqualTo("cyberspace"));
        });
    }

    [Test]
    public async Task RoundTrip_CarriesThemeAndPluginInstanceSettings()
    {
        var source = NewEnv();
        await SeedSiteAsync(source);
        var page = await AddPageAsync(source, "frontpage", "<h1>Hello</h1>");
        await using (var db = source.Db())
        {
            db.WidgetPlacementSettings.Add(new WidgetPlacementSettings
            {
                PageId = page.Id, SlotName = "plugin:cyberspace", WidgetRef = "Plugin.cyberspace",
                SettingsJson = """{"Crash":false}""", CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow,
            });
            db.WidgetPlacementSettings.Add(new WidgetPlacementSettings
            {
                PageId = page.Id, SlotName = "theme", WidgetRef = "Theme.cyberspace@1",
                SettingsJson = """{"PagePadding":"2rem"}""", CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        Assert.That(await ExportAsync(source), Is.Zero);
        var target = NewEnv();
        Assert.That(await ImportAsync(target), Is.Zero);
        Assert.That(await ImportAsync(target), Is.Zero, "a second import of the same list is a no-op for slots");

        await using var read = target.Db();
        var imported = await read.Pages.SingleAsync();
        var slots = await read.WidgetPlacementSettings.Where(s => s.PageId == imported.Id)
            .ToDictionaryAsync(s => s.SlotName);
        Assert.Multiple(async () =>
        {
            Assert.That(slots.Keys, Is.EquivalentTo(new[] { "plugin:cyberspace", "theme" }));
            Assert.That(slots["plugin:cyberspace"].SettingsJson, Is.EqualTo("""{"Crash":false}"""));
            Assert.That(slots["theme"].WidgetRef, Is.EqualTo("Theme.cyberspace@1"));
            Assert.That(slots["theme"].SettingsVersion, Is.EqualTo(1), "unchanged on re-import");
            Assert.That(await read.WidgetPlacementSettingsHistory.CountAsync(), Is.Zero);
        });
    }

    [Test]
    public async Task Import_RemovesSlotsTheListOmits_ButAnOldFormatListLeavesThemAlone()
    {
        var source = NewEnv();
        await SeedSiteAsync(source);
        await AddPageAsync(source, "frontpage", "<h1>Hello</h1>");   // exported with NO slots → InstanceSettings = []
        Assert.That(await ExportAsync(source), Is.Zero);

        var target = NewEnv();
        Assert.That(await ImportAsync(target), Is.Zero);
        async Task AddStaleSlotAsync()
        {
            await using var db = target.Db();
            var p = await db.Pages.SingleAsync();
            db.WidgetPlacementSettings.Add(new WidgetPlacementSettings
            {
                PageId = p.Id, SlotName = "theme", WidgetRef = "Theme.cyberspace", SettingsJson = """{"PagePadding":"9rem"}""",
                CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        async Task<int> SlotCountAsync() { await using var db = target.Db(); return await db.WidgetPlacementSettings.CountAsync(); }

        await AddStaleSlotAsync();
        Assert.That(await ImportAsync(target), Is.Zero);
        Assert.That(await SlotCountAsync(), Is.Zero, "a list that names no slots is authoritative: the stale slot goes");

        // Rewrite the list in the older format with no instanceSettings property at all.
        using (var zip = ZipFile.Open(ListPath, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry(IdeaList.ManifestEntryName)!;
            string json;
            using (var r = new StreamReader(entry.Open())) json = r.ReadToEnd();
            var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
            foreach (var p in node["pages"]!.AsArray()) p!.AsObject().Remove("instanceSettings");
            entry.Delete();
            using var w = new StreamWriter(zip.CreateEntry(IdeaList.ManifestEntryName).Open());
            w.Write(node.ToJsonString());
        }
        await AddStaleSlotAsync();
        Assert.That(await ImportAsync(target), Is.Zero);
        Assert.That(await SlotCountAsync(), Is.EqualTo(1), "an old-format list says nothing about slots, so it must not delete them");
    }

    [Test]
    public async Task ImportAdoptsAnIndependentlySeededPage_BySlug_RatherThanDuplicatingIt()
    {
        var source = NewEnv();
        await SeedSiteAsync(source);
        await AddPageAsync(source, "frontpage", "<h1>authored</h1>");
        Assert.That(await ExportAsync(source), Is.Zero);

        // The target was seeded on its own, so "frontpage" exists with a DIFFERENT uid — the exact
        // shape of a production database that booted before anyone exported anything.
        var target = NewEnv();
        await SeedSiteAsync(target);
        await AddPageAsync(target, "frontpage", "<h1>seeded</h1>");

        Assert.That(await ImportAsync(target), Is.Zero);

        await using var read = target.Db();
        Assert.Multiple(async () =>
        {
            Assert.That(await read.Pages.CountAsync(p => p.Slug == "frontpage"), Is.EqualTo(1),
                "a uid mismatch must not create a second page on the same slug");
            Assert.That((await read.Pages.SingleAsync()).BodyHtml, Is.EqualTo("<h1>authored</h1>"),
                "the idealist is the authority for a page it carries");
        });
    }

    [Test]
    public async Task MediaUidsAreRemapped_SoEveryReferenceStillResolves()
    {
        var source = NewEnv();
        await SeedSiteAsync(source);
        var upload = await source.Store.UploadAsync(
            new MemoryStream([1, 2, 3, 4]), "logo.png", "image/png", folder: "site", mediaType: "image");
        var oldUid = upload.Uid;

        await AddPageAsync(source, "home",
            $"""<div style="background:url(/_media/{oldUid})"></div>""");
        await using (var db = source.Db())
        {
            var p = await db.Pages.FirstAsync();
            db.ComponentMetadata.Add(new ComponentMetadata
            {
                PageUid = p.Uid, ComponentKey = "gallery", SlotName = "main",
                MetadataJson = $$"""{"items":["{{oldUid}}"]}""",
                CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        Assert.That(await ExportAsync(source), Is.Zero);

        var target = NewEnv();
        Assert.That(await ImportAsync(target), Is.Zero);

        var newUid = target.Store.Items.Single().Uid;
        await using var read = target.Db();
        var body = (await read.Pages.SingleAsync()).BodyHtml!;
        var meta = (await read.ComponentMetadata.SingleAsync()).MetadataJson;

        Assert.Multiple(() =>
        {
            Assert.That(newUid, Is.Not.EqualTo(oldUid), "the store mints the uid, so it must have changed");
            Assert.That(body, Does.Not.Contain(oldUid.ToString()), "no reference may still point at the source uid");
            Assert.That(body, Does.Contain($"/_media/{newUid}"));
            Assert.That(meta, Does.Contain(newUid.ToString()).And.Not.Contain(oldUid.ToString()));
            Assert.That(target.Store.Items.Single().Bytes, Is.EqualTo(new byte[] { 1, 2, 3, 4 }),
                "the payload itself must survive the archive round trip");
        });
    }

    [Test]
    public async Task SecondImportUploadsNothingAndCreatesNothing()
    {
        var source = NewEnv();
        await SeedSiteAsync(source);
        await source.Store.UploadAsync(new MemoryStream([9, 9, 9]), "a.png", "image/png");
        await AddPageAsync(source, "home", "<p>hi</p>");
        Assert.That(await ExportAsync(source), Is.Zero);

        var target = NewEnv();
        Assert.That(await ImportAsync(target), Is.Zero);
        var afterFirst = target.Store.Uploads;

        Assert.That(await ImportAsync(target), Is.Zero);

        await using var read = target.Db();
        Assert.Multiple(async () =>
        {
            Assert.That(afterFirst, Is.EqualTo(1));
            Assert.That(target.Store.Uploads, Is.EqualTo(1),
                "identical bytes are matched by SHA-256, so a re-import moves no payloads");
            Assert.That(await read.Pages.CountAsync(), Is.EqualTo(1));
            Assert.That(target.Store.Items, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task UntrustedFlag_DowngradesAuthorTrust()
    {
        var source = NewEnv();
        await SeedSiteAsync(source);
        await AddPageAsync(source, "home", "<script>alert(1)</script>", ContentTrust.Author);
        Assert.That(await ExportAsync(source), Is.Zero);

        var target = NewEnv();
        Assert.That(await ImportAsync(target, "--untrusted"), Is.Zero);

        await using var read = target.Db();
        Assert.That((await read.Pages.SingleAsync()).BodyTrust, Is.EqualTo(ContentTrust.Untrusted),
            "an operator must be able to refuse raw-markup trust from an idealist they did not author");
    }

    [Test]
    public async Task DryRunImport_WritesNothing()
    {
        var source = NewEnv();
        await SeedSiteAsync(source);
        await source.Store.UploadAsync(new MemoryStream([7]), "b.png", "image/png");
        await AddPageAsync(source, "home", "<p>hi</p>");
        Assert.That(await ExportAsync(source), Is.Zero);

        var target = NewEnv();
        Assert.That(await ImportAsync(target, "--dry-run"), Is.Zero);

        await using var read = target.Db();
        Assert.Multiple(async () =>
        {
            Assert.That(await read.Pages.CountAsync(), Is.Zero);
            Assert.That(target.Store.Uploads, Is.Zero);
        });
    }

    [Test]
    public async Task SlugFilter_ExportsOnlyTheMatchingSubtree()
    {
        var source = NewEnv();
        await SeedSiteAsync(source);
        await AddPageAsync(source, "frontpage", "<p>front</p>");
        await AddPageAsync(source, "projects/alpha", "<p>a</p>");
        await AddPageAsync(source, "projects/beta", "<p>b</p>");

        Assert.That(await ExportAsync(source, "--slug", "projects/"), Is.Zero);

        var target = NewEnv();
        Assert.That(await ImportAsync(target), Is.Zero);

        await using var read = target.Db();
        var slugs = await read.Pages.Select(p => p.Slug).OrderBy(s => s).ToListAsync();
        Assert.That(slugs, Is.EqualTo(new[] { "projects/alpha", "projects/beta" }));
    }

    [Test]
    public async Task PageTree_SurvivesOnParentUid()
    {
        var source = NewEnv();
        await SeedSiteAsync(source);
        var parent = await AddPageAsync(source, "projects", "<p>index</p>");
        var child = await AddPageAsync(source, "projects/alpha", "<p>a</p>");
        await using (var db = source.Db())
        {
            var c = await db.Pages.FirstAsync(p => p.Uid == child.Uid);
            c.ParentId = parent.Id;
            await db.SaveChangesAsync();
        }

        Assert.That(await ExportAsync(source), Is.Zero);

        var target = NewEnv();
        Assert.That(await ImportAsync(target), Is.Zero);

        await using var read = target.Db();
        var importedParent = await read.Pages.SingleAsync(p => p.Slug == "projects");
        var importedChild = await read.Pages.SingleAsync(p => p.Slug == "projects/alpha");
        Assert.That(importedChild.ParentId, Is.EqualTo(importedParent.Id),
            "the tree must be rebuilt from uids, never from the source environment's integer ids");
    }

    [Test]
    public async Task ExportCarriesOneSite_AndImportCreatesThatSiteRatherThanDumpingOntoTheDefault()
    {
        var source = NewEnv();
        await SeedSiteAsync(source, "rdb");
        await AddPageAsync(source, "frontpage", "<p>ryan</p>");
        Assert.That(await ExportAsync(source), Is.Zero);

        // The target hosts a DIFFERENT site. One deployment can serve several domains (M.IDEA-§4.10), so
        // falling back to the default site here would republish rdb's pages under mindattic.com.
        var target = NewEnv();
        await SeedSiteAsync(target, "default");
        await AddPageAsync(target, "frontpage", "<p>mindattic</p>");

        Assert.That(await ImportAsync(target), Is.Zero);

        await using var read = target.Db();
        var defaultSite = await read.Sites.SingleAsync(s => s.Key == "default");
        var rdbSite = await read.Sites.SingleAsync(s => s.Key == "rdb");
        Assert.Multiple(async () =>
        {
            Assert.That(await read.Sites.CountAsync(), Is.EqualTo(2), "the idealist's site must be created");
            Assert.That(rdbSite.IsDefault, Is.False, "an imported site must not seize the default");
            Assert.That((await read.Pages.SingleAsync(p => p.SiteId == defaultSite.Id)).BodyHtml,
                Is.EqualTo("<p>mindattic</p>"), "the existing site's page must be untouched");
            Assert.That((await read.Pages.SingleAsync(p => p.SiteId == rdbSite.Id)).BodyHtml,
                Is.EqualTo("<p>ryan</p>"));
        });
    }

    [Test]
    public async Task ExportTakesOnlyTheNamedSitesPages()
    {
        var env = NewEnv();
        await SeedSiteAsync(env, "default");
        await AddPageAsync(env, "frontpage", "<p>mindattic</p>");
        await using (var db = env.Db())
        {
            db.Sites.Add(new Site { Key = "rdb", Name = "Ryan", CreatedUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
            var rdb = await db.Sites.SingleAsync(s => s.Key == "rdb");
            db.Pages.Add(new CmsPage
            {
                Uid = Guid.NewGuid(), SiteId = rdb.Id, Slug = "frontpage", Title = "Ryan",
                Kind = PageKind.Data, BodyHtml = "<p>ryan</p>", BodyTrust = ContentTrust.Author,
                AuthorTrustVersion = 1, IsPublished = true, Enabled = true, CreatedUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        Assert.That(await ExportAsync(env, "--site", "rdb"), Is.Zero);

        var target = NewEnv();
        Assert.That(await ImportAsync(target), Is.Zero);

        await using var read = target.Db();
        var page = await read.Pages.SingleAsync();
        Assert.Multiple(async () =>
        {
            Assert.That(page.BodyHtml, Is.EqualTo("<p>ryan</p>"),
                "a --site export must not sweep in the other domain's identically-slugged page");
            Assert.That((await read.Sites.SingleAsync()).Key, Is.EqualTo("rdb"));
        });
    }

    [Test]
    public async Task ExportWithAnUnknownSiteKey_FailsRatherThanExportingTheWrongSite()
    {
        var env = NewEnv();
        await SeedSiteAsync(env, "default");
        await AddPageAsync(env, "frontpage", "<p>x</p>");

        Assert.Multiple(() =>
        {
            Assert.That(ExportAsync(env, "--site", "nope").Result, Is.EqualTo(1));
            Assert.That(File.Exists(ListPath), Is.False, "a refused export must write no file");
        });
    }

    [Test]
    public async Task AnIdeaListFromAFutureFormat_IsRefusedRatherThanPartiallyApplied()
    {
        var source = NewEnv();
        await SeedSiteAsync(source);
        await AddPageAsync(source, "home", "<p>hi</p>");
        Assert.That(await ExportAsync(source), Is.Zero);

        // Rewrite the manifest as a version this host does not know.
        using (var zip = ZipFile.Open(ListPath, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("idealist.json")!;
            string json;
            using (var r = new StreamReader(entry.Open())) json = await r.ReadToEndAsync();
            json = json.Replace("\"formatVersion\": 1", "\"formatVersion\": 99");
            entry.Delete();
            var replacement = zip.CreateEntry("idealist.json");
            await using var w = new StreamWriter(replacement.Open());
            await w.WriteAsync(json);
        }

        var target = NewEnv();
        Assert.That(await ImportAsync(target), Is.EqualTo(1));

        await using var read = target.Db();
        Assert.That(await read.Pages.CountAsync(), Is.Zero, "a refused idealist must leave no partial state");
    }

    [Test]
    public async Task IntoSite_CopiesThePagesRatherThanMovingThemOffTheSiteThatHasThem()
    {
        // A uid is the PORTABLE identity, so it is global across the deployment — while a page belongs to
        // exactly one site. Reconciling on uid without a site filter would find the page this deployment
        // already has under another site and re-point its SiteId, so --into-site would MOVE a site's pages
        // instead of giving the target its own copy (M.IDEA-§4.9).
        var source = NewEnv();
        await SeedSiteAsync(source);
        var original = await AddPageAsync(source, "frontpage", "<h1>original</h1>");
        Assert.That(await ExportAsync(source), Is.Zero);

        // The target already holds that very page, on its own default site.
        var target = NewEnv();
        await SeedSiteAsync(target);
        var mainSiteId = (await target.Db().Sites.SingleAsync()).Id;
        await AddPageAsync(target, "frontpage", "<h1>original</h1>", uid: original.Uid);

        Assert.That(await ImportAsync(target, "--into-site", "microsite"), Is.Zero);

        await using var read = target.Db();
        var microsite = await read.Sites.SingleAsync(x => x.Key == "microsite");
        Assert.Multiple(async () =>
        {
            Assert.That(await read.Pages.CountAsync(p => p.SiteId == mainSiteId), Is.EqualTo(1),
                "the site that already had the page keeps it");
            Assert.That(await read.Pages.CountAsync(p => p.SiteId == microsite.Id), Is.EqualTo(1),
                "and the named site gets its own copy");
            Assert.That(await read.Pages.CountAsync(), Is.EqualTo(2));
            Assert.That(await read.Pages.Select(p => p.Uid).Distinct().CountAsync(), Is.EqualTo(2),
                "a copy needs its own uid — the portable identity is unique deployment-wide");
        });
    }

    [Test]
    public async Task NotAnIdeaList_IsReportedRatherThanThrowing()
    {
        await File.WriteAllBytesAsync(ListPath, System.Text.Encoding.UTF8.GetBytes("not a zip"));
        var target = NewEnv();
        Assert.That(await ImportAsync(target), Is.EqualTo(1),
            "a corrupt archive is reported as a failure exit code, not a stack trace");
    }

    [Test]
    public async Task Prune_SoftDeletesPagesAbsentFromTheIdeaList()
    {
        var source = NewEnv();
        await SeedSiteAsync(source);
        await AddPageAsync(source, "keep", "<p>keep</p>");
        Assert.That(await ExportAsync(source), Is.Zero);

        var target = NewEnv();
        await SeedSiteAsync(target);
        await AddPageAsync(target, "keep", "<p>old</p>");
        await AddPageAsync(target, "stray", "<p>stray</p>");

        Assert.That(await ImportAsync(target, "--prune"), Is.Zero);

        await using var read = target.Db();
        var stray = await read.Pages.IgnoreQueryFilters().SingleAsync(p => p.Slug == "stray");
        var keptCount = await read.Pages.CountAsync(p => p.Slug == "keep");
        Assert.Multiple(() =>
        {
            Assert.That(stray.IsDeleted, Is.True, "a page absent from the idealist is soft-deleted under --prune");
            Assert.That(keptCount, Is.EqualTo(1));
        });
    }

    // =========================================================================================
    // New: Packages[] install order, per-page Uses[] strict validation, package resolution.
    // =========================================================================================

    [Test]
    public async Task Packages_InstallInListedOrder_LaterEntryCanRequireAnEarlierOne()
    {
        var env = NewEnv();
        var resolver = new InMemoryIdeaListPackageResolver();
        resolver.Add(ContentKind.Plugin, "tooltip", 1, BuildCodePackageBytes("tooltip", 1, "Plugin", "Tooltip"));
        resolver.Add(ContentKind.Plugin, "carousel", 1, BuildPackageRequiring("carousel", "Plugin.tooltip@1"));

        var importer = new IdeaListImporter(
            env.Db(), env.Store, env.Services.GetRequiredService<IPackageInstallService>(), resolver);
        var list = new IdeaList { Packages = ["Plugin.tooltip@1", "Plugin.carousel@1"] };

        using var zip = EmptyZip();
        var result = await importer.ImportAsync(zip, list, new IdeaListImportOptions(), (_, _) => { });

        Assert.That(result.PackagesInstalled, Is.EqualTo(2));
        await using var db = env.Db();
        Assert.That(await db.InstalledPackages.CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task Packages_ReversedOrder_FailsBecauseTheDependencyIsNotYetInstalled()
    {
        // Same two packages as the ordered-install test, listed the OTHER way round — proves the
        // importer genuinely installs in LISTED order rather than resolving requires[] against the
        // whole Packages[] set up front.
        var env = NewEnv();
        var resolver = new InMemoryIdeaListPackageResolver();
        resolver.Add(ContentKind.Plugin, "tooltip", 1, BuildCodePackageBytes("tooltip", 1, "Plugin", "Tooltip"));
        resolver.Add(ContentKind.Plugin, "carousel", 1, BuildPackageRequiring("carousel", "Plugin.tooltip@1"));

        var importer = new IdeaListImporter(
            env.Db(), env.Store, env.Services.GetRequiredService<IPackageInstallService>(), resolver);
        var list = new IdeaList { Packages = ["Plugin.carousel@1", "Plugin.tooltip@1"] };

        using var zip = EmptyZip();
        Assert.ThrowsAsync<IdeaListImportException>(
            () => importer.ImportAsync(zip, list, new IdeaListImportOptions(), (_, _) => { }));

        await using var db = env.Db();
        Assert.That(await db.InstalledPackages.CountAsync(), Is.Zero,
            "the failing first entry must leave nothing installed, even though the second entry is fine on its own");
    }

    [Test]
    public async Task PackagesFailure_AbortsBeforeAnyPageIsWritten()
    {
        var env = NewEnv();
        await SeedSiteAsync(env);
        var resolver = new InMemoryIdeaListPackageResolver(); // deliberately empty — "Plugin.ghost" resolves to nothing

        var importer = new IdeaListImporter(
            env.Db(), env.Store, env.Services.GetRequiredService<IPackageInstallService>(), resolver);
        var list = new IdeaList
        {
            Packages = ["Plugin.ghost@1"],
            Pages = [new IdeaListPage { Uid = Guid.NewGuid(), Slug = "home", Title = "home", BodyHtml = "<p>hi</p>" }],
        };

        using var zip = EmptyZip();
        var ex = Assert.ThrowsAsync<IdeaListImportException>(
            () => importer.ImportAsync(zip, list, new IdeaListImportOptions(), (_, _) => { }));
        Assert.That(ex!.Message, Does.Contain("Plugin.ghost@1"));

        await using var db = env.Db();
        Assert.That(await db.Pages.CountAsync(), Is.Zero, "no page may be written once Packages[] fails");
    }

    [Test]
    public async Task PageUsesUnmet_FailsLoudly_NoPartialApply()
    {
        var env = NewEnv();
        await SeedSiteAsync(env);
        var importer = new IdeaListImporter(
            env.Db(), env.Store, env.Services.GetRequiredService<IPackageInstallService>(),
            new InMemoryIdeaListPackageResolver());
        var list = new IdeaList
        {
            Pages =
            [
                new IdeaListPage
                {
                    Uid = Guid.NewGuid(), Slug = "home", Title = "home", BodyHtml = "<p>hi</p>",
                    Uses = ["Plugin.tooltip@1"],
                },
            ],
        };

        using var zip = EmptyZip();
        var ex = Assert.ThrowsAsync<IdeaListImportException>(
            () => importer.ImportAsync(zip, list, new IdeaListImportOptions(), (_, _) => { }));
        Assert.That(ex!.Reasons.Single(), Does.Contain("Plugin.tooltip@1"));

        await using var db = env.Db();
        Assert.That(await db.Pages.CountAsync(), Is.Zero,
            "an unmet Uses[] entry must leave nothing written, not just skip that one page");
    }

    [Test]
    public async Task PageUsesSatisfiedByJustInstalledPackage_Succeeds()
    {
        var env = NewEnv();
        await SeedSiteAsync(env);
        var resolver = new InMemoryIdeaListPackageResolver();
        resolver.Add(ContentKind.Plugin, "tooltip", 1, BuildCodePackageBytes("tooltip", 1, "Plugin", "Tooltip"));

        var importer = new IdeaListImporter(
            env.Db(), env.Store, env.Services.GetRequiredService<IPackageInstallService>(), resolver);
        var list = new IdeaList
        {
            Packages = ["Plugin.tooltip@1"],
            Pages =
            [
                new IdeaListPage
                {
                    Uid = Guid.NewGuid(), Slug = "home", Title = "home", BodyHtml = "<p>hi</p>",
                    Uses = ["Plugin.tooltip@1"],
                },
            ],
        };

        using var zip = EmptyZip();
        var result = await importer.ImportAsync(zip, list, new IdeaListImportOptions(), (_, _) => { });

        Assert.Multiple(() =>
        {
            Assert.That(result.PackagesInstalled, Is.EqualTo(1));
            Assert.That(result.PagesCreated, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task PackageResolver_SearchesMultipleDirectoriesInOrder_FirstMatchWins()
    {
        var dir1 = Path.Combine(_dir, "dir1"); Directory.CreateDirectory(dir1);
        var dir2 = Path.Combine(_dir, "dir2"); Directory.CreateDirectory(dir2);

        // Same (kind,key,version) in both — DisplayName marks which one a resolve actually returned.
        await File.WriteAllBytesAsync(Path.Combine(dir1, "a.idea"), BuildCodePackageBytes("tooltip", 1, "Plugin", "First"));
        await File.WriteAllBytesAsync(Path.Combine(dir2, "b.idea"), BuildCodePackageBytes("tooltip", 1, "Plugin", "Second"));

        var resolver = new FileSystemIdeaListPackageResolver([dir1, dir2]);
        await using var stream = await resolver.ResolveAsync(ContentKind.Plugin, "tooltip", 1);

        Assert.That(stream, Is.Not.Null);
        using var archive = IdeaArchiveReader.Open(stream!);
        archive.TryReadManifest(out var manifest, out _);
        Assert.That(manifest!.DisplayName, Is.EqualTo("First"), "dir1 is listed first and must win on a conflicting match");
    }

    [Test]
    public void PackageResolver_MissingEverywhere_ResolvesToNull()
    {
        var dir = Path.Combine(_dir, "emptylib");
        Directory.CreateDirectory(dir);
        var resolver = new FileSystemIdeaListPackageResolver([dir]);

        Assert.That(resolver.ResolveAsync(ContentKind.Plugin, "ghost", 1).Result, Is.Null);
    }

    [Test]
    public async Task PackageResolver_MissingEverywhere_ImportFailsLoudlyNamingTheEntry()
    {
        var env = NewEnv();
        var dir = Path.Combine(_dir, "emptylib");
        Directory.CreateDirectory(dir);
        var resolver = new FileSystemIdeaListPackageResolver([dir]);
        var importer = new IdeaListImporter(
            env.Db(), env.Store, env.Services.GetRequiredService<IPackageInstallService>(), resolver);
        var list = new IdeaList { Packages = ["Plugin.ghost@1"] };

        using var zip = EmptyZip();
        var ex = Assert.ThrowsAsync<IdeaListImportException>(
            () => importer.ImportAsync(zip, list, new IdeaListImportOptions(), (_, _) => { }));
        Assert.That(ex!.Message, Does.Contain("Plugin.ghost@1"));
    }

    // =========================================================================================
    // New: --compose-idealist
    // =========================================================================================

    [Test]
    public async Task ComposeIdealist_PackagesOnly_ProducesAnEmptySiteContentFreeArtifact()
    {
        var env = NewEnv();
        var exit = await ComposeIdeaListCli.RunAsync(
            ["--compose-idealist", ListPath, "--package", "Theme.cyberspace@1", "--package", "Plugin.tooltip@2"],
            env.Services);
        Assert.That(exit, Is.Zero);

        using var zip = ZipFile.OpenRead(ListPath);
        var (list, error) = await IdeaListImporter.ReadManifestAsync(zip);
        Assert.Multiple(() =>
        {
            Assert.That(list, Is.Not.Null, error);
            Assert.That(list!.Site, Is.Null);
            Assert.That(list.Pages, Is.Empty);
            Assert.That(list.Packages, Is.EquivalentTo(new[] { "Theme.cyberspace@1", "Plugin.tooltip@2" }));
        });
    }

    [Test]
    public async Task ComposeIdealist_FromSite_UnionsExplicitPackagesWithSiteContent()
    {
        var env = NewEnv();
        await SeedSiteAsync(env, "default");
        await AddPageAsync(env, "home", "<p>hi</p>");

        var exit = await ComposeIdeaListCli.RunAsync(
            ["--compose-idealist", ListPath, "--package", "Theme.cyberspace@1", "--from-site", "default"],
            env.Services);
        Assert.That(exit, Is.Zero);

        using var zip = ZipFile.OpenRead(ListPath);
        var (list, _) = await IdeaListImporter.ReadManifestAsync(zip);
        Assert.Multiple(() =>
        {
            Assert.That(list!.Packages, Does.Contain("Theme.cyberspace@1"),
                "an explicit --package ref must survive alongside the site export");
            Assert.That(list.Pages.Single().Slug, Is.EqualTo("home"));
            Assert.That(list.Site!.Key, Is.EqualTo("default"));
        });
    }
}
