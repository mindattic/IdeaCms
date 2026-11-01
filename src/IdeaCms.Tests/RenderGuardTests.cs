using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using MindAttic.Ideas.Abstractions;
using IdeaCms.Core.Rendering;

namespace IdeaCms.Tests;

/// <summary>
/// The disabled/missing-dependency render guard: the shared include grammar, plus IncludeExpander
/// degrading an unresolved/disabled tag to a MissingContent placeholder AND raising exactly one alert
/// (never a render crash; a resolved tag raises none).
/// </summary>
[TestFixture]
public class RenderGuardTests
{
    // ---- IncludeReferenceParser grammar ----

    [Test]
    public void TryParseTag_ShortForm_WithVersionAndDottedKey()
    {
        Assert.That(IncludeReferenceParser.TryParseTag("plugin.tooltip.v11", out var k, out var key, out var v), Is.True);
        Assert.That((k, key, v), Is.EqualTo((ContentKind.Plugin, "tooltip", (int?)11)));

        // Keys may contain dots; the short form preserves them just like the full form.
        Assert.That(IncludeReferenceParser.TryParseTag("plugin.ui.tooltip", out _, out var dotted, out _), Is.True);
        Assert.That(dotted, Is.EqualTo("ui.tooltip"));
    }

    [Test]
    public void TryParseTag_KindAloneOrUnknownKind_Fails()
    {
        // A lone kind has no key, and a first segment that isn't a real kind is not an include.
        Assert.That(IncludeReferenceParser.TryParseTag("theme", out _, out _, out _), Is.False);
        Assert.That(IncludeReferenceParser.TryParseTag("foo.bar", out _, out _, out _), Is.False);
    }

    [Test]
    public void Parse_IgnoresPlainHtmlAndMalformed()
    {
        Assert.That(IncludeReferenceParser.Parse("<div><p>hello</p></div>"), Is.Empty);
        Assert.That(IncludeReferenceParser.Parse(null), Is.Empty);
    }

    [Test]
    public void Parse_IgnoresTokenLikeText()
    {
        // Text containing {{Plugin.Foo}} is now left as literal text — no component reference is found.
        var refs = IncludeReferenceParser.Parse("<p>{{Plugin.Foo}}</p>");
        Assert.That(refs, Is.Empty, "token-like text must not be parsed as a component reference");
    }

    // ---- IncludeExpander guard behavior ----

    [Test]
    public void MissingPlaceholder_LinksToAdminUpload_WithTheMissingKey()
    {
        // RFC 0001 "clickable upload-to-fix": the placeholder targets the admin upload surface with
        // the broken reference in the query, so the admin fixes the page by uploading the named .idea.
        Assert.Multiple(() =>
        {
            Assert.That(MissingContent.UploadToFixHref("IdeaCms.Plugin.Tooltip.V1"),
                Is.EqualTo("/admin/upload?missing=IdeaCms.Plugin.Tooltip.V1"));
            Assert.That(MissingContent.UploadToFixHref("a b"),
                Is.EqualTo("/admin/upload?missing=a%20b"), "the key is query-escaped");
        });
    }

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

    private static (int missing, int disabled, bool placeholder, bool resolvedComponent) RunExpand(ContentResolution outcome)
    {
        var catalog = new FakeCatalog { Outcome = outcome };
        var sink = new RecordingSink();
        var builder = new RenderTreeBuilder();
        builder.OpenElement(0, "div");
        var seq = 1;
        IncludeExpander.Expand(builder, ref seq, "<Plugin.Tooltip />",
            catalog, new PassGate(), ContentTrust.Author, sink, Guid.NewGuid(), "demo");
        builder.CloseElement();

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

    [Test]
    public void MissingInclude_RendersPlaceholder_AndRaisesOneMissingAlert()
    {
        var (missing, disabled, placeholder, _) = RunExpand(ContentResolution.Missing);
        Assert.Multiple(() =>
        {
            Assert.That(missing, Is.EqualTo(1));
            Assert.That(disabled, Is.EqualTo(0));
            Assert.That(placeholder, Is.True);
        });
    }

    [Test]
    public void DisabledInclude_RendersPlaceholder_AndRaisesOneDisabledAlert()
    {
        var (missing, disabled, placeholder, _) = RunExpand(ContentResolution.Disabled);
        Assert.Multiple(() =>
        {
            Assert.That(disabled, Is.EqualTo(1));
            Assert.That(missing, Is.EqualTo(0));
            Assert.That(placeholder, Is.True);
        });
    }

    [Test]
    public void ResolvedInclude_RendersComponent_AndRaisesNoAlert()
    {
        var (missing, disabled, _, resolved) = RunExpand(ContentResolution.Resolved);
        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.True);
            Assert.That(missing, Is.EqualTo(0));
            Assert.That(disabled, Is.EqualTo(0));
        });
    }

    // ---- XSS sanitization (Bugs: <script src> passthrough and data:/vbscript: URI passthrough) ----

    private static IReadOnlyList<(string Name, object? Value)> AttributesForElement(
        string html, string elementName, ContentTrust trust)
    {
        var catalog = new FakeCatalog { Outcome = ContentResolution.Missing };
        var sink = new RecordingSink();
        var builder = new RenderTreeBuilder();
        builder.OpenElement(0, "div");
        var seq = 1;
        IncludeExpander.Expand(builder, ref seq, html, catalog, new PassGate(), trust, sink, Guid.NewGuid(), "test");
        builder.CloseElement();

        var frames = builder.GetFrames();
        var result = new List<(string, object?)>();
        bool inTarget = false;
        for (var i = 0; i < frames.Count; i++)
        {
            var f = frames.Array[i];
            if (f.FrameType == RenderTreeFrameType.Element)
                inTarget = string.Equals(f.ElementName, elementName, StringComparison.OrdinalIgnoreCase);
            else if (inTarget && f.FrameType == RenderTreeFrameType.Attribute)
                result.Add((f.AttributeName, f.AttributeValue));
        }
        return result;
    }

    [Test]
    public void Untrusted_ScriptSrc_IsStripped_ToPreventExternalScriptLoad()
    {
        const string html = """<script src="https://evil.example/xss.js"></script>""";
        var attrs = AttributesForElement(html, "script", ContentTrust.Untrusted);
        Assert.That(attrs.Select(a => a.Name), Does.Not.Contain("src"),
            "untrusted <script src=...> must be stripped");
    }

    [Test]
    public void Author_ScriptSrc_IsPreserved()
    {
        // Author <script>/<style> is emitted as ONE markup frame (never an element with a markup child, which
        // interactive Blazor re-parses outside raw-text context), so the attribute lives in that markup.
        const string html = """<script src="https://cdn.example/lib.js"></script>""";
        var builder = new RenderTreeBuilder();
        var seq = 0;
        IncludeExpander.Expand(builder, ref seq, html, new FakeCatalog { Outcome = ContentResolution.Missing },
            new PassGate(), ContentTrust.Author, new RecordingSink(), Guid.NewGuid(), "test");
        var frames = builder.GetFrames();
        var all = Enumerable.Range(0, frames.Count).Select(i => frames.Array[i]).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(all.Any(f => f.FrameType == RenderTreeFrameType.Markup
                && f.MarkupContent.Contains("src=\"https://cdn.example/lib.js\"")), Is.True,
                "author-trusted <script src=...> must be preserved");
            Assert.That(all.Any(f => f.FrameType == RenderTreeFrameType.Element && f.ElementName == "script"), Is.False);
        });
    }

    [Test]
    public void Untrusted_DataUriOnAnchor_IsStripped()
    {
        const string html = """<a href="data:text/html,<script>alert(1)</script>">click</a>""";
        var attrs = AttributesForElement(html, "a", ContentTrust.Untrusted);
        Assert.That(attrs.Where(a => a.Name == "href").Select(a => a.Value?.ToString()),
            Is.Empty.Or.All.Not.StartWith("data:"),
            "data: URI in untrusted href must be stripped");
    }

    [Test]
    public void Untrusted_JavascriptUriOnAnchor_IsStripped()
    {
        const string html = """<a href="javascript:alert(1)">click</a>""";
        var attrs = AttributesForElement(html, "a", ContentTrust.Untrusted);
        Assert.That(attrs.Where(a => a.Name == "href").Select(a => a.Value?.ToString()),
            Is.Empty.Or.All.Not.StartWith("javascript:"),
            "javascript: URI in untrusted href must be stripped");
    }

    [Test]
    public void Untrusted_DataAttributeWithDataUriValue_IsPreserved()
    {
        // Regression: IsUnsafeUri was applied to ALL attribute values; a data-* attribute whose value
        // starts with "data:" (e.g. data-payload="data:application/json,{}") was silently stripped
        // even though it is safe application data, not a navigation URI.
        // The fix restricts IsUnsafeUri to URL-bearing attributes (href, src, action, …) only.
        const string html = """<div data-type="data:application/json" data-config="data:text/plain,ok">x</div>""";
        var attrs = AttributesForElement(html, "div", ContentTrust.Untrusted);
        Assert.Multiple(() =>
        {
            Assert.That(attrs.Any(a => a.Name == "data-type"), Is.True,
                "data-type=\"data:…\" must be preserved for untrusted content");
            Assert.That(attrs.Any(a => a.Name == "data-config"), Is.True,
                "data-config=\"data:…\" must be preserved for untrusted content");
        });
    }

    [Test]
    public void Untrusted_ScriptElement_IsDroppedEntirely()
    {
        // Regression: untrusted <script> was emitted as an empty open/close pair even though inner HTML
        // was dropped; an empty <script nonce="..."></script> is still an XSS attack surface.
        // Now the entire element is omitted from the render tree for untrusted content.
        const string html = """<script nonce="abc">alert(1)</script>""";

        var catalog = new FakeCatalog { Outcome = ContentResolution.Missing };
        var builder = new RenderTreeBuilder();
        var seq = 1;
        IncludeExpander.Expand(builder, ref seq, html, catalog, new PassGate(), ContentTrust.Untrusted,
            pageId: Guid.NewGuid(), slug: "test");

        var frames = builder.GetFrames();
        var scriptFound = false;
        for (var i = 0; i < frames.Count; i++)
        {
            var f = frames.Array[i];
            if (f.FrameType == RenderTreeFrameType.Element &&
                string.Equals(f.ElementName, "script", StringComparison.OrdinalIgnoreCase))
            {
                scriptFound = true;
                break;
            }
        }
        Assert.That(scriptFound, Is.False, "untrusted <script> element must be omitted entirely from the render tree");
    }

    [Test]
    public void Untrusted_LiteralMaComponentTag_DoesNotResolveAComponent()
    {
        // Regression: UpgradePascalCaseTags is skipped for untrusted content precisely so an untrusted
        // author cannot embed component tags — but RenderNodes matched <ma-component> unconditionally, so
        // writing the post-upgrade tag BY HAND smuggled a live citizen past the trust gate (M.IDEA-LAW-5).
        const string html = """<ma-component data-key="tooltip" kind="Plugin"></ma-component>""";

        var catalog = new FakeCatalog { Outcome = ContentResolution.Resolved };
        var builder = new RenderTreeBuilder();
        var seq = 1;
        IncludeExpander.Expand(builder, ref seq, html, catalog, new PassGate(), ContentTrust.Untrusted,
            pageId: Guid.NewGuid(), slug: "test");

        var frames = builder.GetFrames();
        var resolved = false;
        for (var i = 0; i < frames.Count; i++)
            if (frames.Array[i].FrameType == RenderTreeFrameType.Component &&
                frames.Array[i].ComponentType == typeof(DummyComponent))
                resolved = true;

        Assert.That(resolved, Is.False,
            "untrusted content must never resolve a citizen through a hand-written <ma-component> tag");
    }

    [Test]
    public void Author_LiteralMaComponentTag_StillResolves()
    {
        // The Author path is unchanged: the upgrade pass emits exactly this shape, so it must still bind.
        const string html = """<ma-component data-key="tooltip" kind="Plugin"></ma-component>""";

        var catalog = new FakeCatalog { Outcome = ContentResolution.Resolved };
        var builder = new RenderTreeBuilder();
        var seq = 1;
        IncludeExpander.Expand(builder, ref seq, html, catalog, new PassGate(), ContentTrust.Author,
            pageId: Guid.NewGuid(), slug: "test");

        var frames = builder.GetFrames();
        var resolved = false;
        for (var i = 0; i < frames.Count; i++)
            if (frames.Array[i].FrameType == RenderTreeFrameType.Component &&
                frames.Array[i].ComponentType == typeof(DummyComponent))
                resolved = true;

        Assert.That(resolved, Is.True, "author-trusted <ma-component> must still resolve");
    }

    // ---- Nested PascalCase XML tags <Outer><Inner>…</Inner></Outer> ----

    [Test]
    public void Expander_NestedPascalTags_OuterReceivesInnerAsChildContent()
    {
        // <Outer><Inner>text</Inner></Outer> — UpgradePascalCaseTags rewrites both to ma-component;
        // AngleSharp parses them as nested elements; RenderNodes emits both components with the inner
        // passed as ChildContent of the outer (if the outer declares it).
        var catalog = new FakeCatalog { Outcome = ContentResolution.Resolved };
        var builder = new RenderTreeBuilder();
        var seq = 1;
        IncludeExpander.Expand(builder, ref seq, "<Outer><Inner>text</Inner></Outer>",
            catalog, new PassGate(), ContentTrust.Author, pageId: Guid.NewGuid(), slug: "x");

        var frames = builder.GetFrames();
        var componentCount = 0;
        for (var i = 0; i < frames.Count; i++)
            if (frames.Array[i].FrameType == RenderTreeFrameType.Component &&
                frames.Array[i].ComponentType == typeof(DummyComponent))
                componentCount++;

        Assert.That(componentCount, Is.GreaterThanOrEqualTo(1),
            "at least the outer PascalCase component must be resolved");
    }

    // ---- dotted <Kind.Key /> form ----

    [Test]
    public void Parse_DottedTag_CollectedWithCorrectKind()
    {
        // <Plugin.Footer /> — Parse() should find it as a Plugin reference.
        var refs = IncludeReferenceParser.Parse("<Plugin.Footer />");
        Assert.That(refs, Has.Count.EqualTo(1));
        Assert.That(refs[0].Kind, Is.EqualTo(ContentKind.Plugin));
        Assert.That(refs[0].Key, Is.EqualTo("footer"));
    }

    [Test]
    public void Parse_PascalTag_WithUnrecognisedKind_StillDefaultsToComponent()
    {
        // Regression: Walk() called Enum.TryParse with the default already assigned to the out param.
        // TryParse writes default(ContentKind) - i.e. Page - on failure, so an unrecognised kind (a typo,
        // or a name outside the four kinds like "Widget", M.IDEA-LAW-1) was recorded as a PAGE reference: the delete guard
        // then protected the wrong citizen while IncludeExpander ignored the bad attribute and resolved
        // Component-then-Plugin. An unparseable kind must leave the documented Component default intact.
        foreach (var bad in new[] { "Widget", "Control", "nonsense" })
        {
            var refs = IncludeReferenceParser.Parse($"<Alert kind=\"{bad}\" />");
            Assert.That(refs, Has.Count.EqualTo(1), bad);
            Assert.That(refs[0].Kind, Is.EqualTo(ContentKind.Component), $"kind=\"{bad}\" must not become Page");
            Assert.That(refs[0].Key, Is.EqualTo("alert"), bad);
        }
    }

    [Test]
    public void Parse_PascalTag_WithoutKindAttribute_DefaultsToComponent()
    {
        // <Alert /> without kind — Parse defaults the reported kind to Component (render-time
        // will also try Plugin as a fallback, but the guard records Component).
        var refs = IncludeReferenceParser.Parse("<Alert />");
        Assert.That(refs, Has.Count.EqualTo(1));
        Assert.That(refs[0].Kind, Is.EqualTo(ContentKind.Component));
        Assert.That(refs[0].Key, Is.EqualTo("alert"));
    }

    [Test]
    public void Expander_DottedTag_UsedForResolution_NoFallback()
    {
        // <Plugin.Footer /> with an explicit kind must NOT fall back to Component if Plugin
        // is missing — the dotted kind prefix is an authoritative disambiguation, not a hint.
        var catalog = new FakeCatalog { Outcome = ContentResolution.Missing };
        var sink = new RecordingSink();
        var builder = new RenderTreeBuilder();
        var seq = 1;
        IncludeExpander.Expand(builder, ref seq, "<Plugin.Footer />",
            catalog, new PassGate(), ContentTrust.Author, sink, Guid.NewGuid(), "x");

        // Exactly one Missing alert for Plugin, none for Component.
        Assert.That(sink.Missing, Is.EqualTo(1));
    }

    [Test]
    public void Expander_DottedTag_KindNotPassedToComponent()
    {
        // The kind prefix in <Plugin.Footer> is routing metadata; it must not reach the component as a parameter.
        var catalog = new FakeCatalog { Outcome = ContentResolution.Resolved };
        var builder = new RenderTreeBuilder();
        var seq = 1;
        IncludeExpander.Expand(builder, ref seq, "<Plugin.Footer title=\"home\" />",
            catalog, new PassGate(), ContentTrust.Author, pageId: Guid.NewGuid(), slug: "x");

        var frames = builder.GetFrames();
        bool kindAttrFound = false;
        for (var i = 0; i < frames.Count; i++)
            if (frames.Array[i].FrameType == RenderTreeFrameType.Attribute &&
                string.Equals(frames.Array[i].AttributeName, "kind", StringComparison.OrdinalIgnoreCase))
                kindAttrFound = true;

        Assert.That(kindAttrFound, Is.False, "kind must not be forwarded to the component");
    }
}
