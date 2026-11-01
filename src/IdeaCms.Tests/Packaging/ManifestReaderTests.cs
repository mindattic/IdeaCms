using System.Text.Json;
using IdeaCms.Packaging;

namespace IdeaCms.Tests.Packaging;

[TestFixture]
public class ManifestReaderTests
{
    [Test]
    public void MinimalKernel_ParsesWithDocumentedDefaults()
    {
        const string json = """
            { "manifestVersion": 1, "category": "Plugin", "kind": "code",
              "key": "ui.tooltip", "version": 2, "displayName": "Tooltip" }
            """;
        var m = ManifestReader.Read(json);
        Assert.Multiple(() =>
        {
            Assert.That(m.Key, Is.EqualTo("ui.tooltip"));
            Assert.That(m.Version, Is.EqualTo(2));
            Assert.That(m.Category, Is.EqualTo("Plugin"));
            Assert.That(m.Kind, Is.EqualTo("code"));
            Assert.That(m.DisplayName, Is.EqualTo("Tooltip"));
            Assert.That(m.RenderMode, Is.EqualTo("InteractiveServer"));   // documented default
            Assert.That(m.Scope, Is.EqualTo("Placeable"));               // documented default
            Assert.That(m.Css, Is.Empty);
            Assert.That(m.AllowOverride, Is.False);
        });
    }

    [Test]
    public void UnknownField_RoundTripsLosslesslyThroughExtra()
    {
        const string json = """
            { "manifestVersion": 1, "category": "Theme", "kind": "code", "key": "cyberspace",
              "version": 1, "displayName": "Cyberspace", "futureField": { "nested": [1, 2, 3] } }
            """;
        var m = ManifestReader.Read(json);
        Assert.That(m.Extra.ContainsKey("futureField"), Is.True, "unknown field must be captured in Extra");

        // Re-serialize and re-read: the unknown field must survive.
        var again = ManifestReader.Read(ManifestReader.Write(m));
        Assert.That(again.Extra.ContainsKey("futureField"), Is.True);
        Assert.That(again.Extra["futureField"].GetProperty("nested").GetArrayLength(), Is.EqualTo(3));
    }

    [Test]
    public void CamelCaseMapping_StableForEveryKernelField()
    {
        var m = new IdeaManifest
        {
            ManifestVersion = 1, Category = "Page", Kind = "data", Key = "about", Version = 3, DisplayName = "About",
        };
        var json = ManifestReader.Write(m);
        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"manifestVersion\""));
            Assert.That(json, Does.Contain("\"category\""));
            Assert.That(json, Does.Contain("\"kind\""));
            Assert.That(json, Does.Contain("\"key\""));
            Assert.That(json, Does.Contain("\"version\""));
            Assert.That(json, Does.Contain("\"displayName\""));
        });
    }

    [Test]
    public void MalformedJson_Throws()
    {
        Assert.Throws<JsonException>(() => ManifestReader.Read("{ not valid json "));
    }
}
