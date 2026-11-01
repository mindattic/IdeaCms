using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using MindAttic.Ideas.Abstractions;
using IdeaCms.Core.Rendering;

namespace IdeaCms.Tests.Rendering;

/// <summary>
/// The CmsInclude (compiled-page) seam and the data-page PascalCase include tag MUST route through the same
/// resolve/render core (IncludeExpander.EmitInclude), so a compiled page's
/// <c>&lt;CmsInclude Ref="MindAttic.Ideas.Plugin.Tooltip.V11"/&gt;</c> degrades and alerts identically
/// to a data page's <c>&lt;Plugin.Tooltip data-version="11" /&gt;</c>. These tests assert that parity.
/// </summary>
[TestFixture]
public class CmsIncludeParityTests
{
    private sealed class DummyComponent : Microsoft.AspNetCore.Components.ComponentBase { }

    private sealed class FakeCatalog : IContentCatalog
    {
        public ContentResolution Outcome = ContentResolution.Missing;
        public IReadOnlyCollection<ContentDescriptor> All => Array.Empty<ContentDescriptor>();
        public ContentDescriptor? Find(ContentKind kind, string key, int version) => null;
        public ContentDescriptor? FindLatest(ContentKind kind, string key) => null;
        public Type? ResolveType(ContentDescriptor descriptor) => typeof(DummyComponent);
        public ResolvedContent ResolveTag(ContentKind kind, string key, int? version) =>
            Outcome == ContentResolution.Resolved
                ? new ResolvedContent(ContentResolution.Resolved, typeof(DummyComponent), null)
                : new ResolvedContent(Outcome, null, null);
    }

    private sealed class RecordingSink : IRenderAlertSink
    {
        public int Missing, Disabled;
        public void RaiseMissing(ContentKind k, string key, int? v, Guid p, string s) => Missing++;
        public void RaiseDisabled(ContentKind k, string key, int? v, Guid p, string s) => Disabled++;
    }

    private sealed class PassGate : IRawContentGate
    {
        public MarkupString Emit(string? html, ContentTrust trust) => new(html ?? "");
    }

    private sealed class FakePage : IPageContext
    {
        public Guid PageId { get; init; } = Guid.NewGuid();
        public string Slug { get; init; } = "demo";
        public string Title => "Demo";
        public string? SeoTitle => null;
        public string? ThemeKey => null;
        public int? ThemeVersion => null;
        public IInlineMarkup Inline => null!;
        public IReadOnlyDictionary<string, string?> Meta => new Dictionary<string, string?>();
    }

    private sealed class FakeRenderContext : IRenderContext
    {
        public Guid InstanceId { get; } = Guid.NewGuid();
        public ContentMode Mode => ContentMode.View;
        public CmsRenderMode RenderMode => CmsRenderMode.InteractiveServer;
        public IPageContext Page { get; } = new FakePage();
        public ISiteContext Site => null!;
        public IServiceProvider Services => null!;
        public string? RawSettingsJson => null;
        public T GetSettings<T>() where T : class, new() => new();
    }

    private static (int missing, int disabled, bool placeholder, bool resolved) Frames(RenderTreeBuilder builder, RecordingSink sink)
    {
        var frames = builder.GetFrames();
        bool placeholder = false, resolved = false;
        for (var i = 0; i < frames.Count; i++)
        {
            var f = frames.Array[i];
            if (f.FrameType != RenderTreeFrameType.Component) continue;
            if (f.ComponentType == typeof(MissingContent)) placeholder = true;
            if (f.ComponentType == typeof(DummyComponent)) resolved = true;
        }
        return (sink.Missing, sink.Disabled, placeholder, resolved);
    }

    // The data-page path (IncludeExpander over an HTML string with a PascalCase tag).
    private static (int, int, bool, bool) RunDataPage(ContentResolution outcome)
    {
        var sink = new RecordingSink();
        var b = new RenderTreeBuilder();
        var seq = 0;
        IncludeExpander.Expand(b, ref seq, "<Plugin.Tooltip data-version=\"11\" />",
            new FakeCatalog { Outcome = outcome }, new PassGate(), ContentTrust.Author, sink,
            new FakePage().PageId, "demo");
        return Frames(b, sink);
    }

    // The compiled-page path (IncludeRenderer behind CmsInclude) over the equivalent string id.
    private static (int, int, bool, bool) RunCodePage(ContentResolution outcome)
    {
        var sink = new RecordingSink();
        var renderer = new IncludeRenderer(new FakeCatalog { Outcome = outcome }, sink);
        var fragment = renderer.Render(new FakeRenderContext(), "MindAttic.Ideas.Plugin.Tooltip.V11");
        var b = new RenderTreeBuilder();
        fragment(b);
        return Frames(b, sink);
    }

    [TestCase(ContentResolution.Resolved)]
    [TestCase(ContentResolution.Missing)]
    [TestCase(ContentResolution.Disabled)]
    public void CmsInclude_MatchesDataPageInclude(ContentResolution outcome)
    {
        var data = RunDataPage(outcome);
        var code = RunCodePage(outcome);
        Assert.That(code, Is.EqualTo(data),
            "CmsInclude (compiled-page) must produce the same outcome + alerts as the data-page include tag.");
    }
}
