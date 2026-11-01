using Microsoft.EntityFrameworkCore;
using IdeaCms.Abstractions;
using IdeaCms.Core.Data;
using IdeaCms.Core.Entities;
using IdeaCms.Core.Services;
using IdeaCms.Tests.Portability;

namespace IdeaCms.Tests;

[TestFixture]
public class PackageCatalogServiceTests
{
    private sealed class InMemoryFactory(string db) : IDbContextFactory<CmsDbContext>
    {
        private readonly DbContextOptions<CmsDbContext> _opts =
            new DbContextOptionsBuilder<CmsDbContext>().UseInMemoryDatabase(db).Options;
        public CmsDbContext CreateDbContext() => new(_opts);
        public Task<CmsDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(CreateDbContext());
    }

    private static IDbContextFactory<CmsDbContext> NewFactory() =>
        new InMemoryFactory("pkgcat_" + Guid.NewGuid().ToString("N"));

    private static InstalledPackage Pkg(string category, string key, int version) => new()
    {
        Category = category, Key = key, Version = version,
        DisplayName = $"{key} V{version}",
        BlobPath = $"{category}/{key}/{version}.idea",
        Sha256 = new string('a', 64),
        IsActiveVersion = true, Enabled = true,
        InstalledUtc = DateTime.UtcNow,
    };

    [Test]
    public async Task ListAvailableAsync_FeedNotConfigured_ShortCircuits_NoFetcherCalls()
    {
        var fetcher = new FakeCatalogFetcher { IsConfigured = false };
        fetcher.Add("IdeaCms.Plugin.gallery", "1.0.0"); // present, but must never be read

        var result = await new PackageCatalogService(fetcher, NewFactory()).ListAvailableAsync();

        Assert.That(result.FeedConfigured, Is.False);
        Assert.That(result.Entries, Is.Empty);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public async Task ListAvailableAsync_ParsesKindAndKey_FromPackageId()
    {
        var fetcher = new FakeCatalogFetcher();
        fetcher.Add("IdeaCms.Plugin.gallery", "1.0.0", "2.0.0");
        fetcher.Add("IdeaCms.Theme.cyberspace", "1.0.0");

        var result = await new PackageCatalogService(fetcher, NewFactory()).ListAvailableAsync();

        Assert.That(result.FeedConfigured, Is.True);
        Assert.That(result.Error, Is.Null);
        Assert.That(result.Entries.Select(e => (e.Kind, e.Key, e.Version)), Is.EquivalentTo(new[]
        {
            (ContentKind.Plugin, "gallery", 1),
            (ContentKind.Plugin, "gallery", 2),
            (ContentKind.Theme, "cyberspace", 1),
        }));
    }

    [Test]
    public async Task ListAvailableAsync_KeyWithDots_ParsesWholeRemainderAsKey()
    {
        var fetcher = new FakeCatalogFetcher();
        fetcher.Add("IdeaCms.Component.media.image", "1.0.0");

        var result = await new PackageCatalogService(fetcher, NewFactory()).ListAvailableAsync();

        Assert.That(result.Entries.Single().Key, Is.EqualTo("media.image"));
    }

    [Test]
    public async Task ListAvailableAsync_SkipsUnparseableIds_WithoutThrowing()
    {
        var fetcher = new FakeCatalogFetcher();
        fetcher.Add("SomeOther.Package", "1.0.0");
        fetcher.Add("IdeaCms.NotAKind.foo", "1.0.0");
        fetcher.Add("IdeaCms.Plugin.gallery", "1.0.0");

        var result = await new PackageCatalogService(fetcher, NewFactory()).ListAvailableAsync();

        Assert.That(result.Entries.Select(e => e.Key), Is.EqualTo(new[] { "gallery" }));
    }

    [Test]
    public async Task ListAvailableAsync_CrossReferencesInstalledPackages()
    {
        var factory = NewFactory();
        await using (var db = factory.CreateDbContext())
        {
            db.InstalledPackages.Add(Pkg("Plugin", "gallery", 1));
            await db.SaveChangesAsync();
        }

        var fetcher = new FakeCatalogFetcher();
        fetcher.Add("IdeaCms.Plugin.gallery", "1.0.0", "2.0.0");

        var result = await new PackageCatalogService(fetcher, factory).ListAvailableAsync();

        var v1 = result.Entries.Single(e => e.Version == 1);
        var v2 = result.Entries.Single(e => e.Version == 2);
        Assert.That(v1.Installed, Is.True);
        Assert.That(v2.Installed, Is.False);
    }

    [Test]
    public async Task ListAvailableAsync_FetcherThrows_ReturnsError_DoesNotPropagate()
    {
        var fetcher = new FakeCatalogFetcher { ThrowOnListPackageIds = new HttpRequestException("feed unreachable") };

        var result = await new PackageCatalogService(fetcher, NewFactory()).ListAvailableAsync();

        Assert.That(result.FeedConfigured, Is.True);
        Assert.That(result.Entries, Is.Empty);
        Assert.That(result.Error, Does.Contain("feed unreachable"));
    }
}
