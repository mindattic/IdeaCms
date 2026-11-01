using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using IdeaCms.Abstractions;
using IdeaCms.Blazor.Cli;
using IdeaCms.Core.Data;
using IdeaCms.Core.Entities;
using MindAttic.Media;
using CmsPage = IdeaCms.Core.Entities.Page;

namespace IdeaCms.Tests;

/// <summary>
/// --extract-media lifts inline base64 images out of page bodies into the managed media store. Base64
/// inlining is why a hand-authored page body runs to hundreds of kilobytes; this is the migration path off
/// it, so the cases that matter are: the rewrite is correct, identical bytes upload once, and unreadable
/// data is left alone rather than destroyed.
/// </summary>
[TestFixture]
public class ExtractMediaCliTests
{
    /// <summary>An in-memory IMediaStore that records what it was handed.</summary>
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
                Uid = Guid.NewGuid(), FileName = fileName, ContentType = contentType, Folder = folder,
                MediaType = mediaType, SizeBytes = bytes.Length, Bytes = bytes,
                Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
            };
            Items.Add(item);
            return Task.FromResult(item);
        }

        public Task<IReadOnlyList<MediaItem>> ListAsync(int? tenantId = null, string? folder = null,
            string? mediaType = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MediaItem>>(
                Items.Where(i => folder is null || i.Folder == folder).ToList());

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

    /// <summary>A 1x1 GIF — real, decodable image bytes, small enough to inline in a test.</summary>
    private const string Gif1x1 = "R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7";
    private const string Gif1x1Red = "R0lGODlhAQABAIABAP8AAP///yH5BAEAAAEALAAAAAABAAEAAAICTAEAOw==";

    private static async Task<(string Body, FakeMediaStore Store, int Exit)> RunAsync(string body, params string[] extraArgs)
    {
        var (b, _, store, exit) = await RunFullAsync(body, null, extraArgs);
        return (b, store, exit);
    }

    private static async Task<(string Body, string? Css, FakeMediaStore Store, int Exit)> RunFullAsync(
        string? body, string? css, params string[] extraArgs)
    {
        var factory = new InMemoryFactory("media_" + Guid.NewGuid().ToString("N"));
        await using (var db = factory.CreateDbContext())
        {
            db.Pages.Add(new CmsPage
            {
                SiteId = 1, Slug = "page-under-test", Title = "T", Kind = PageKind.Data,
                BodyHtml = body, PageCss = css, BodyTrust = ContentTrust.Author,
                IsPublished = true, Enabled = true, CreatedUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var store = new FakeMediaStore();
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<CmsDbContext>>(factory);
        services.AddScoped(_ => factory.CreateDbContext());
        services.AddSingleton<IMediaStore>(store);

        var exit = await ExtractMediaCli.RunAsync(
            ["--extract-media", .. extraArgs], services.BuildServiceProvider());

        await using var read = factory.CreateDbContext();
        var page = await read.Pages.IgnoreQueryFilters().FirstAsync(p => p.Slug == "page-under-test");
        return (page.BodyHtml ?? "", page.PageCss, store, exit);
    }

    [Test]
    public async Task InlineImage_BecomesAMediaImageTag_AndLeavesNoBase64()
    {
        var (body, store, exit) = await RunAsync(
            $"""<p>before</p><img src="data:image/gif;base64,{Gif1x1}" alt="A cover"><p>after</p>""");

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.Zero);
            Assert.That(store.Uploads, Is.EqualTo(1));
            Assert.That(body, Does.Not.Contain("base64,"));
            Assert.That(body, Does.Contain("<Component.MediaImage"));
            Assert.That(body, Does.Contain($@"uid=""{store.Items[0].Uid}"""));
            Assert.That(body, Does.Contain(@"alt=""A cover"""));
            Assert.That(body, Does.Contain("<p>before</p>").And.Contain("<p>after</p>"),
                "surrounding markup must survive untouched");
        });
    }

    [Test]
    public async Task IdenticalBytes_UploadOnce_AndBothReferencesPointAtIt()
    {
        // A logo repeated across a page (or across pages) must not become N copies of the same asset.
        var (body, store, _) = await RunAsync(
            $"""<img src="data:image/gif;base64,{Gif1x1}" alt="one"><img src="data:image/gif;base64,{Gif1x1}" alt="two">""");

        var uid = store.Items[0].Uid.ToString();
        Assert.Multiple(() =>
        {
            Assert.That(store.Uploads, Is.EqualTo(1), "the same bytes must upload once");
            Assert.That(System.Text.RegularExpressions.Regex.Matches(body, uid).Count, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task DifferentBytes_UploadSeparately()
    {
        var (_, store, _) = await RunAsync(
            $"""<img src="data:image/gif;base64,{Gif1x1}" alt="a"><img src="data:image/gif;base64,{Gif1x1Red}" alt="b">""");

        Assert.That(store.Uploads, Is.EqualTo(2));
    }

    [Test]
    public async Task FileNameComesFromAltText()
    {
        var (_, store, _) = await RunAsync(
            $"""<img src="data:image/gif;base64,{Gif1x1}" alt="Melody Valkyrie: Huntress of Norp">""");

        Assert.That(store.Items[0].FileName, Is.EqualTo("melody-valkyrie-huntress-of-norp.gif"));
    }

    [Test]
    public async Task WidthHeightAndClass_AreCarriedOntoTheComponent()
    {
        var (body, _, _) = await RunAsync(
            $"""<img src="data:image/gif;base64,{Gif1x1}" alt="x" width="120" height="80" class="book-cover">""");

        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Contain(@"width=""120"""));
            Assert.That(body, Does.Contain(@"height=""80"""));
            // MediaImage's parameter is CssClass, so a plain class attribute would be dropped silently.
            Assert.That(body, Does.Contain(@"cssClass=""book-cover"""));
        });
    }

    [Test]
    public async Task Base64ThatFailsToDecode_IsLeftInline_AndReportsFailure()
    {
        // "ABCDE" is a legal base64 alphabet but an illegal length, so it matches the pattern and then
        // fails to decode. Destroying content we could not decode would be far worse than leaving it be.
        var (body, store, exit) = await RunAsync(
            """<img src="data:image/gif;base64,ABCDE" alt="broken">""");

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(1), "a failure must be reported, not swallowed");
            Assert.That(store.Uploads, Is.Zero);
            Assert.That(body, Does.Contain("base64,"), "the original markup must survive");
        });
    }

    [Test]
    public async Task SrcOutsideTheBase64Alphabet_IsNotTreatedAsAnInlineImage()
    {
        // Not a failure — it never looked like an inline image, so there is nothing to lift and nothing
        // to report. It must simply be left exactly as written.
        var original = """<img src="data:image/gif;base64,!!!!not-valid-base64!!!!" alt="broken">""";

        var (body, store, exit) = await RunAsync(original);

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.Zero);
            Assert.That(store.Uploads, Is.Zero);
            Assert.That(body, Is.EqualTo(original));
        });
    }

    [Test]
    public async Task DryRun_ChangesNothing()
    {
        var original = $"""<img src="data:image/gif;base64,{Gif1x1}" alt="x">""";

        var (body, store, _) = await RunAsync(original, "--dry-run");

        Assert.Multiple(() =>
        {
            Assert.That(body, Is.EqualTo(original));
            Assert.That(store.Uploads, Is.Zero);
        });
    }

    [Test]
    public async Task PageWithNoInlineImages_IsUntouched()
    {
        var original = "<p>Just markup, and a mention of base64, in prose.</p>";

        var (body, store, exit) = await RunAsync(original);

        Assert.Multiple(() =>
        {
            Assert.That(body, Is.EqualTo(original));
            Assert.That(store.Uploads, Is.Zero);
            Assert.That(exit, Is.Zero);
        });
    }

    [Test]
    public async Task SingleQuotedSrc_IsAlsoRecognised()
    {
        var (body, store, _) = await RunAsync(
            $"<img src='data:image/gif;base64,{Gif1x1}' alt='q'>");

        Assert.Multiple(() =>
        {
            Assert.That(store.Uploads, Is.EqualTo(1));
            Assert.That(body, Does.Not.Contain("base64,"));
        });
    }

    // ---- CSS: url(data:…) has no component to become, so it points at the raw media endpoint ----

    [Test]
    public async Task CssDataUrl_BecomesTheRawMediaEndpoint()
    {
        var (_, css, store, exit) = await RunFullAsync(
            null, $$""":root { --logo: url("data:image/gif;base64,{{Gif1x1}}"); }""");

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.Zero);
            Assert.That(store.Uploads, Is.EqualTo(1));
            Assert.That(css, Does.Not.Contain("base64,"));
            Assert.That(css, Does.Contain($"url(/_media/{store.Items[0].Uid})"));
        });
    }

    [Test]
    public async Task CssAsset_IsNamedAfterItsCustomProperty()
    {
        // Admin -> Media should read as names, not frontpage-1.gif.
        var (_, _, store, _) = await RunFullAsync(
            null, $$""":root { --bg-abstract-dark: url("data:image/gif;base64,{{Gif1x1}}"); }""");

        Assert.That(store.Items[0].FileName, Is.EqualTo("bg-abstract-dark.gif"));
    }

    [Test]
    public async Task UnquotedCssUrl_IsAlsoRecognised()
    {
        var (_, css, store, _) = await RunFullAsync(
            null, $$""".x { background: url(data:image/gif;base64,{{Gif1x1}}); }""");

        Assert.Multiple(() =>
        {
            Assert.That(store.Uploads, Is.EqualTo(1));
            Assert.That(css, Does.Not.Contain("base64,"));
        });
    }

    [Test]
    public async Task BodyAndCssShareOneAssetWhenTheBytesMatch()
    {
        // The same logo inlined in both markup and stylesheet must not become two assets.
        var (body, css, store, _) = await RunFullAsync(
            $"""<img src="data:image/gif;base64,{Gif1x1}" alt="logo">""",
            $$""":root { --logo: url("data:image/gif;base64,{{Gif1x1}}"); }""");

        var uid = store.Items[0].Uid.ToString();
        Assert.Multiple(() =>
        {
            Assert.That(store.Uploads, Is.EqualTo(1));
            Assert.That(body, Does.Contain(uid));
            Assert.That(css, Does.Contain(uid));
        });
    }

    [Test]
    public async Task TruncatedCssBase64_IsLeftInline_AndReportsFailure()
    {
        // This is the real shape of the two dead mindattic.com backgrounds: valid alphabet, bad length.
        var (_, css, store, exit) = await RunFullAsync(
            null, """:root { --broken: url("data:image/png;base64,ABCDE"); }""");

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(store.Uploads, Is.Zero);
            Assert.That(css, Does.Contain("base64,ABCDE"));
        });
    }
}
