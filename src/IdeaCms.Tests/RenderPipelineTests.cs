using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.EntityFrameworkCore;
using IdeaCms.Abstractions;
using IdeaCms.Core.Data;
using IdeaCms.Core.Discovery;
using IdeaCms.Core.Rendering;
using IdeaCms.Core.Services;
using IdeaCms.Packaging;
using IdeaCms.Tests.Packaging;

namespace IdeaCms.Tests;

/// <summary>
/// End-to-end render pipeline: install a package → catalog reload → IncludeExpander resolves the
/// token to a Component frame (Resolved outcome, not a MissingContent placeholder). This is the
/// automatable portion of M.IDEA-US-F5/A6; the Blazor-host HTTP layer still needs an attended run.
///
/// The test uses <see cref="DefaultTypeResolver"/> so that a type already in the test process
/// (PluginBase, reachable by CLR name) resolves without ALC extraction — proving that the
/// install → discovery → catalog → include-expander chain works end-to-end.
/// </summary>
[TestFixture]
public class RenderPipelineTests
{
    private sealed class InMemoryFactory(string db) : IDbContextFactory<CmsDbContext>
    {
        private readonly DbContextOptions<CmsDbContext> _opts =
            new DbContextOptionsBuilder<CmsDbContext>().UseInMemoryDatabase(db).Options;
        public CmsDbContext CreateDbContext() => new(_opts);
        public Task<CmsDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(CreateDbContext());
    }

    private sealed class PassGate : IRawContentGate
    {
        public MarkupString Emit(string? html, ContentTrust trust) => new(html ?? "");
    }

    private static (PackageInstallService Svc, ContentCatalog Catalog) BuildPipeline()
    {
        var factory = new InMemoryFactory("pipeline_" + Guid.NewGuid().ToString("N"));
        var resolver = new DefaultTypeResolver();
        var catalog  = new ContentCatalog(resolver);
        var discovery = new DiscoveryService(factory, [], catalog);
        var blobs = new InMemoryPackageBlobStore();
        var svc = new PackageInstallService(factory, discovery, blobs, new NullPackageExtractor(), new NullRenderAlertSink(),
            new TestPackageSigningTrust(), new AdminInboxService(factory));
        return (svc, catalog);
    }

    // Concrete Plugin for test manifests. PluginBase is abstract; Activator.CreateInstance (called by
    // AllAssetsOf when harvesting asset URLs) would throw MemberAccessException on an abstract type.
    // DefaultTypeResolver scans all loaded assemblies, so it finds TestPlugin in this test assembly even
    // though the manifest names a fake "Demo" assembly.
    private sealed class TestPlugin : PluginBase
    {
        public override IReadOnlyList<string> StylesheetUrls => [];
        public override IReadOnlyList<string> ScriptUrls => [];
    }
    private static readonly string TestPluginTypeName = typeof(TestPlugin).FullName!;
    private const string FakeAsmName = "Demo";

    [Test]
    public async Task Install_ThenReload_ThenExpand_ProducesResolvedFrame()
    {
        // Arrange: package whose EntryType is PluginBase (resolvable by DefaultTypeResolver)
        var (svc, catalog) = BuildPipeline();
        var archive = IdeaTestArchive.Build(new Dictionary<string, string>
        {
            ["idea.json"] = ManifestReader.Write(new IdeaManifest
            {
                ManifestVersion = 1, Category = "Plugin", Kind = "code",
                Key = "testwidget", Version = 1,
                DisplayName = "Test Widget", Sdk = 1,
                EntryType = TestPluginTypeName,
                AssemblyName = FakeAsmName,
            }),
            [$"bin/{FakeAsmName}.dll"] = "MZ-fake",
        });

        // Act: install (includes ReloadCatalogAsync internally)
        var plan = await svc.InstallAsync(archive, allowOverride: false);

        // Catalog should now have the descriptor
        var desc = catalog.FindLatest(ContentKind.Plugin, "testwidget");
        Assert.That(desc, Is.Not.Null, "catalog should contain the installed widget");

        // IncludeExpander should produce a Component frame, not a Missing placeholder
        var builder = new RenderTreeBuilder();
        var seq = 0;
        IncludeExpander.Expand(builder, ref seq,
            "<Plugin.Testwidget data-version=\"1\" />",
            catalog, new PassGate(), ContentTrust.Author);

        var frames = builder.GetFrames();
        bool hasComponent = false, hasMissing = false;
        for (var i = 0; i < frames.Count; i++)
        {
            if (frames.Array[i].FrameType != RenderTreeFrameType.Component) continue;
            if (frames.Array[i].ComponentType == typeof(MissingContent)) hasMissing = true;
            else hasComponent = true;
        }

        Assert.Multiple(() =>
        {
            Assert.That(plan.Action, Is.EqualTo(InstallAction.Install));
            Assert.That(hasMissing, Is.False, "no MissingContent placeholder expected");
            Assert.That(hasComponent, Is.True,  "a Component frame for the resolved type is expected");
        });
    }

    [Test]
    public async Task Install_ThenExpand_UnknownToken_ProducesMissingFrame()
    {
        var (svc, catalog) = BuildPipeline();
        var archive = IdeaTestArchive.Build(new Dictionary<string, string>
        {
            ["idea.json"] = ManifestReader.Write(new IdeaManifest
            {
                ManifestVersion = 1, Category = "Plugin", Kind = "code",
                Key = "knownwidget", Version = 1, DisplayName = "Known", Sdk = 1,
                EntryType = TestPluginTypeName, AssemblyName = FakeAsmName,
            }),
            [$"bin/{FakeAsmName}.dll"] = "MZ-fake",
        });
        await svc.InstallAsync(archive, allowOverride: false);

        // Reference a widget that was NOT installed
        var builder = new RenderTreeBuilder();
        var seq = 0;
        IncludeExpander.Expand(builder, ref seq,
            "<Plugin.NotInstalled data-version=\"1\" />",
            catalog, new PassGate(), ContentTrust.Author);

        var frames = builder.GetFrames();
        bool hasMissing = false;
        for (var i = 0; i < frames.Count; i++)
        {
            if (frames.Array[i].FrameType == RenderTreeFrameType.Component &&
                frames.Array[i].ComponentType == typeof(MissingContent))
                hasMissing = true;
        }
        Assert.That(hasMissing, Is.True, "unknown token should degrade to MissingContent");
    }
}
