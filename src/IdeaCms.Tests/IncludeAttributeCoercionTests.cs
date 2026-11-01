using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using IdeaCms.Abstractions;
using IdeaCms.Core.Rendering;
using BlazorComponentBase = Microsoft.AspNetCore.Components.ComponentBase;

namespace IdeaCms.Tests;

/// <summary>
/// RFC 0001 typed-attribute coercion: a PascalCase HTML tag attribute that matches a declared typed
/// [Parameter] on the resolved component is converted to that type (bool/int/double/enum, with
/// Nullable unwrapped); unmatched attributes keep their raw string for the CaptureUnmatchedValues
/// bag; a value that can't convert falls back to the raw value (a render never throws).
/// </summary>
[TestFixture]
public class IncludeAttributeCoercionTests
{
    public enum Flavor { Plain, Spicy }

    private sealed class TypedWidget : BlazorComponentBase
    {
        [Parameter] public bool Flag { get; set; }
        [Parameter] public int Count { get; set; }
        [Parameter] public double Ratio { get; set; }
        [Parameter] public int? MaybeCount { get; set; }
        [Parameter] public Flavor Mode { get; set; }
        [Parameter] public string? Text { get; set; }
        [Parameter(CaptureUnmatchedValues = true)] public IDictionary<string, object>? Attributes { get; set; }
    }

    [TestCase(typeof(bool), "true", true)]
    [TestCase(typeof(bool), "TRUE", true)]
    [TestCase(typeof(int), "42", 42)]
    [TestCase(typeof(double), "1.5", 1.5)]
    [TestCase(typeof(int?), "7", 7)]
    [TestCase(typeof(string), "hello", "hello")]
    public void CoerceAttributeValue_ConvertsDeclaredTypes(Type target, string raw, object expected)
    {
        Assert.That(IncludeExpander.CoerceAttributeValue(target, raw), Is.EqualTo(expected));
    }

    [Test]
    public void CoerceAttributeValue_Enum_ByNameCaseInsensitive()
    {
        Assert.That(IncludeExpander.CoerceAttributeValue(typeof(Flavor), "spicy"), Is.EqualTo(Flavor.Spicy));
    }

    [Test]
    public void CoerceAttributeValue_BadValue_FallsBackToRaw_NeverThrows()
    {
        Assert.That(IncludeExpander.CoerceAttributeValue(typeof(int), "not-a-number"), Is.EqualTo("not-a-number"));
        Assert.That(IncludeExpander.CoerceAttributeValue(typeof(bool), "yep"), Is.EqualTo("yep"));
    }

    [Test]
    public void Expand_TagAttributes_BindTyped_AndLeaveUnmatchedRaw()
    {
        // The full path a data page takes: PascalCase HTML tag -> IncludeExpander -> component frames.
        var builder = new RenderTreeBuilder();
        var seq = 0;
        IncludeExpander.Expand(builder, ref seq,
            "<Plugin.Typed flag=\"true\" count=\"42\" ratio=\"1.5\" maybecount=\"7\" mode=\"Spicy\" text=\"hi\" data-extra=\"raw\" />",
            new TypedCatalog(), new PassGate(), ContentTrust.Author);

        var attrs = ComponentAttributes(builder);
        Assert.Multiple(() =>
        {
            Assert.That(attrs["flag"], Is.EqualTo(true), "bool parameter coerces");
            Assert.That(attrs["count"], Is.EqualTo(42), "int parameter coerces");
            Assert.That(attrs["ratio"], Is.EqualTo(1.5), "double parameter coerces");
            Assert.That(attrs["maybecount"], Is.EqualTo(7), "nullable int parameter coerces");
            Assert.That(attrs["mode"], Is.EqualTo(Flavor.Spicy), "enum parameter coerces by name");
            Assert.That(attrs["text"], Is.EqualTo("hi"), "string parameter stays a string");
            Assert.That(attrs["data-extra"], Is.EqualTo("raw"), "unmatched attribute stays raw for the bag");
        });
    }

    // ---- infra ----

    private static Dictionary<string, object?> ComponentAttributes(RenderTreeBuilder builder)
    {
        var frames = builder.GetFrames();
        var attrs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var inComponent = false;
        for (var i = 0; i < frames.Count; i++)
        {
            var f = frames.Array[i];
            if (f.FrameType == RenderTreeFrameType.Component && f.ComponentType == typeof(TypedWidget))
            {
                inComponent = true;
                continue;
            }
            if (!inComponent) continue;
            if (f.FrameType == RenderTreeFrameType.Attribute) attrs[f.AttributeName] = f.AttributeValue;
            else break;   // first non-attribute frame ends the component's attribute run
        }
        Assert.That(attrs, Is.Not.Empty, "the tag must resolve to the TypedWidget component");
        return attrs;
    }

    private sealed class TypedCatalog : IContentCatalog
    {
        public IReadOnlyCollection<ContentDescriptor> All => Array.Empty<ContentDescriptor>();
        public ContentDescriptor? Find(ContentKind kind, string key, int version) => null;
        public ContentDescriptor? FindLatest(ContentKind kind, string key) => null;
        public Type? ResolveType(ContentDescriptor descriptor) => typeof(TypedWidget);
        public ResolvedContent ResolveTag(ContentKind kind, string key, int? version) =>
            new(ContentResolution.Resolved, typeof(TypedWidget), null);
    }

    private sealed class PassGate : IRawContentGate
    {
        public MarkupString Emit(string? html, ContentTrust trust) => new(html ?? "");
    }
}
