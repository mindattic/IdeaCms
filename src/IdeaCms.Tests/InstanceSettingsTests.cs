using Microsoft.AspNetCore.Components;
using IdeaCms.Abstractions;
using IdeaCms.Core.Rendering;

namespace IdeaCms.Tests;

/// <summary>M.IDEA-§4.5: a citizen's instance settings are its typed [Parameter]s, bound per instance.</summary>
[TestFixture]
public class InstanceSettingsTests
{
    public enum Size { Small, Large }

    private sealed class Sample : Abstractions.ComponentBase
    {
        [Parameter, Setting("Glow effect", Group = "Effects")] public bool Glow { get; set; } = true;
        [Parameter] public int Count { get; set; } = 3;
        [Parameter] public double Speed { get; set; } = 1.5;
        [Parameter] public Size Scale { get; set; } = Size.Large;
        [Parameter, Setting(Copyable = false)] public string? Caption { get; set; }
        [Parameter, Setting(Hidden = true)] public string? Plumbing { get; set; }
        [Parameter] public RenderFragment? ChildContent { get; set; }
        [Parameter] public List<string>? Unsupported { get; set; }
        public string NotAParameter { get; set; } = "";
    }

    [Test]
    public void Schema_ListsTypedParameters_WithMetadataAndDefaults()
    {
        var schema = SettingsSchema.For(typeof(Sample)).ToDictionary(d => d.Name);

        Assert.Multiple(() =>
        {
            Assert.That(schema.Keys, Is.EquivalentTo(new[] { "Glow", "Count", "Speed", "Scale", "Caption" }),
                "RenderFragments, hidden, unsupported types, the Attributes bag and non-parameters are not settings");
            Assert.That(schema["Glow"].DisplayName, Is.EqualTo("Glow effect"));
            Assert.That(schema["Glow"].Group, Is.EqualTo("Effects"));
            Assert.That(schema["Glow"].DefaultValue, Is.EqualTo("true"));
            Assert.That(schema["Count"].ValueKind, Is.EqualTo(SettingValueKind.Integer));
            Assert.That(schema["Speed"].DefaultValue, Is.EqualTo("1.5"));
            Assert.That(schema["Scale"].Choices, Is.EqualTo(new[] { "Small", "Large" }));
            Assert.That(schema["Caption"].Copyable, Is.False);
        });
    }

    [Test]
    public void Serialize_TypesValues_AndDropsUnknownOrUnparsable()
    {
        var json = InstanceSettingsBinder.Serialize(typeof(Sample), new Dictionary<string, string?>
        {
            ["glow"] = "false", ["Count"] = "7", ["Speed"] = "not-a-number", ["Scale"] = "small",
            ["Caption"] = "Hi", ["Bogus"] = "x", ["Plumbing"] = "nope",
        });

        var stored = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        Assert.Multiple(() =>
        {
            Assert.That(stored.Select(kv => kv.Key), Is.EquivalentTo(new[] { "Glow", "Count", "Scale", "Caption" }));
            Assert.That(stored["Glow"]!.GetValue<bool>(), Is.False, "matched case-insensitively and stored as a JSON bool");
            Assert.That(stored["Count"]!.GetValue<long>(), Is.EqualTo(7));
            Assert.That(stored["Scale"]!.GetValue<string>(), Is.EqualTo("Small"), "enum normalized to its declared name");
        });
    }

    [Test]
    public void ToParameters_CoercesToDeclaredTypes()
    {
        var p = InstanceSettingsBinder.ToParameters(typeof(Sample), """{"Glow":false,"Count":7,"Scale":"Small","Nope":1}""");

        Assert.Multiple(() =>
        {
            Assert.That(p["Glow"], Is.EqualTo(false));
            Assert.That(p["Count"], Is.EqualTo(7));
            Assert.That(p["Scale"], Is.EqualTo(Size.Small));
            Assert.That(p.ContainsKey("Nope"), Is.False);
        });
    }

    [Test]
    public void ToParameters_MalformedJson_BindsNothing() =>
        Assert.That(InstanceSettingsBinder.ToParameters(typeof(Sample), "{not json"), Is.Empty);

    [Test]
    public void ThemeBase_ExposesPagePaddingDefault()
    {
        var d = SettingsSchema.Find(typeof(SampleTheme), "PagePadding");
        Assert.That(d?.DefaultValue, Is.EqualTo(".75rem 1rem"));
    }

    private sealed class SampleTheme : ThemeBase { }
}

[TestFixture]
public class BodyTagIndexTests
{
    private const string Body = """
        <section><!-- <Component.Ignored /> -->
          <Component.Tabs accent="red">
            <Component.MediaImage uid="abc" width="200" />
            <b>not a tag</b>
          </Component.Tabs>
          <style>/* <Component.AlsoIgnored /> */ .x{}</style>
          <Card kind="Component" data-version="2" radius='8px' bare />
        </section>
        """;

    [Test]
    public void Scan_FindsCitizenTags_WithNestingKindVersionAndAttributes()
    {
        var tags = BodyTagIndex.Scan(Body);

        Assert.That(tags.Select(t => t.Key), Is.EqualTo(new[] { "tabs", "mediaimage", "card" }),
            "comments, <style> content and ordinary HTML are not citizen tags");
        Assert.Multiple(() =>
        {
            Assert.That(tags[0].ExplicitKind, Is.EqualTo(ContentKind.Component));
            Assert.That(tags[0].SelfClosing, Is.False);
            Assert.That(tags[1].Depth, Is.EqualTo(1));
            Assert.That(tags[1].ParentIndex, Is.EqualTo(0));
            Assert.That(tags[1].Get("uid"), Is.EqualTo("abc"));
            Assert.That(tags[2].Depth, Is.EqualTo(0));
            Assert.That(tags[2].ExplicitKind, Is.EqualTo(ContentKind.Component), "kind= attribute names the kind");
            Assert.That(tags[2].Version, Is.EqualTo(2));
            Assert.That(tags[2].Get("radius"), Is.EqualTo("8px"));
        });
    }

    [Test]
    public void SetSettings_RewritesOnlyThatTag_KeepingOtherAttributesAndBytes()
    {
        var updated = BodyTagIndex.SetSettings(Body, 1, ["Uid", "Width", "Height"],
            new Dictionary<string, string?> { ["width"] = "320", ["Height"] = "a \"b\" & c", ["Uid"] = "abc" });

        Assert.Multiple(() =>
        {
            Assert.That(updated, Does.Contain("""<Component.MediaImage uid="abc" width="320" height="a &quot;b&quot; &amp; c" />"""));
            Assert.That(updated.Replace("""<Component.MediaImage uid="abc" width="320" height="a &quot;b&quot; &amp; c" />""", ""),
                Is.EqualTo(Body.Replace("""<Component.MediaImage uid="abc" width="200" />""", "")),
                "every other byte of the author's markup is untouched");
            Assert.That(BodyTagIndex.Scan(updated)[1].Get("height"), Is.EqualTo("a \"b\" & c"), "values round-trip");
        });
    }

    [Test]
    public void SetSettings_NullValue_RemovesTheAttribute_AndKeepsMetaAttributes()
    {
        var updated = BodyTagIndex.SetSettings(Body, 2, ["Radius"], new Dictionary<string, string?> { ["Radius"] = null });

        Assert.That(updated, Does.Contain("""<Card kind="Component" data-version="2" bare />"""));
    }
}

[TestFixture]
public class EffectivePluginsTests
{
    private const string Site = """["Plugin.navmenu","Plugin.footer"]""";

    [Test]
    public void NoSelection_InheritsTheSiteDefaults() =>
        Assert.That(Core.Services.PageAdminService.EffectivePlugins(null, Site), Is.EqualTo(new[] { "Plugin.navmenu", "Plugin.footer" }));

    [Test]
    public void ExplicitEmptySelection_MeansNoPlugins() =>
        Assert.That(Core.Services.PageAdminService.EffectivePlugins("[]", Site), Is.Empty);

    [Test]
    public void OwnSelection_Wins() =>
        Assert.That(Core.Services.PageAdminService.EffectivePlugins("""["Plugin.navmenu"]""", Site), Is.EqualTo(new[] { "Plugin.navmenu" }));
}
