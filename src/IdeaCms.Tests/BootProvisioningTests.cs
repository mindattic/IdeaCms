using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using IdeaCms.Abstractions;
using IdeaCms.Blazor;
using IdeaCms.Core.Data;
using IdeaCms.Core.Discovery;
using IdeaCms.Core.Portability;
using IdeaCms.Core.Services;
using IdeaCms.Tests.Packaging;
using MindAttic.Media;

namespace IdeaCms.Tests;

/// <summary>
/// The vanilla-vs-custom boot decision (M.IDEA-§4.9), extracted from `Program.cs`'s top-level statements
/// into <see cref="BootProvisioning"/> specifically so it could carry real coverage: no
/// <c>IdeaCms:Idealist</c> falls back to installing everything under <c>library/</c> exactly as before;
/// a configured one is applied via <see cref="IdeaListImporter"/>, and any failure propagates rather
/// than leaving a curated instance half-provisioned.
/// </summary>
[TestFixture]
public class BootProvisioningTests
{
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

    /// <summary>BootProvisioning's vanilla path never touches media; this only satisfies
    /// IdeaListImporter's constructor for the custom-boot path.</summary>
    private sealed class NullMediaStore : IMediaStore
    {
        public Task<MediaItem> UploadAsync(Stream content, string fileName, string contentType,
            int? tenantId = null, string folder = "", string mediaType = "", int? width = null,
            int? height = null, string? notes = null, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<MediaItem>> ListAsync(int? tenantId = null, string? folder = null,
            string? mediaType = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MediaItem>>([]);
        public Task<MediaItem?> GetMetaAsync(Guid uid, CancellationToken ct = default) => Task.FromResult<MediaItem?>(null);
        public Task<(MediaItem Meta, Stream Content)?> GetAsync(Guid uid, CancellationToken ct = default) =>
            Task.FromResult<(MediaItem, Stream)?>(null);
        public Task<bool> DeleteAsync(Guid uid, CancellationToken ct = default) => Task.FromResult(false);
    }

    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bootprov_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private IServiceProvider NewServices(out InMemoryFactory factory)
    {
        var localFactory = new InMemoryFactory("bootprov_" + Guid.NewGuid().ToString("N"));
        factory = localFactory;
        var catalog = new ContentCatalog(new NullResolver());
        var discovery = new DiscoveryService(localFactory, Array.Empty<ICmsContentSource>(), catalog);
        var installer = new PackageInstallService(
            localFactory, discovery, new InMemoryPackageBlobStore(), new NullPackageExtractor(), new NullRenderAlertSink(),
            new TestPackageSigningTrust(), new AdminInboxService(localFactory));

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<CmsDbContext>>(localFactory);
        services.AddScoped(_ => localFactory.CreateDbContext());
        services.AddSingleton<IMediaStore>(new NullMediaStore());
        services.AddSingleton<IPackageInstallService>(installer);
        return services.BuildServiceProvider();
    }

    private static void WriteIdealist(string path, IdeaList list)
    {
        using var file = File.Create(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        var entry = zip.CreateEntry(IdeaList.ManifestEntryName);
        using var stream = entry.Open();
        JsonSerializer.Serialize(stream, list, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    [Test]
    public async Task VanillaBoot_NoIdealistConfigured_InstallsEverythingInLibraryFolder()
    {
        var sp = NewServices(out var factory);
        var libraryDir = Path.Combine(_dir, "library");
        Directory.CreateDirectory(libraryDir);
        await File.WriteAllBytesAsync(Path.Combine(libraryDir, "a.idea"),
            IdeaTestArchive.CodePackage("tooltip", 1, "Plugin").ToArray());

        var config = new ConfigurationBuilder().Build();
        await BootProvisioning.ApplyAsync(sp, _dir, config, TextWriter.Null, TextWriter.Null);

        await using var db = factory.CreateDbContext();
        Assert.That(await db.InstalledPackages.CountAsync(), Is.EqualTo(1),
            "no IdeaCms:Idealist configured must fall back to installing everything under library/");
    }

    [Test]
    public async Task CustomBoot_AppliesTheConfiguredIdealist()
    {
        var sp = NewServices(out var factory);
        var libraryDir = Path.Combine(_dir, "library");
        Directory.CreateDirectory(libraryDir);
        await File.WriteAllBytesAsync(Path.Combine(libraryDir, "a.idea"),
            IdeaTestArchive.CodePackage("tooltip", 1, "Plugin").ToArray());

        var idealistPath = Path.Combine(_dir, "provision.idealist");
        WriteIdealist(idealistPath, new IdeaList { Packages = ["Plugin.tooltip@1"] });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["IdeaCms:Idealist"] = idealistPath })
            .Build();
        await BootProvisioning.ApplyAsync(sp, _dir, config, TextWriter.Null, TextWriter.Null);

        await using var db = factory.CreateDbContext();
        var pkg = await db.InstalledPackages.SingleAsync();
        Assert.That(pkg.Key, Is.EqualTo("tooltip"));
    }

    [Test]
    public void CustomBoot_BadIdealist_ThrowsRatherThanStartingPartially()
    {
        var sp = NewServices(out _);
        var idealistPath = Path.Combine(_dir, "bad.idealist");
        // A Uses[] entry nothing satisfies — the whole apply must throw, not degrade.
        WriteIdealist(idealistPath, new IdeaList
        {
            Pages = [new IdeaListPage { Uid = Guid.NewGuid(), Slug = "home", Title = "home", Uses = ["Plugin.ghost@1"] }],
        });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["IdeaCms:Idealist"] = idealistPath })
            .Build();

        Assert.ThrowsAsync<IdeaListImportException>(
            () => BootProvisioning.ApplyAsync(sp, _dir, config, TextWriter.Null, TextWriter.Null));
    }
}
